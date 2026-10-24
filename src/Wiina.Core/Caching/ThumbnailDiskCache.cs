using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wiina.Core.Caching;

/// <summary>
/// Identity of one thumbnail source. Every field participates in the cache key
/// so a changed file (size/mtime), duration, bucket size or thumbnail height
/// can never return a stale frame.
/// </summary>
public sealed record ThumbnailSource(
    string Path,
    long FileSizeBytes,
    long LastWriteUtcTicks,
    double DurationSeconds,
    double BucketSeconds,
    int Height);

/// <summary>One cached JPEG frame plus the metadata needed to validate it.</summary>
public sealed record ThumbnailDiskEntry(
    byte[] Data,
    int Width,
    int Height,
    long Bucket,
    DateTime LastAccessUtc);

/// <summary>
/// IINA-style on-disk thumbnail cache: one folder per source, one JPEG per
/// time bucket, and a JSON index that is rewritten atomically. Entries are
/// invalidated when the source file (size/mtime), duration, bucket size or
/// requested height change, and the whole tree is evicted least-recently-used
/// once the byte budget is exceeded. A format bump starts a fresh directory so
/// stale layouts never have to be migrated.
/// </summary>
public sealed class ThumbnailDiskCache
{
    /// <summary>Bump together with any layout/metadata change.</summary>
    public const int FormatVersion = 2;

    private const string IndexName = "index.json";
    private const string VersionName = "version.json";
    private const int KeyHashBytes = 16;

    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _versionRoot;
    private readonly Func<DateTime> _clock;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _bytes;
    private long _limitBytes = long.MaxValue;
    private bool _loaded;
    private bool _dirty;

    public ThumbnailDiskCache(string rootDirectory, Func<DateTime>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Cache root is required.", nameof(rootDirectory));
        }

