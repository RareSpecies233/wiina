using Wiina.Core.Caching;
using Xunit;

namespace Wiina.Core.Tests;

public class ThumbnailCacheTests
{
    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(4.9, 5, 0)]
    [InlineData(5, 5, 1)]
    [InlineData(12.3, 10, 1)]
    [InlineData(-3, 5, 0)]
    [InlineData(double.NaN, 5, 0)]
    public void BucketForFloorsToFixedWindows(double seconds, double bucket, long expected)
    {
        Assert.Equal(expected, ThumbnailCache<string>.BucketFor(seconds, bucket));
    }

    [Fact]
    public void InvalidBucketFallsBackToFiveSeconds()
    {
        Assert.Equal(1, ThumbnailCache<string>.BucketFor(7, 0));
    }

    [Fact]
    public void CacheHitsAndMissesByBucket()
    {
        var cache = new ThumbnailCache<string>(capacity: 3, bucketSeconds: 5);
        cache.Put(1, "a");

        Assert.True(cache.TryGet(4.9, out string value));
        Assert.Equal("a", value);
        Assert.False(cache.TryGet(5, out _));
    }

    [Fact]
    public void EvictsLeastRecentlyUsed()
    {
        var cache = new ThumbnailCache<string>(capacity: 2, bucketSeconds: 5);
        cache.Put(0, "a");
        cache.Put(5, "b");
        Assert.True(cache.TryGet(0, out _)); // touch a
        cache.Put(10, "c"); // evicts b

        Assert.True(cache.TryGet(0, out _));
        Assert.False(cache.TryGet(5, out _));
        Assert.True(cache.TryGet(10, out _));
        Assert.Equal(new long[] { 2, 0 }, cache.KeysInLruOrder());
    }

    [Fact]
    public void ReplacingAnEntryEvictsTheOldValue()
    {
        var evicted = new List<string>();
        var cache = new ThumbnailCache<string>(
            capacity: 2,
            bucketSeconds: 5,
            onEvict: evicted.Add);
        cache.Put(0, "old");
        cache.Put(0, "new");

        Assert.Contains("old", evicted);
        Assert.True(cache.TryGet(0, out string value));
        Assert.Equal("new", value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ClearEvictsEverything()
    {
        var evicted = new List<string>();
        var cache = new ThumbnailCache<string>(
            capacity: 3,
            bucketSeconds: 5,
            onEvict: evicted.Add);
        cache.Put(0, "a");
        cache.Put(5, "b");

        IReadOnlyList<string> removed = cache.Clear();

        Assert.Equal(2, removed.Count);
        Assert.Equal(2, evicted.Count);
        Assert.Equal(0, cache.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CapacityMustBePositive(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThumbnailCache<string>(capacity));
    }

    [Fact]
    public void BucketSizeMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThumbnailCache<string>(1, 0));
    }
}
