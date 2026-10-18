using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Wiina.Core.Caching;
using Wiina.Mpv;
using Windows.Graphics.Imaging;
using Windows.Storage;

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
/// frame decoding. Results are cached by time bucket with a bounded LRU.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private const int MaxSourceDimension = 16_384;
    private readonly object _gate = new();
    private readonly Dictionary<long, Task<ThumbnailFrame?>> _inflight = new();
    private readonly SemaphoreSlim _extractGate = new(1, 1);
    private readonly ThumbnailCache<ThumbnailFrame> _cache;
    private readonly double _bucketSeconds;
    private MpvPlayer? _player;
    private TaskCompletionSource<bool>? _fileLoaded;
    private string? _loadedPath;
    private bool _disposed;

    public ThumbnailService(
        int cacheCount = 60,
        double bucketSeconds = 5,
        Action<string>? log = null)
    {
        _bucketSeconds = double.IsFinite(bucketSeconds) && bucketSeconds > 0
            ? bucketSeconds
            : 5;
        _cache = new ThumbnailCache<ThumbnailFrame>(
            Math.Max(1, cacheCount),
            _bucketSeconds);
        Log = log;
    }

    public Action<string>? Log { get; }

    public int CachedCount => _cache.Count;

    public bool TryGetCached(double seconds, out ThumbnailFrame frame) =>
        _cache.TryGet(seconds, out frame);

    /// <summary>
    /// Requests a thumbnail for <paramref name="seconds"/>. Concurrent requests
    /// for the same bucket share one extraction. Stale callers are expected to
    /// ignore the result (the UI cancels its own request token).
    /// </summary>
    public Task<ThumbnailFrame?> GetAsync(
        string? mediaPath,
        double seconds,
        int targetHeight = 90,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult<ThumbnailFrame?>(null);
        }

        if (!IsLocalFile(mediaPath))
        {
            return Task.FromResult<ThumbnailFrame?>(null);
        }

        if (_cache.TryGet(seconds, out ThumbnailFrame cached))
        {
            return Task.FromResult<ThumbnailFrame?>(cached);
        }

        long key = _cache.KeyFor(seconds);
        lock (_gate)
        {
            if (_inflight.TryGetValue(key, out Task<ThumbnailFrame?>? running))
            {
                return running;
            }

            Task<ThumbnailFrame?> task = Task.Run(
                () => ExtractAsync(mediaPath!, key, targetHeight));
            _inflight[key] = task;
            _ = task.ContinueWith(
                _ =>
                {
                    lock (_gate)
                    {
                        _inflight.Remove(key);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    private async Task<ThumbnailFrame?> ExtractAsync(
        string mediaPath,
        long bucket,
        int targetHeight)
    {
        try
        {
            await _extractGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return null;
                }

                double seconds = bucket * _bucketSeconds;
                MpvPlayer? player = EnsurePlayer();
                if (player is null || !await LoadFileAsync(player, mediaPath).ConfigureAwait(false))
                {
                    return null;
                }

                await SeekAsync(player, seconds).ConfigureAwait(false);
                MpvScreenshot? shot = Capture(player);
                ThumbnailFrame? frame = shot is null ? null : Scale(shot, seconds, targetHeight);
                if (frame is null)
                {
                    shot = await CaptureToFileAsync(player).ConfigureAwait(false);
                    frame = shot is null ? null : Scale(shot, seconds, targetHeight);
                }

                if (frame is null)
                {
                    Log?.Invoke($"thumbnail {seconds:0.0}s failed");
                    return null;
                }

                _cache.Put(seconds, frame);
                return frame;
            }
            finally
            {
                _extractGate.Release();
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"thumbnail {mediaPath} @ {bucket}: {ex.Message}");
            return null;
        }
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
        _player?.Dispose();
        _player = null;
        _cache.Clear();
        _extractGate.Dispose();
    }
}
