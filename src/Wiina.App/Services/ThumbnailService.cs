using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Wiina.Core.Caching;
using Wiina.Core.State;
using Wiina.Mpv;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Wiina.App.Services;

/// <summary>One decoded, already downscaled thumbnail (BGRA8, top-down).</summary>
public sealed record ThumbnailFrame(
    byte[] Bgra,
    int Width,
    int Height,
    double Time);

/// <summary>
/// Background thumbnail generator for the timeline hover peek. A second,
/// headless libmpv instance seeks the same file, captures <c>screenshot-raw</c>
/// and scales it before returning. The UI thread never performs file IO or
/// frame decoding.
///
/// Results live in three tiers: a bounded memory LRU for the current session, an
/// on-disk JPEG cache keyed by file identity (path, size, mtime, duration,
/// bucket, height) that survives restarts, and a background pre-generation pass
/// that warms the disk cache once a file is opened. Extraction runs on a single
/// worker so only one libmpv seek is ever in flight; hover requests are queued
/// ahead of pre-generation work.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private const int MaxSourceDimension = 16_384;
    private const int PreGenerationHeight = 90;

    private readonly object _gate = new();
    private readonly List<Job> _queue = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wake = new(0);
    private readonly ThumbnailCache<ThumbnailFrame> _cache;
    private readonly ThumbnailDiskCache? _disk;
    private readonly double _bucketSeconds;
    private MpvPlayer? _player;
    private TaskCompletionSource<bool>? _fileLoaded;
    private string? _loadedPath;
    private Task? _worker;
    private Thread? _workerThread;
    private CancellationTokenSource? _pregenCts;
    private DateTime _lastInteractionUtc = DateTime.UtcNow;
    private volatile bool _slowMode;
    private bool _disposed;

    public ThumbnailService(
        int cacheCount = 60,
        double bucketSeconds = 5,
        Action<string>? log = null,
        string? diskCacheDirectory = null,
        int diskCacheMegabytes = 512,
        bool slowMode = false)
    {
        _slowMode = slowMode;
        _bucketSeconds = double.IsFinite(bucketSeconds) && bucketSeconds > 0
            ? bucketSeconds
            : 5;
        _cache = new ThumbnailCache<ThumbnailFrame>(
            Math.Max(1, cacheCount),
            _bucketSeconds);
        Log = log;

        long budget = Math.Max(0, diskCacheMegabytes) * 1024L * 1024L;
        if (budget > 0)
        {
            string directory = string.IsNullOrWhiteSpace(diskCacheDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Wiina",
                    "thumbnails")
                : diskCacheDirectory;
            _disk = new ThumbnailDiskCache(directory);
            _disk.SetLimitBytes(budget);
        }
    }

    public Action<string>? Log { get; }

    public int CachedCount => _cache.Count;

    /// <summary>Whether an on-disk cache is active (a 0 MiB budget disables it).</summary>
    public bool DiskCacheEnabled => _disk is not null;

    /// <summary>Bytes currently occupied by the on-disk cache.</summary>
    public long DiskCacheBytes => _disk?.TotalBytes ?? 0;

    public bool TryGetCached(double seconds, out ThumbnailFrame frame) =>
        _cache.TryGet(seconds, out frame);

    /// <summary>
    /// Slow thumbnail mode: the single extraction worker waits between jobs and
    /// whole-file pre-generation is skipped, so weak devices keep a core free.
    /// Cached frames (memory and disk) are still served instantly, which is what
    /// keeps the first hover usable. Safe to toggle while playback runs.
    /// </summary>
    public bool SlowMode
    {
        get => _slowMode;
        set
        {
            if (_slowMode == value)
            {
                return;
            }

            _slowMode = value;
            if (value)
            {
                StopPreGeneration();
            }

            ApplyWorkerPriority();

            // Wake an idle worker so the new policy (or a queued job) takes
            // effect without waiting for the next request.
            try
            {
                _wake.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>Live-apply entry point used by the settings window.</summary>
    public void SetSlowMode(bool enabled) => SlowMode = enabled;

    /// <summary>
    /// Requests a thumbnail for <paramref name="seconds"/>. Concurrent requests
    /// for the same source/time share one extraction. The memory cache answers
    /// immediately, the disk cache answers after an IO hop, and anything else is
    /// queued on the single extraction worker. Stale callers are expected to
    /// ignore the result (the UI cancels its own request token).
    /// </summary>
    public async Task<ThumbnailFrame?> GetAsync(
        string? mediaPath,
        double seconds,
        double durationSeconds,
        int targetHeight = 90,
        CancellationToken cancellationToken = default,
        bool background = false)
    {
        if (_disposed || cancellationToken.IsCancellationRequested || !IsLocalFile(mediaPath))
        {
            return null;
        }

        if (!background)
        {
            _lastInteractionUtc = DateTime.UtcNow;
        }

        if (_cache.TryGet(seconds, out ThumbnailFrame cached))
        {
            return cached;
        }

        int height = ClampHeight(targetHeight);
        long bucket = _cache.KeyFor(seconds);
        double bucketTime = bucket * _bucketSeconds;

        if (_disk is not null && durationSeconds > 0)
        {
            var diskClock = Stopwatch.StartNew();
            ThumbnailFrame? diskFrame = await TryReadDiskAsync(
                mediaPath!,
                durationSeconds,
                bucketTime,
                height).ConfigureAwait(false);
            if (diskFrame is not null)
            {
                diskClock.Stop();
                _cache.Put(bucketTime, diskFrame);
                Log?.Invoke(
                    $"thumbnail {bucketTime:0.0}s from disk in " +
                    $"{diskClock.Elapsed.TotalMilliseconds:0} ms");
                return diskFrame;
            }
        }

        Job job = GetOrCreateJob(mediaPath!, bucketTime, durationSeconds, height, background);
        return await job.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Job GetOrCreateJob(
        string path,
        double seconds,
        double duration,
        int height,
        bool background)
    {
        string id = JobId(path, seconds, height);
        long bucket = _cache.KeyFor(seconds);
        lock (_gate)
        {
            if (_jobs.TryGetValue(id, out Job? existing))
            {
                if (!background && existing.Background)
                {
                    existing.Background = false;
                    ReprioritizeLocked(existing);
                }

                return existing;
            }

            var job = new Job
            {
                Id = id,
                Path = path,
                Seconds = seconds,
                Duration = duration,
                Height = height,
                Bucket = bucket,
                Background = background,
            };
            _jobs[id] = job;
            InsertLocked(job);
            EnsureWorkerLocked();
            _wake.Release();
            return job;
        }
    }

    /// <summary>
    /// Extracts every time bucket of <paramref name="mediaPath"/> into the disk
    /// cache in the background. A previous run for another file is cancelled.
    /// Pre-generated jobs run behind interactive hover requests.
    /// </summary>
    public void StartPreGeneration(
        string? mediaPath,
        double durationSeconds,
        int targetHeight = PreGenerationHeight)
    {
        StopPreGeneration();
        if (_slowMode)
        {
            // Slow mode never walks the timeline up front; frames are extracted
            // on demand while the user hovers.
            return;
        }

        if (_disposed
            || _disk is null
            || !IsLocalFile(mediaPath)
            || !double.IsFinite(durationSeconds)
            || durationSeconds <= 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _pregenCts = cts;
        string path = mediaPath!;
        int height = ClampHeight(targetHeight);
        _ = Task.Run(() => PreGenerateAsync(path, durationSeconds, height, cts.Token));
    }

    public void StopPreGeneration()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _pregenCts;
            _pregenCts = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    /// <summary>Applies a new disk budget live; 0 disables the cache and clears it.</summary>
    public void SetDiskCacheLimit(int megabytes)
    {
        long budget = Math.Max(0, megabytes) * 1024L * 1024L;
        _disk?.SetLimitBytes(budget);
        if (megabytes <= 0)
        {
            StopPreGeneration();
        }
    }

    /// <summary>Deletes every cached frame; returns the number of bytes freed.</summary>
    public long ClearDiskCache() => _disk?.Clear() ?? 0;

    private async Task PreGenerateAsync(
        string path,
        double duration,
        int height,
        CancellationToken ct)
    {
        long lastBucket = (long)Math.Floor(duration / _bucketSeconds);
        int extracted = 0;
        int reused = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            for (long bucket = 0; bucket <= lastBucket; bucket++)
            {
                if (ct.IsCancellationRequested || _disposed)
                {
                    return;
                }

                double seconds = bucket * _bucketSeconds;
                if (_cache.TryGet(seconds, out _))
                {
                    reused++;
                    continue;
                }

                if (_disk is not null
                    && await TryReadDiskAsync(path, duration, seconds, height).ConfigureAwait(false) is { } cached)
                {
                    _cache.Put(seconds, cached);
                    reused++;
                    continue;
                }

                ThumbnailFrame? frame = await GetAsync(
                    path,
                    seconds,
                    duration,
                    height,
                    ct,
                    background: true).ConfigureAwait(false);
                if (frame is not null)
                {
                    extracted++;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log?.Invoke("thumbnail pre-generation failed: " + ex.Message);
        }
        finally
        {
            clock.Stop();
            Log?.Invoke(
                $"thumbnail pre-generation: {extracted} extracted, {reused} cached, " +
                $"{lastBucket + 1} buckets in {clock.Elapsed.TotalSeconds:0.0}s");
        }
    }

    // ------------------------------------------------------------- worker loop

    private void EnsureWorkerLocked()
    {
        if (_worker is { IsCompleted: false })
        {
            return;
        }

        // A dedicated long-running thread (instead of a pool thread) lets slow
        // mode really lower the extraction priority for hover jobs.
        _worker = Task.Factory.StartNew(
            WorkerLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Applies the current policy's priority to the extraction thread. Slow mode
    /// runs below normal so the UI thread and mpv's render thread win the CPU;
    /// normal mode restores the default priority. Best effort on purpose.
    /// </summary>
    private void ApplyWorkerPriority()
    {
        Thread? thread = _workerThread;
        if (thread is null)
        {
            return;
        }

        try
        {
            thread.Priority = _slowMode ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
        }
        catch (Exception)
        {
            // Priority changes can be refused; extraction still throttles.
        }
    }

    private void WorkerLoop()
    {
        _workerThread = Thread.CurrentThread;
        _workerThread.IsBackground = true;
        ApplyWorkerPriority();
        try
        {
            while (true)
            {
                Job? job;
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    job = TakeNextLocked();
                }

                if (job is null)
                {
                    try
                    {
                        _wake.Wait(250);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    continue;
                }

                // Never let pre-generation compete with a fresh hover: the drag
                // preview feels laggy if a background seek occupies mpv.
                if (job.Background
                    && (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 1000)
                {
                    Thread.Sleep(120);
                }

                ThumbnailFrame? frame = null;
                try
                {
                    frame = ExtractAsync(job).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log?.Invoke(
                        $"thumbnail {job.Path} @ {job.Seconds:0.0}s: {ex.Message}");
                }

                job.Completion.TrySetResult(frame);
                lock (_gate)
                {
                    _jobs.Remove(job.Id);
                }

                if (_slowMode)
                {
                    // One-core mode: leave the CPU alone between frames so
                    // hovering still costs a single short spike.
                    Thread.Sleep(ThumbnailPolicy.SlowModeCooldownMilliseconds);
                }
            }
        }
        finally
        {
            _workerThread = null;
        }
    }

    private Job? TakeNextLocked()
    {
        if (_queue.Count == 0)
        {
            return null;
        }

        Job job = _queue[0];
        _queue.RemoveAt(0);
        return job;
    }

    private void InsertLocked(Job job)
    {
        int index = _queue.Count;
        for (int i = 0; i < _queue.Count; i++)
        {
            if (Compare(job, _queue[i]) < 0)
            {
                index = i;
                break;
            }
        }

        _queue.Insert(index, job);
    }

    private void ReprioritizeLocked(Job job)
    {
        _queue.Remove(job);
        InsertLocked(job);
    }

    private static int Compare(Job a, Job b)
    {
        int priority = (a.Background ? 1 : 0).CompareTo(b.Background ? 1 : 0);
        return priority != 0 ? priority : a.Bucket.CompareTo(b.Bucket);
    }

    private async Task<ThumbnailFrame?> ExtractAsync(Job job)
    {
        var clock = Stopwatch.StartNew();
        MpvPlayer? player = EnsurePlayer();
        if (player is null || !await LoadFileAsync(player, job.Path).ConfigureAwait(false))
        {
            return null;
        }

        await SeekAsync(player, job.Seconds).ConfigureAwait(false);
        MpvScreenshot? shot = Capture(player);
        ThumbnailFrame? frame = shot is null ? null : Scale(shot, job.Seconds, job.Height);
        if (frame is null)
        {
            shot = await CaptureToFileAsync(player).ConfigureAwait(false);
            frame = shot is null ? null : Scale(shot, job.Seconds, job.Height);
        }

        if (frame is null)
        {
            Log?.Invoke($"thumbnail {job.Seconds:0.0}s failed");
            return null;
        }

        _cache.Put(job.Seconds, frame);
        clock.Stop();
        long extractedMs = clock.ElapsedMilliseconds;
        if (_disk is not null && job.Duration > 0)
        {
            await WriteDiskAsync(job, frame).ConfigureAwait(false);
        }

        // Pre-generation extracts hundreds of frames; only log the interactive
        // requests, which are the ones whose latency matters to a user.
        if (!job.Background)
        {
            Log?.Invoke(
                $"thumbnail {job.Seconds:0.0}s extracted in {extractedMs} ms");
        }

        return frame;
    }

    private MpvPlayer? EnsurePlayer()
    {
        if (_player is { IsRunning: true })
        {
            return _player;
        }

        try
        {
            var player = new MpvPlayer();
            player.Log += message => Log?.Invoke("thumb mpv: " + message);
            player.FileLoaded += () => _fileLoaded?.TrySetResult(true);
            player.FileLoadFailed += (_, message) =>
                _fileLoaded?.TrySetException(
                    new InvalidOperationException(message ?? "thumbnail load failed"));
            player.Start(1280, 720, headless: true);
            _player = player;
            return player;
        }
        catch (Exception ex)
        {
            Log?.Invoke("thumbnail mpv start failed: " + ex.Message);
            _player?.Dispose();
            _player = null;
            return null;
        }
    }

    private async Task<bool> LoadFileAsync(MpvPlayer player, string path)
    {
        if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var loaded = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _fileLoaded = loaded;
        try
        {
            player.CommandAsync("loadfile", path, "replace");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
            _loadedPath = path;
            player.CommandAsync("set", "pause", "yes");
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"thumbnail load failed: {ex.Message}");
            _loadedPath = null;
            return false;
        }
        finally
        {
            _fileLoaded = null;
        }
    }

    private static async Task SeekAsync(MpvPlayer player, double seconds)
    {
        player.CommandAsync("set", "pause", "yes");
        player.CommandAsync(
            "seek",
            seconds.ToString("0.###", CultureInfo.InvariantCulture),
            "absolute",
            "exact");

        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(20).ConfigureAwait(false);
            double position = player.GetPropertyDouble("time-pos");
            bool seeking = player.GetPropertyFlag("seeking");
            if (!seeking && Math.Abs(position - seconds) < 0.75)
            {
                break;
            }
        }
    }

    private MpvScreenshot? Capture(MpvPlayer player)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                MpvScreenshot? shot = player.ScreenshotRaw();
                if (shot is not null)
                {
                    return shot;
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("screenshot-raw failed: " + ex.Message);
            }

            Thread.Sleep(80);
        }

        return null;
    }

    private async Task<MpvScreenshot?> CaptureToFileAsync(MpvPlayer player)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"wiina-thumb-{Guid.NewGuid():N}.png");
        try
        {
            player.CommandAsync("screenshot-to-file", path, "video");
            for (int i = 0; i < 80; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    return await DecodePngAsync(path).ConfigureAwait(false);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log?.Invoke("screenshot-to-file failed: " + ex.Message);
            return null;
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static async Task<MpvScreenshot?> DecodePngAsync(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return null;
        }

        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        return new MpvScreenshot(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            stride,
            "bgra",
            pixels);
    }

    // -------------------------------------------------------------- disk cache

    private async Task<ThumbnailFrame?> TryReadDiskAsync(
        string path,
        double duration,
        double seconds,
        int height)
    {
        ThumbnailDiskCache? disk = _disk;
        if (disk is null)
        {
            return null;
        }

        try
        {
            long bucket = _cache.KeyFor(seconds);
            ThumbnailDiskEntry? entry = await Task.Run(() =>
            {
                ThumbnailSource source = DescribeSource(path, duration, height);
                return disk.TryRead(source, bucket, out ThumbnailDiskEntry? found)
                    ? found
                    : null;
            }).ConfigureAwait(false);
            if (entry is null)
            {
                return null;
            }

            return await DecodeJpegAsync(entry.Data, bucket * _bucketSeconds)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke("thumbnail disk read failed: " + ex.Message);
            return null;
        }
    }

    private async Task WriteDiskAsync(Job job, ThumbnailFrame frame)
    {
        ThumbnailDiskCache? disk = _disk;
        if (disk is null)
        {
            return;
        }

        try
        {
            byte[]? jpeg = await EncodeJpegAsync(frame).ConfigureAwait(false);
            if (jpeg is null)
            {
                return;
            }

            await Task.Run(() =>
            {
                ThumbnailSource source = DescribeSource(
                    job.Path, job.Duration, job.Height);
                disk.Put(source, job.Bucket, jpeg, frame.Width, frame.Height);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke("thumbnail disk write failed: " + ex.Message);
        }
    }

    private ThumbnailSource DescribeSource(string path, double duration, int height)
    {
        long size = 0;
        long ticks = 0;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                size = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
            }
        }
        catch (Exception)
        {
            // A missing/unreadable file simply yields a key that never hits.
        }

        return new ThumbnailSource(
            path,
            size,
            ticks,
            duration,
            _bucketSeconds,
            height);
    }

    private static async Task<byte[]?> EncodeJpegAsync(ThumbnailFrame frame)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                BitmapEncoder.JpegEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                (uint)frame.Width,
                (uint)frame.Height,
                96,
                96,
                frame.Bgra);
            await encoder.FlushAsync();
            return await ReadStreamAsync(stream).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static async Task<ThumbnailFrame?> DecodeJpegAsync(byte[] jpeg, double seconds)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await stream.WriteAsync(jpeg.AsBuffer());
            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return null;
            }

            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyToBuffer(pixels.AsBuffer());
            return new ThumbnailFrame(
                pixels,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                seconds);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static async Task<byte[]> ReadStreamAsync(IRandomAccessStream stream)
    {
        uint size = checked((uint)stream.Size);
        var bytes = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static int ClampHeight(int height) => Math.Clamp(height, 24, 240);

    private static string JobId(string path, double seconds, int height) =>
        (ThumbnailDiskCache.NormalizePath(path)
            + "\u0001"
            + seconds.ToString("0.###", CultureInfo.InvariantCulture)
            + "\u0001"
            + height.ToString(CultureInfo.InvariantCulture));

    /// <summary>Converts BGRA/BGR0/RGBA/RGB0 and downscales with bilinear sampling.</summary>
    internal static ThumbnailFrame? Scale(MpvScreenshot shot, double time, int targetHeight)
    {
        if (shot.Width <= 0 || shot.Height <= 0 || targetHeight <= 0)
        {
            return null;
        }

        int height = Math.Clamp(targetHeight, 24, 240);
        int width = Math.Clamp(
            (int)Math.Round(height * (double)shot.Width / shot.Height),
            32,
            480);
        int sourceStride = Math.Abs(shot.Stride);
        if (sourceStride < shot.Width * 4 || shot.Pixels.Length < sourceStride)
        {
            return null;
        }

        bool bottomUp = shot.Stride < 0;
        int bpp = Math.Max(3, sourceStride / Math.Max(1, shot.Width));
        bpp = Math.Min(4, bpp);
        string format = shot.PixelFormat ?? "bgra";
        bool swapRedBlue = format is "rgba" or "rgb0" or "argb";
        bool hasAlpha = format is "bgra" or "rgba" or "argb";
        var output = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            double sy = ((y + 0.5) * shot.Height / height) - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sy), 0, shot.Height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, shot.Height - 1);
            double fy = Math.Clamp(sy - y0, 0, 1);
            int row0 = (bottomUp ? shot.Height - 1 - y0 : y0) * sourceStride;
            int row1 = (bottomUp ? shot.Height - 1 - y1 : y1) * sourceStride;

            for (int x = 0; x < width; x++)
            {
                double sx = ((x + 0.5) * shot.Width / width) - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, shot.Width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, shot.Width - 1);
                double fx = Math.Clamp(sx - x0, 0, 1);

                int i00 = row0 + (x0 * bpp);
                int i10 = row0 + (x1 * bpp);
                int i01 = row1 + (x0 * bpp);
                int i11 = row1 + (x1 * bpp);
                if (i11 + 2 >= shot.Pixels.Length)
                {
                    return null;
                }

                byte b0 = shot.Pixels[i00];
                byte g0 = shot.Pixels[i00 + 1];
                byte r0 = shot.Pixels[i00 + 2];
                byte a0 = hasAlpha && bpp > 3 ? shot.Pixels[i00 + 3] : (byte)255;
                byte b1 = shot.Pixels[i10];
                byte g1 = shot.Pixels[i10 + 1];
                byte r1 = shot.Pixels[i10 + 2];
                byte a1 = hasAlpha && bpp > 3 ? shot.Pixels[i10 + 3] : (byte)255;
                byte b2 = shot.Pixels[i01];
                byte g2 = shot.Pixels[i01 + 1];
                byte r2 = shot.Pixels[i01 + 2];
                byte a2 = hasAlpha && bpp > 3 ? shot.Pixels[i01 + 3] : (byte)255;
                byte b3 = shot.Pixels[i11];
                byte g3 = shot.Pixels[i11 + 1];
                byte r3 = shot.Pixels[i11 + 2];
                byte a3 = hasAlpha && bpp > 3 ? shot.Pixels[i11 + 3] : (byte)255;

                byte b = Blend(b0, b1, b2, b3, fx, fy);
                byte g = Blend(g0, g1, g2, g3, fx, fy);
                byte r = Blend(r0, r1, r2, r3, fx, fy);
                byte a = Blend(a0, a1, a2, a3, fx, fy);
                int index = ((y * width) + x) * 4;
                if (swapRedBlue)
                {
                    output[index] = r;
                    output[index + 1] = g;
                    output[index + 2] = b;
                }
                else
                {
                    output[index] = b;
                    output[index + 1] = g;
                    output[index + 2] = r;
                }

                output[index + 3] = a;
            }
        }

        return new ThumbnailFrame(output, width, height, time);
    }

    private static byte Blend(byte v00, byte v10, byte v01, byte v11, double fx, double fy)
    {
        double top = v00 + ((v10 - v00) * fx);
        double bottom = v01 + ((v11 - v01) * fx);
        return (byte)Math.Clamp(Math.Round(top + ((bottom - top) * fy)), 0, 255);
    }

    private static bool IsLocalFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp cleanup must never break playback.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPreGeneration();

        List<Job> pending;
        lock (_gate)
        {
            pending = new List<Job>(_queue);
            _queue.Clear();
            _jobs.Clear();
        }

        foreach (Job job in pending)
        {
            job.Completion.TrySetResult(null);
        }

        _wake.Release();
        _player?.Dispose();
        _player = null;
        _cache.Clear();
        _disk?.Flush();
    }

    private sealed class Job
    {
        public string Id { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public double Seconds { get; init; }
        public double Duration { get; init; }
        public int Height { get; init; }
        public long Bucket { get; init; }
        public bool Background { get; set; }
        public TaskCompletionSource<ThumbnailFrame?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
