using Wiina.Core.Caching;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ThumbnailDiskCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "wiina-thumb-tests-" + Guid.NewGuid().ToString("N"));

    private static ThumbnailSource Source(
        string path = @"C:\media\a.mkv",
        long size = 1000,
        long ticks = 638000000000000000,
        double duration = 120,
        double bucket = 5,
        int height = 90) =>
        new(path, size, ticks, duration, bucket, height);

    private static byte[] Payload(int length, byte seed = 7)
    {
        var bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(seed + (i * 31));
        }

        return bytes;
    }

    [Fact]
    public void PutThenReadRoundTrips()
    {
        var cache = new ThumbnailDiskCache(_root);
        ThumbnailSource source = Source();

        Assert.True(cache.Put(source, 3, Payload(64), 160, 90));
        Assert.True(cache.TryRead(source, 3, out ThumbnailDiskEntry? entry));

        Assert.NotNull(entry);
        Assert.Equal(64, entry!.Data.Length);
        Assert.Equal(160, entry.Width);
        Assert.Equal(90, entry.Height);
        Assert.Equal(3, entry.Bucket);
    }

    [Fact]
    public void MissingBucketMisses()
    {
        var cache = new ThumbnailDiskCache(_root);
        cache.Put(Source(), 2, Payload(32), 160, 90);

        Assert.False(cache.TryRead(Source(), 3, out ThumbnailDiskEntry? entry));
        Assert.Null(entry);
    }

    [Fact]
    public void ChangedSourceIdentityMisses()
    {
        var cache = new ThumbnailDiskCache(_root);
        ThumbnailSource source = Source();
        cache.Put(source, 2, Payload(32), 160, 90);

        Assert.False(cache.TryRead(Source(size: 2001), 2, out _));
        Assert.False(cache.TryRead(Source(duration: 121), 2, out _));
        Assert.False(cache.TryRead(Source(height: 120), 2, out _));
        Assert.False(cache.TryRead(Source(path: @"C:\media\b.mkv"), 2, out _));

        // The original identity is untouched by the failed lookups.
        Assert.True(cache.TryRead(source, 2, out _));
    }

    [Fact]
    public void ZeroByteFrameIsRejected()
    {
        var cache = new ThumbnailDiskCache(_root);

        Assert.False(cache.Put(Source(), 0, Array.Empty<byte>(), 160, 90));
        Assert.False(cache.Put(Source(), 0, Payload(8), 0, 90));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void EvictsLeastRecentlyUsedWhenOverBudget()
    {
        var cache = new ThumbnailDiskCache(_root);
        ThumbnailSource source = Source();
        cache.SetLimitBytes(250);

        cache.Put(source, 0, Payload(100), 160, 90);
        cache.Put(source, 1, Payload(100), 160, 90);
        cache.Put(source, 2, Payload(100), 160, 90);

        Assert.True(cache.TotalBytes <= 250);
        Assert.False(cache.TryRead(source, 0, out _));
        Assert.True(cache.TryRead(source, 2, out _));
        Assert.True(cache.TryRead(source, 1, out _));
    }

    [Fact]
    public void RecencyOrderDecidesEviction()
    {
        var cache = new ThumbnailDiskCache(_root);
        ThumbnailSource source = Source();
        cache.SetLimitBytes(250);
        cache.Put(source, 0, Payload(100), 160, 90);
        cache.Put(source, 1, Payload(100), 160, 90);

        // Touch bucket 0 so bucket 1 becomes the least recently used.
        Assert.True(cache.TryRead(source, 0, out _));
        cache.Put(source, 2, Payload(100), 160, 90);

        Assert.True(cache.TryRead(source, 0, out _));
        Assert.False(cache.TryRead(source, 1, out _));
    }

    [Fact]
    public void ZeroLimitEvictsEverything()
    {
        var cache = new ThumbnailDiskCache(_root);
        cache.Put(Source(), 1, Payload(100), 160, 90);
        Assert.True(cache.TotalBytes > 0);

        cache.SetLimitBytes(0);

        Assert.Equal(0, cache.TotalBytes);
        Assert.False(cache.TryRead(Source(), 1, out _));
    }

    [Fact]
    public void ClearReportsFreedBytesAndEmptiesTheCache()
    {
        var cache = new ThumbnailDiskCache(_root);
        cache.Put(Source(), 1, Payload(50), 160, 90);
        cache.Put(Source(), 2, Payload(50), 160, 90);

        long freed = cache.Clear();

        Assert.True(freed > 0);
        Assert.Equal(0, cache.TotalBytes);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryRead(Source(), 1, out _));
    }

    [Fact]
    public void IndexSurvivesANewInstance()
    {
        var first = new ThumbnailDiskCache(_root);
        first.Put(Source(), 4, Payload(80), 160, 90);
        first.Flush();

        var second = new ThumbnailDiskCache(_root);

        Assert.True(second.TryRead(Source(), 4, out ThumbnailDiskEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal(80, entry!.Data.Length);
    }

    [Fact]
    public void NoTempFilesLeftBehind()
    {
        var cache = new ThumbnailDiskCache(_root);
        cache.Put(Source(), 1, Payload(40), 160, 90);

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void SourceKeyIgnoresPathCasing()
    {
        Assert.Equal(
            ThumbnailDiskCache.ComputeSourceKey(Source(@"C:\Media\A.MKV")),
            ThumbnailDiskCache.ComputeSourceKey(Source(@"c:\media\a.mkv")));
    }

    [Fact]
    public void SourceKeyChangesWithEveryComponent()
    {
        string baseline = ThumbnailDiskCache.ComputeSourceKey(Source());

        Assert.NotEqual(baseline, ThumbnailDiskCache.ComputeSourceKey(Source(size: 1001)));
        Assert.NotEqual(baseline, ThumbnailDiskCache.ComputeSourceKey(Source(ticks: 1)));
        Assert.NotEqual(baseline, ThumbnailDiskCache.ComputeSourceKey(Source(duration: 121)));
        Assert.NotEqual(baseline, ThumbnailDiskCache.ComputeSourceKey(Source(bucket: 10)));
        Assert.NotEqual(baseline, ThumbnailDiskCache.ComputeSourceKey(Source(height: 120)));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // The temp tree is harmless if a handle lingers.
        }
    }
}
