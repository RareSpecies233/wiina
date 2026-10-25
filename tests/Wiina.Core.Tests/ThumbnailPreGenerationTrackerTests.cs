using Wiina.Core.Caching;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ThumbnailPreGenerationTrackerTests
{
    private const string First = @"C:\videos\first.mkv";
    private const string Second = @"C:\videos\second.mkv";

    [Fact]
    public void CompletesOnlyWhenEveryBucketIsDone()
    {
        var tracker = new ThumbnailPreGenerationTracker();
        tracker.Begin(First, totalBuckets: 3);

        Assert.False(tracker.IsComplete);
        Assert.True(tracker.IsPendingFor(First));
        Assert.Equal(0, tracker.CompletedBuckets);

        tracker.MarkCompleted(First);
        Assert.False(tracker.IsComplete);
        Assert.True(tracker.IsPendingFor(First));

        tracker.MarkCompleted(First);
        Assert.False(tracker.IsComplete);

        tracker.MarkCompleted(First);
        Assert.True(tracker.IsComplete);
        Assert.True(tracker.IsCompleteFor(First));
        Assert.False(tracker.IsPendingFor(First));
        Assert.Equal(3, tracker.CompletedBuckets);
        Assert.Equal(3, tracker.TotalBuckets);
    }

    [Fact]
    public void AnEmptyFileOrMissingSourceIsCompleteImmediately()
    {
        var tracker = new ThumbnailPreGenerationTracker();
        tracker.Begin(First, totalBuckets: 0);
        Assert.True(tracker.IsComplete);
        Assert.False(tracker.IsPendingFor(First));

        tracker.Begin(null, totalBuckets: 10);
        Assert.True(tracker.IsComplete);
        Assert.False(tracker.IsPendingFor(null));
    }

    [Fact]
    public void MarkCompleteForceFinishesARunThatWillNotWalkTheTimeline()
    {
        var tracker = new ThumbnailPreGenerationTracker();
        tracker.Begin(First, totalBuckets: 500);
        Assert.True(tracker.IsPendingFor(First));

        tracker.MarkComplete(First);

        Assert.True(tracker.IsComplete);
        Assert.False(tracker.IsPendingFor(First));
        Assert.Equal(500, tracker.CompletedBuckets);
    }

    [Fact]
    public void AnotherSourceCannotCompleteTheTrackedOne()
    {
        var tracker = new ThumbnailPreGenerationTracker();
        tracker.Begin(First, totalBuckets: 2);

        tracker.MarkCompleted(Second);
        tracker.MarkComplete(Second);

        Assert.False(tracker.IsComplete);
        Assert.True(tracker.IsPendingFor(First));
        Assert.False(tracker.IsCompleteFor(Second));

        // Buckets never overshoot the total (a cached pass may report twice).
        tracker.MarkCompleted(First);
        tracker.MarkCompleted(First);
        tracker.MarkCompleted(First);
        Assert.Equal(2, tracker.CompletedBuckets);
        Assert.True(tracker.IsCompleteFor(First));
    }

    [Fact]
    public void SwitchingFilesResetsProgressAndCompletion()
    {
        var tracker = new ThumbnailPreGenerationTracker();
        tracker.Begin(First, totalBuckets: 1);
        tracker.MarkCompleted(First);
        Assert.True(tracker.IsComplete);

        tracker.Begin(Second, totalBuckets: 4);
        Assert.False(tracker.IsComplete);
        Assert.False(tracker.IsCompleteFor(First));
        Assert.Equal(0, tracker.CompletedBuckets);
        Assert.Equal(Second, tracker.SourceKey);

        tracker.Reset();
        Assert.False(tracker.IsComplete);
        Assert.Null(tracker.SourceKey);
        Assert.False(tracker.IsPendingFor(Second));
    }
}
