using Wiina.Core.Playback;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class FrameDropDetectorTests
{
    private static FrameDropCounters Counters(
        long dropped = 0,
        long mistimed = 0,
        long delayed = 0,
        long decoder = 0) => new(dropped, mistimed, delayed, decoder);

    [Fact]
    public void ReportsDropsThatOnlyAppearInMistimedCounter()
    {
        // vo=gpu-next + d3d11 composition often leaves frame-drop-count at zero
        // and reports jank through mistimed-frame-count instead.
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(mistimed: 10), playing: true);
        Assert.False(detector.TryTakeNotice(0, out _));

        detector.Sample(1, Counters(mistimed: 16), playing: true);
        detector.Sample(2, Counters(mistimed: 22), playing: true);

        Assert.Equal(12, detector.WindowDrops);
        Assert.True(detector.PerMinute > 100);
        Assert.True(detector.TryTakeNotice(2, out FrameDropNotice notice));
        Assert.Equal(12, notice.Drops);
        Assert.Equal(12, notice.TotalDrops);
        Assert.InRange(notice.PerMinute, 300, 400);    }

    [Fact]
    public void CounterResetAfterSeekDoesNotReportNegativeDrops()
    {
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(dropped: 40, mistimed: 5), playing: true);
        detector.Sample(1, Counters(dropped: 60, mistimed: 5), playing: true);
        Assert.True(detector.TotalDrops > 0);

        // Seek: every counter restarts at zero.
        detector.Sample(2, Counters(), playing: true);
        Assert.Equal(0, detector.WindowDrops);
        Assert.Equal(FrameDropDiscontinuity.CounterReset, detector.LastDiscontinuity);
        Assert.False(detector.TryTakeNotice(2, out _));

        // Playback continues normally after the seek: only the new drops count.
        detector.Sample(3, Counters(dropped: 4), playing: true);
        detector.Sample(4, Counters(dropped: 10), playing: true);
        Assert.Equal(10, detector.WindowDrops);
        Assert.False(detector.LastCounters.IsZero);
    }

    [Fact]
    public void PausedSamplesNeverProduceANotice()
    {
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(dropped: 5), playing: false);
        detector.Sample(1, Counters(dropped: 500, mistimed: 300), playing: false);
        detector.Sample(2, Counters(dropped: 900), playing: false);

        Assert.Equal(0, detector.WindowDrops);
        Assert.Equal(0, detector.PerMinute);
        Assert.Equal(0, detector.TotalDrops);
        Assert.False(detector.TryTakeNotice(2, out _));
        Assert.Equal(FrameDropDiscontinuity.NotPlaying, detector.LastDiscontinuity);
    }

    [Fact]
    public void NoticeRespectsCooldown()
    {
        var detector = new FrameDropDetector(cooldownSeconds: 10);

        detector.Sample(0, Counters(dropped: 0), playing: true);
        detector.Sample(1, Counters(dropped: 6), playing: true);
        detector.Sample(2, Counters(dropped: 12), playing: true);

        Assert.True(detector.TryTakeNotice(2, out _));
        Assert.False(detector.TryTakeNotice(2.5, out _));
        Assert.False(detector.TryTakeNotice(11.9, out _));

        // More drops while the cooldown runs, then a notice after it expires.
        detector.Sample(3, Counters(dropped: 18), playing: true);
        detector.Sample(4, Counters(dropped: 24), playing: true);
        Assert.False(detector.TryTakeNotice(11.5, out _));
        Assert.True(detector.TryTakeNotice(12.1, out FrameDropNotice second));
        Assert.Equal(24, second.TotalDrops);
    }

    [Fact]
    public void DropExitsTheWindowAndTheRateFallsBackToZero()
    {
        var detector = new FrameDropDetector(windowSeconds: 3, cooldownSeconds: 0);

        detector.Sample(0, Counters(dropped: 0), playing: true);
        detector.Sample(1, Counters(dropped: 30), playing: true);
        Assert.True(detector.PerMinute > 0);

        // Ten seconds without a single further drop: the burst must age out.
        for (int i = 2; i <= 12; i++)
        {
            detector.Sample(i, Counters(dropped: 30), playing: true);
        }

        Assert.Equal(0, detector.WindowDrops);
        Assert.Equal(0, detector.PerMinute);
        Assert.False(detector.TryTakeNotice(12, out _));
        Assert.Equal(30, detector.TotalDrops);
    }

    [Fact]
    public void LowRateStaysQuiet()
    {
        var detector = new FrameDropDetector();

        // One isolated drop is above the per-minute threshold when compressed
        // into a three second window, but a single drop is not worth an OSD.
        for (int i = 0; i <= 20; i++)
        {
            detector.Sample(i, Counters(dropped: i / 20), playing: true);
        }

        Assert.True(detector.PerMinute > FrameDropDetector.DefaultMinimumPerMinute);
        Assert.True(detector.WindowDrops < FrameDropDetector.DefaultMinimumDrops);
        Assert.False(detector.TryTakeNotice(20, out _));
    }

    [Fact]
    public void DecoderDropsAndDelayedFramesAreCountedSeparately()
    {
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(), playing: true);
        detector.Sample(1, Counters(decoder: 4, delayed: 9), playing: true);
        detector.Sample(2, Counters(decoder: 8, delayed: 18), playing: true);

        // Delayed frames were recovered by the VO, so only the decoder drops count.
        Assert.Equal(8, detector.WindowDrops);
        Assert.Equal(8, detector.TotalDrops);
        Assert.Equal(18, detector.LastCounters.Delayed);
    }

    [Fact]
    public void ResetClearsTheWindowAndTotal()
    {
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(dropped: 0), playing: true);
        detector.Sample(1, Counters(dropped: 50), playing: true);
        Assert.True(detector.TotalDrops >= 50);

        detector.Reset();

        Assert.Equal(0, detector.TotalDrops);
        Assert.Equal(0, detector.WindowDrops);
        Assert.Equal(0, detector.PerMinute);
        Assert.False(detector.HasWindow);

        // The first sample after a reset only rebases; it cannot fire.
        detector.Sample(5, Counters(dropped: 900), playing: true);
        Assert.False(detector.TryTakeNotice(5, out _));
    }

    [Fact]
    public void SeekRightAfterDropsDoesNotLeakTheOldBurstIntoTheWindow()
    {
        var detector = new FrameDropDetector();

        detector.Sample(0, Counters(dropped: 0), playing: true);
        detector.Sample(1, Counters(dropped: 25), playing: true);
        detector.Sample(2, Counters(dropped: 50), playing: true);

        // Backwards jump (seek) resets the counter mid-window.
        detector.Sample(3, Counters(dropped: 3), playing: true);
        detector.Sample(4, Counters(dropped: 6), playing: true);

        Assert.True(detector.WindowDrops <= 6);

        detector.Sample(5, Counters(dropped: 9), playing: true);
        Assert.True(detector.TryTakeNotice(5, out FrameDropNotice notice));
        Assert.Equal(6, notice.Drops);
    }
}
