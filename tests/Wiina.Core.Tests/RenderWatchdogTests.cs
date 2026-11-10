using Wiina.Core.Playback;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class RenderWatchdogTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Baseline plus the two stalled samples the policy requires.</summary>
    private static RenderWatchdogDecision DriveStall(
        RenderWatchdog watchdog,
        DateTimeOffset start,
        long frame = 100,
        double startPos = 0)
    {
        watchdog.Observe(new RenderSample(start, startPos, frame));
        watchdog.Observe(new RenderSample(start.AddSeconds(4), startPos + 4, frame));
        return watchdog.Observe(new RenderSample(start.AddSeconds(6), startPos + 6, frame));
    }

    [Fact]
    public void AdvancingFramesAreNeverAStall()
    {
        var watchdog = new RenderWatchdog();

        for (int i = 0; i < 20; i++)
        {
            RenderWatchdogDecision decision = watchdog.Observe(
                new RenderSample(Start.AddSeconds(i * 2), i * 2, 1000 + i));
            Assert.Equal(RenderWatchdogAction.None, decision.Action);
        }

        Assert.Equal(0, watchdog.RecoveriesInWindow);
    }

    [Fact]
    public void RequiresTwoConsecutiveStalledSamples()
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 0, 100));

        // 2 s advance is below the 3 s threshold: not evidence yet.
        RenderWatchdogDecision first = watchdog.Observe(new RenderSample(Start.AddSeconds(2), 2, 100));
        Assert.Equal(RenderWatchdogAction.None, first.Action);
        Assert.Equal(0, first.StalledSamples);

        // 4 s without a new frame: one stalled sample, still no action.
        RenderWatchdogDecision second = watchdog.Observe(new RenderSample(Start.AddSeconds(4), 4, 100));
        Assert.Equal(RenderWatchdogAction.None, second.Action);
        Assert.Equal(1, second.StalledSamples);

        // 6 s without a new frame: the second consecutive sample trips it.
        RenderWatchdogDecision third = watchdog.Observe(new RenderSample(Start.AddSeconds(6), 6, 100));
        Assert.Equal(RenderWatchdogAction.Stalled, third.Action);
        Assert.Equal(2, third.StalledSamples);
        Assert.Equal(0, third.BaselineTimePos);
        Assert.Equal(6, third.AdvanceSeconds);
        Assert.Equal(1, third.RecoveriesInWindow);
    }

    [Fact]
    public void FrameProgressBreaksTheConsecutiveStreak()
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 0, 100));
        Assert.Equal(
            RenderWatchdogAction.None,
            watchdog.Observe(new RenderSample(Start.AddSeconds(4), 4, 100)).Action);

        // A presented frame is proof the output is alive: the streak resets.
        Assert.Equal(
            RenderWatchdogAction.None,
            watchdog.Observe(new RenderSample(Start.AddSeconds(5), 5, 101)).Action);

        RenderWatchdogDecision next = watchdog.Observe(
            new RenderSample(Start.AddSeconds(9), 9, 101));
        Assert.Equal(RenderWatchdogAction.None, next.Action);
        Assert.Equal(1, next.StalledSamples);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void IneligibleSamplesResetTheBaseline(
        bool paused,
        bool seeking,
        bool buffering,
        bool switching)
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 0, 100));

        // This sample would otherwise satisfy the 3 s advance.
        RenderWatchdogDecision ignored = watchdog.Observe(
            new RenderSample(
                Start.AddSeconds(4),
                4,
                100,
                paused,
                seeking,
                buffering,
                switching));
        Assert.Equal(RenderWatchdogAction.None, ignored.Action);
        Assert.False(watchdog.HasBaseline);

        // The next healthy sample becomes the new baseline, so the old 4 s
        // window cannot leak across the pause/seek/switch.
        RenderWatchdogDecision resumed = watchdog.Observe(
            new RenderSample(Start.AddSeconds(5), 5, 100));
        Assert.Equal(RenderWatchdogAction.None, resumed.Action);
        Assert.True(watchdog.HasBaseline);
        Assert.Equal(0, watchdog.StalledSamples);
    }

    [Fact]
    public void RecoveryInProgressSamplesAreIgnored()
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 0, 100));
        watchdog.Observe(new RenderSample(Start.AddSeconds(4), 4, 100));

        RenderWatchdogDecision decision = watchdog.Observe(
            new RenderSample(Start.AddSeconds(6), 6, 100, RecoveryInProgress: true));
        Assert.Equal(RenderWatchdogAction.None, decision.Action);
        Assert.False(watchdog.HasBaseline);
    }

    [Fact]
    public void BackwardsPositionRebaselines()
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 100, 500));

        // A loop/seek to the start must not read as "advanced 3 s".
        RenderWatchdogDecision decision = watchdog.Observe(
            new RenderSample(Start.AddSeconds(4), 2, 500));
        Assert.Equal(RenderWatchdogAction.None, decision.Action);
        Assert.Equal(0, decision.StalledSamples);
        Assert.True(watchdog.HasBaseline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnknownFrameNumberIsIgnored(long frame)
    {
        var watchdog = new RenderWatchdog();
        watchdog.Observe(new RenderSample(Start, 0, 100));

        RenderWatchdogDecision decision = watchdog.Observe(
            new RenderSample(Start.AddSeconds(5), 5, frame));
        Assert.Equal(RenderWatchdogAction.None, decision.Action);
        Assert.False(watchdog.HasBaseline);
    }

    [Fact]
    public void NegativeTimePositionIsIgnored()
    {
        var watchdog = new RenderWatchdog();
        RenderWatchdogDecision decision = watchdog.Observe(
            new RenderSample(Start, -1, 100));
        Assert.Equal(RenderWatchdogAction.None, decision.Action);
        Assert.False(watchdog.HasBaseline);
    }

    [Fact]
    public void AllowsTwoRecoveriesThenExhaustsTheBudget()
    {
        var watchdog = new RenderWatchdog();

        Assert.Equal(RenderWatchdogAction.Stalled, DriveStall(watchdog, Start).Action);
        Assert.Equal(
            RenderWatchdogAction.Stalled,
            DriveStall(watchdog, Start.AddMinutes(1), frame: 200).Action);
        Assert.Equal(2, watchdog.RecoveriesInWindow);
        Assert.True(watchdog.BudgetExhausted);

        // A third stall inside the window must stop the retry loop.
        RenderWatchdogDecision third = DriveStall(watchdog, Start.AddMinutes(2), frame: 300);
        Assert.Equal(RenderWatchdogAction.BudgetExhausted, third.Action);
        Assert.Equal(2, third.RecoveriesInWindow);
        Assert.Equal(2, watchdog.RecoveriesInWindow);
    }

    [Fact]
    public void BudgetExpiresAfterTheRollingWindow()
    {
        var watchdog = new RenderWatchdog(window: TimeSpan.FromMinutes(10));

        Assert.Equal(RenderWatchdogAction.Stalled, DriveStall(watchdog, Start).Action);
        Assert.Equal(
            RenderWatchdogAction.Stalled,
            DriveStall(watchdog, Start.AddMinutes(2), frame: 200).Action);
        Assert.True(watchdog.BudgetExhausted);

        // Both attempts are older than the 10 min window (cutoff is 13-10 = 3
        // min, so the 0 min and 2 min recoveries have both expired), therefore
        // recovery is allowed again.
        RenderWatchdogDecision later = DriveStall(watchdog, Start.AddMinutes(13), frame: 300);
        Assert.Equal(RenderWatchdogAction.Stalled, later.Action);
        Assert.Equal(1, later.RecoveriesInWindow);
    }

    [Fact]
    public void ResetClearsTheBudgetAndSamples()
    {
        var watchdog = new RenderWatchdog();
        DriveStall(watchdog, Start);
        DriveStall(watchdog, Start.AddMinutes(1), frame: 200);
        Assert.True(watchdog.BudgetExhausted);

        watchdog.Reset();

        Assert.False(watchdog.BudgetExhausted);
        Assert.Equal(0, watchdog.RecoveriesInWindow);
        Assert.False(watchdog.HasBaseline);
        Assert.Equal(
            RenderWatchdogAction.Stalled,
            DriveStall(watchdog, Start.AddMinutes(2), frame: 300).Action);
    }

    [Fact]
    public void ResetSamplesKeepsTheRecoveryBudget()
    {
        var watchdog = new RenderWatchdog();
        DriveStall(watchdog, Start);
        Assert.Equal(1, watchdog.RecoveriesInWindow);

        watchdog.ResetSamples();

        Assert.False(watchdog.HasBaseline);
        Assert.Equal(0, watchdog.StalledSamples);
        Assert.Equal(1, watchdog.RecoveriesInWindow);
    }
}
