using Wiina.Core.Models;
using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ModeStateMachineTests
{
    [Fact]
    public void NormalToFullscreenSucceeds()
    {
        var machine = new ModeStateMachine();

        ModeTransitionResult result = machine.Request(PlayerMode.Fullscreen, _ => true);

        Assert.True(result.Success);
        Assert.Equal(PlayerMode.Normal, result.From);
        Assert.Equal(PlayerMode.Fullscreen, result.To);
        Assert.False(result.ExitPrevious);
        Assert.Equal(PlayerMode.Fullscreen, machine.Current);
        Assert.Equal(PlayerMode.Normal, machine.Previous);
    }

    [Fact]
    public void SameModeIsRejectedWithoutApplying()
    {
        var machine = new ModeStateMachine();
        bool applied = false;

        ModeTransitionResult result = machine.Request(PlayerMode.Normal, _ =>
        {
            applied = true;
            return true;
        });

        Assert.False(result.Success);
        Assert.Equal(ModeTransitionReason.SameMode, result.Reason);
        Assert.False(applied);
    }

    [Theory]
    [InlineData(PlayerMode.Normal, PlayerMode.Fullscreen, false)]
    [InlineData(PlayerMode.Normal, PlayerMode.PiP, false)]
    [InlineData(PlayerMode.Normal, PlayerMode.Mini, false)]
    [InlineData(PlayerMode.Fullscreen, PlayerMode.PiP, true)]
    [InlineData(PlayerMode.Fullscreen, PlayerMode.Mini, true)]
    [InlineData(PlayerMode.PiP, PlayerMode.Fullscreen, true)]
    [InlineData(PlayerMode.PiP, PlayerMode.Mini, true)]
    [InlineData(PlayerMode.Mini, PlayerMode.Fullscreen, true)]
    [InlineData(PlayerMode.Mini, PlayerMode.PiP, true)]
    public void TransitionTableAllowsEveryDifferentMode(
        PlayerMode from,
        PlayerMode to,
        bool exitPrevious)
    {
        var machine = new ModeStateMachine();
        if (from != PlayerMode.Normal)
        {
            Assert.True(machine.Request(from, _ => true).Success);
        }

        ModeTransitionResult result = machine.Request(to, _ => true);

        Assert.True(result.Success);
        Assert.Equal(exitPrevious, result.ExitPrevious);
        Assert.Equal(to, machine.Current);
    }

    [Fact]
    public void MutualExclusionKeepsOnlyTheLatestMode()
    {
        var machine = new ModeStateMachine();

        Assert.True(machine.Request(PlayerMode.Fullscreen, _ => true).Success);
        Assert.True(machine.Request(PlayerMode.PiP, _ => true).Success);

        Assert.Equal(PlayerMode.PiP, machine.Current);
        Assert.NotEqual(PlayerMode.Fullscreen, machine.Current);
    }

    [Fact]
    public void ApplyFailureRollsBackToPreviousMode()
    {
        var machine = new ModeStateMachine();
        var applied = new List<PlayerMode>();

        ModeTransitionResult result = machine.Request(PlayerMode.PiP, mode =>
        {
            applied.Add(mode);
            return mode == PlayerMode.Normal;
        });

        Assert.False(result.Success);
        Assert.Equal(ModeTransitionReason.RolledBack, result.Reason);
        Assert.Equal(PlayerMode.Normal, machine.Current);
        Assert.Equal(new[] { PlayerMode.PiP, PlayerMode.Normal }, applied);
    }

    [Fact]
    public void ApplyExceptionRollsBackToPreviousMode()
    {
        var machine = new ModeStateMachine();

        ModeTransitionResult result = machine.Request(
            PlayerMode.Mini,
            mode => mode == PlayerMode.Normal
                ? true
                : throw new InvalidOperationException("boom"));

        Assert.False(result.Success);
        Assert.Equal(ModeTransitionReason.RolledBack, result.Reason);
        Assert.Equal(PlayerMode.Normal, machine.Current);
    }

    [Fact]
    public void RollbackFromFullscreenRestoresTheNormalPlan()
    {
        var machine = new ModeStateMachine();
        Assert.True(machine.Request(PlayerMode.Fullscreen, _ => true).Success);

        ModeTransitionResult result = machine.Request(PlayerMode.PiP, _ => false);

        Assert.False(result.Success);
        Assert.Equal(ModeTransitionReason.RolledBack, result.Reason);
        Assert.Equal(PlayerMode.Fullscreen, machine.Current);
        Assert.Equal(PlayerMode.Normal, machine.Previous);
    }

    [Fact]
    public void ReentrantRequestIsRejected()
    {
        var machine = new ModeStateMachine();
        ModeTransitionResult? nested = null;

        ModeTransitionResult result = machine.Request(PlayerMode.Fullscreen, _ =>
        {
            nested = machine.Request(PlayerMode.Mini, _ => true);
            return true;
        });

        Assert.True(result.Success);
        Assert.NotNull(nested);
        Assert.False(nested!.Value.Success);
        Assert.Equal(ModeTransitionReason.Reentrant, nested.Value.Reason);
        Assert.Equal(PlayerMode.Fullscreen, machine.Current);
    }

    [Fact]
    public void ChangedEventReportsTheOutcome()
    {
        var machine = new ModeStateMachine();
        ModeTransitionResult? observed = null;
        machine.Changed += (_, result) => observed = result;

        machine.Request(PlayerMode.Fullscreen, _ => true);

        Assert.NotNull(observed);
        Assert.True(observed!.Value.Success);
        Assert.Equal(PlayerMode.Fullscreen, observed.Value.To);
    }

    [Theory]
    [InlineData(PlayerMode.Normal, false, false, true)]
    [InlineData(PlayerMode.Fullscreen, false, false, true)]
    [InlineData(PlayerMode.PiP, false, true, true)]
    [InlineData(PlayerMode.Mini, true, false, false)]
    public void PolicyDescribesEachMode(
        PlayerMode mode,
        bool topmost,
        bool hidesMainVideo,
        bool showsChrome)
    {
        ModePlan plan = ModePolicy.For(mode);

        Assert.Equal(topmost, plan.IsAlwaysOnTop);
        Assert.Equal(hidesMainVideo, plan.HidesMainVideo);
        Assert.Equal(showsChrome, plan.ShowsChrome);
        Assert.Equal(mode == PlayerMode.Mini, plan.UsesCompactWindow);
    }
}