        _root = System.IO.Path.GetFullPath(rootDirectory);
        _versionRoot = System.IO.Path.Combine(_root, "v" + FormatVersion);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public string Root => _root;

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _bytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _entries.Count;
            }
        }
    }

    public long LimitBytes
    {
        get
        {
            lock (_gate)
            {
                return _limitBytes;
            }
        }
    }

    /// <summary>Stable hash over every key component (32 lowercase hex chars).</summary>
    public static string ComputeSourceKey(ThumbnailSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string path = NormalizePath(source.Path);
        string payload = string.Join(
            '\n',
            "wiina-thumbs",
            FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            path,
            source.FileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            source.LastWriteUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Math.Round(source.DurationSeconds, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            Math.Round(source.BucketSeconds, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            Math.Clamp(source.Height, 1, 4096).ToString(System.Globalization.CultureInfo.InvariantCulture));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash, 0, KeyHashBytes).ToLowerInvariant();
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch
        {
            full = path;
        }

        return full.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
    }

    /// <summary>Limits the cache and immediately evicts least-recently-used frames.</summary>
    public void SetLimitBytes(long maxBytes)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _limitBytes = maxBytes < 0 ? 0 : maxBytes;
            TrimLocked();
        }
    }

    public bool TryRead(ThumbnailSource source, long bucket, out ThumbnailDiskEntry? entry)
    {
        entry = null;
        lock (_gate)
        {
            EnsureLoaded();
            string sourceKey = ComputeSourceKey(source);
            string key = FrameKey(sourceKey, bucket);
            if (!_entries.TryGetValue(key, out Entry? found))
            {
                return false;
            }

            if (!Matches(found, source) || !File.Exists(AbsolutePath(found)))
            {
                // The source changed (or the file vanished): drop every frame
                // for it instead of serving a stale image for another bucket.
                InvalidateSourceLocked(sourceKey);
                return false;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(AbsolutePath(found));
            }
            catch
            {
                RemoveLocked(key);
                return false;
            }

            if (data.Length != found.Bytes)
            {
                RemoveLocked(key);
                return false;
            }

            found.LastAccessUtcTicks = _clock().ToUniversalTime().Ticks;
            _dirty = true;
            entry = new ThumbnailDiskEntry(
                data,
                found.Width,
                found.FrameHeight,
                found.Bucket,
                new DateTime(found.LastAccessUtcTicks, DateTimeKind.Utc));
            return true;
        }
    }

    public bool Put(
        ThumbnailSource source,
        long bucket,
        byte[] jpeg,
        int width,
        int frameHeight)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        if (jpeg.Length == 0 || width <= 0 || frameHeight <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            EnsureLoaded();
            string sourceKey = ComputeSourceKey(source);
            string key = FrameKey(sourceKey, bucket);
            string relative = RelativePath(sourceKey, bucket);
            string absolute = System.IO.Path.Combine(_versionRoot, relative);
            string directory = System.IO.Path.GetDirectoryName(absolute)!;

            try
            {
                Directory.CreateDirectory(directory);
                string temp = absolute + ".tmp";
                File.WriteAllBytes(temp, jpeg);
                File.Move(temp, absolute, overwrite: true);
            }
            catch
            {
                return false;
            }

            if (_entries.TryGetValue(key, out Entry? existing))
            {
                _bytes -= existing.Bytes;
                _entries.Remove(key);
            }

            var entry = new Entry
            {
                Key = key,
                RelativePath = relative,
                SourceKey = sourceKey,
                SourcePath = NormalizePath(source.Path),
                SourceSize = source.FileSizeBytes,
                SourceTicks = source.LastWriteUtcTicks,
                Duration = Math.Round(source.DurationSeconds, 3),
                BucketSeconds = Math.Round(source.BucketSeconds, 3),
                Height = source.Height,
                Bucket = bucket,
                Width = width,
                FrameHeight = frameHeight,
                Bytes = jpeg.Length,
                LastAccessUtcTicks = _clock().ToUniversalTime().Ticks,
            };
            _entries[key] = entry;
            _bytes += entry.Bytes;
            _dirty = true;
            TrimLocked();
            FlushLocked();
            return true;
        }
    }

    /// <summary>Evicts least-recently-used frames until the budget is met.</summary>
    public long Trim(long maxBytes)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _limitBytes = maxBytes < 0 ? 0 : maxBytes;
            long before = _bytes;
            TrimLocked();
            FlushLocked();
            return before - _bytes;
        }
    }

    /// <summary>Removes every cached frame and returns the number of bytes freed.</summary>
    public long Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            long freed = _bytes;
            foreach (Entry entry in _entries.Values)
            {
                TryDeleteFile(AbsolutePath(entry));
            }

            _entries.Clear();
            _bytes = 0;
            _dirty = true;
            FlushLocked();
            TryDeleteDirectory(_versionRoot);
            Directory.CreateDirectory(_versionRoot);
            return freed;
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_loaded)
            {
                return;
            }

            FlushLocked();
        }
    }

    /// <summary>Deletes a whole cache tree (used by "clear cache") and returns bytes freed.</summary>
    public static long DeleteDirectory(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return 0;
        }

        long freed = MeasureDirectory(rootDirectory);
        foreach (string directory in Directory.EnumerateDirectories(rootDirectory))
        {
            TryDeleteDirectory(directory);
        }

        foreach (string file in Directory.EnumerateFiles(rootDirectory))
        {
            TryDeleteFile(file);
        }

        return freed;
    }

    public static long MeasureDirectory(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         rootDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // A file disappearing mid-scan simply contributes nothing.
                }
            }
        }
        catch
        {
            // Measurement is best-effort; never break the settings dialog.
        }

        return total;
    }

    // ------------------------------------------------------------- internals

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            Directory.CreateDirectory(_root);
            // A format bump (or a hand-edited version file) starts clean.
            bool current = false;
            string versionFile = System.IO.Path.Combine(_versionRoot, VersionName);
            if (File.Exists(versionFile))
            {
                try
                {
                    IndexDto? version = JsonSerializer.Deserialize<IndexDto>(
                        File.ReadAllText(versionFile), _json);
                    current = version is { Version: FormatVersion };
                }
                catch
                {
                    current = false;
                }
            }

            if (!current)
            {
                DeleteStaleVersionRoots();
                Directory.CreateDirectory(_versionRoot);
                WriteVersion();
                return;
            }

            Directory.CreateDirectory(_versionRoot);
            string indexFile = System.IO.Path.Combine(_versionRoot, IndexName);
            if (!File.Exists(indexFile))
            {
                return;
            }

            IndexDto? index;
            try
            {
                index = JsonSerializer.Deserialize<IndexDto>(
                    File.ReadAllText(indexFile), _json);
            }
            catch
            {
                index = null;
            }

            if (index is null || index.Version != FormatVersion)
            {
                TryDeleteDirectory(_versionRoot);
                Directory.CreateDirectory(_versionRoot);
                WriteVersion();
                return;
            }

            foreach (Entry entry in index.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) ||
                    string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    continue;
                }

                string absolute = AbsolutePath(entry);
                long length;
                try
                {
                    length = new FileInfo(absolute).Length;
                }
                catch
                {
                    continue;
                }

                if (length != entry.Bytes)
                {
                    TryDeleteFile(absolute);
                    continue;
                }

                _entries[entry.Key] = entry;
                _bytes += entry.Bytes;
            }
        }
        catch
        {
            // A cache that cannot be opened simply behaves as an empty cache.
            _entries.Clear();
            _bytes = 0;
        }
    }

    private void DeleteStaleVersionRoots()
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(_root))
            {
                string name = System.IO.Path.GetFileName(directory);
                if (name.Length > 1 && name[0] == 'v' &&
                    !string.Equals(directory, _versionRoot, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch
        {
            // Best-effort cleanup of old formats.
        }
    }

    private void WriteVersion()
    {
        try
        {
            string file = System.IO.Path.Combine(_versionRoot, VersionName);
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new IndexDto { Version = FormatVersion }, _json));
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            // The version file is an optimisation: a missing one leaks cache
            // space but never serves wrong data.
        }
    }

    private void FlushLocked()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        try
        {
            Directory.CreateDirectory(_versionRoot);
            var index = new IndexDto { Version = FormatVersion };
            index.Entries.AddRange(_entries.Values);
            string file = System.IO.Path.Combine(_versionRoot, IndexName);
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(index, _json));
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            _dirty = true;
        }
    }

    private void TrimLocked()
    {
        while (_bytes > _limitBytes && _entries.Count > 0)
        {
            Entry? oldest = null;
            foreach (Entry candidate in _entries.Values)
            {
                if (oldest is null || candidate.LastAccessUtcTicks < oldest.LastAccessUtcTicks)
                {
                    oldest = candidate;
                }
            }

            if (oldest is null)
            {
                break;
            }

            RemoveLocked(oldest.Key);
        }

        // Empty per-source folders would otherwise accumulate forever.
        foreach (string directory in SafeDirectories(_versionRoot))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // Keep going: a locked folder is retried next trim.
            }
        }
    }

    private void InvalidateSourceLocked(string sourceKey)
    {
        var keys = new List<string>();
        foreach (Entry entry in _entries.Values)
        {
            if (string.Equals(entry.SourceKey, sourceKey, StringComparison.Ordinal))
            {
                keys.Add(entry.Key);
            }
        }

        foreach (string key in keys)
        {
            RemoveLocked(key);
        }

        string directory = System.IO.Path.Combine(_versionRoot, sourceKey);
        TryDeleteDirectory(directory);
        _dirty = true;
    }

    private void RemoveLocked(string key)
    {
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            return;
        }

        TryDeleteFile(AbsolutePath(entry));
        _entries.Remove(key);
        _bytes -= entry.Bytes;
        _dirty = true;
    }

    private static bool Matches(Entry entry, ThumbnailSource source)
    {
        return entry.SourceSize == source.FileSizeBytes &&
               entry.SourceTicks == source.LastWriteUtcTicks &&
               Math.Abs(entry.Duration - Math.Round(source.DurationSeconds, 3)) < 0.0005 &&
               Math.Abs(entry.BucketSeconds - Math.Round(source.BucketSeconds, 3)) < 0.0005 &&
               entry.Height == Math.Clamp(source.Height, 1, 4096) &&
               string.Equals(entry.SourcePath, NormalizePath(source.Path), StringComparison.Ordinal);
    }

    private string AbsolutePath(Entry entry) =>
        System.IO.Path.Combine(_versionRoot, entry.RelativePath);

    private static string FrameKey(string sourceKey, long bucket) =>
        sourceKey + "/" + bucket.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string RelativePath(string sourceKey, long bucket) =>
        System.IO.Path.Combine(
            sourceKey,
            bucket.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".jpg");

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryDeleteFile(string path)
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
            // A frame that cannot be deleted is retried by the next trim.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Same as TryDeleteFile: best effort.
        }
    }

    private sealed class Entry
    {
        public string Key { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string SourceKey { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public long SourceSize { get; set; }
        public long SourceTicks { get; set; }
        public double Duration { get; set; }
        public double BucketSeconds { get; set; }
        public int Height { get; set; }
        public long Bucket { get; set; }
        public int Width { get; set; }
        public int FrameHeight { get; set; }
        public long Bytes { get; set; }
        public long LastAccessUtcTicks { get; set; }
    }

    private sealed class IndexDto
    {
        public int Version { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }
}
