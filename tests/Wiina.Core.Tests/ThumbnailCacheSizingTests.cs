using Wiina.Core.Caching;
using Xunit;

namespace Wiina.Core.Tests;

public class ThumbnailCacheSizingTests
{
    [Theory]
    [InlineData(1440, 5, 1, 290)] // 24 min at the default 5 s bucket
    [InlineData(7200, 5, 1, 1442)] // 2 h at the default 5 s bucket
    [InlineData(7200, 2, 1, 3602)]
    [InlineData(12, 5, 60, 60)] // the configured count is only a floor
    [InlineData(0, 5, 1, 1)]
    [InlineData(-5, 5, 1, 1)]
    [InlineData(double.NaN, 5, 1, 1)]
    [InlineData(double.PositiveInfinity, 5, 1, 1)]
    [InlineData(1440, 0, 1, 290)] // an invalid bucket falls back to 5 s
    public void FramesForDurationCoversEveryBucket(
        double durationSeconds,
        double bucketSeconds,
        int minimumFrames,
        int expected)
    {
        Assert.Equal(
            expected,
            ThumbnailCacheSizing.FramesForDuration(
                durationSeconds,
                bucketSeconds,
                minimumFrames));
    }

    [Fact]
    public void TwoHourMovieEstimateStaysUnderHundredMegabytes()
    {
        long perFrame = ThumbnailCacheSizing.FrameBytes(160, 90);
        Assert.Equal(57_600, perFrame);

        int frames = ThumbnailCacheSizing.FramesForDuration(7200, 5);
        long bytes = ThumbnailCacheSizing.EstimateBytes(frames, perFrame);

        Assert.Equal(1442, frames);
        Assert.Equal(83_059_200, bytes);
        Assert.InRange(ThumbnailCacheSizing.Megabyte(bytes), 79.0, 79.3);
    }

    [Fact]
    public void MissingSourceFallsBackToTheConfiguredFloor()
    {
        Assert.Equal(1, ThumbnailCacheSizing.FramesForDuration(0, 5));
        Assert.Equal(60, ThumbnailCacheSizing.FramesForDuration(0, 5, 60));
    }
}
