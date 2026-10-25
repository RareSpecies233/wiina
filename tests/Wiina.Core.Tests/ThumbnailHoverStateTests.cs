using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// The v1.3.8 plan (§3.2/§3.4) rules behind the timeline bubble: a pending
/// request must never empty the bubble, "not allowed" must, and a new file must
/// never show the previous file's frame.
/// </summary>
public sealed class ThumbnailHoverStateTests
{
    [Fact]
    public void StartsHiddenWithoutAFrame()
    {
        var state = new ThumbnailHoverState();

        Assert.Equal(ThumbnailHoverPhase.Hidden, state.Phase);
        Assert.False(state.HasFrame);
        Assert.False(state.IsVisible);
    }

    [Fact]
    public void FirstHoverShowsAnEmptyBackplateUntilTheFrameArrives()
    {
        var state = new ThumbnailHoverState();

        Assert.Equal(ThumbnailHoverPhase.Empty, state.BeginRequest());
        Assert.True(state.IsVisible);

        Assert.Equal(ThumbnailHoverPhase.Updated, state.Complete(ThumbnailOutcome.Frame));
        Assert.True(state.HasFrame);
    }

    [Fact]
    public void PendingKeepsThePreviousFrameAndNeverFlashesBlack()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.Frame);

        // Moving along the timeline: the old frame stays on screen while the
        // next position is decoded, which is the whole point of the outcome enum.
        Assert.Equal(ThumbnailHoverPhase.Showing, state.BeginRequest());
        Assert.Equal(ThumbnailHoverPhase.Showing, state.Complete(ThumbnailOutcome.Pending));
        Assert.True(state.HasFrame);
        Assert.True(state.IsVisible);
    }

    [Fact]
    public void PendingBeforeAnyFrameStaysEmptyWithoutSpinningForever()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();

        Assert.Equal(ThumbnailHoverPhase.Empty, state.Complete(ThumbnailOutcome.Pending));
        Assert.False(state.HasFrame);
        Assert.True(state.IsVisible);
    }

    [Fact]
    public void NotAllowedEmptiesTheBubbleEvenWhenAFrameWasOnScreen()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.Frame);

        Assert.Equal(ThumbnailHoverPhase.Unavailable, state.Complete(ThumbnailOutcome.NotAllowed));
        Assert.False(state.HasFrame);
        Assert.True(state.IsVisible);
    }

    [Fact]
    public void NotAllowedKeepsTheBubbleEmptyOnTheNextRequest()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.NotAllowed);

        // Still empty: no stale frame can be resurrected by another pointer move.
        Assert.Equal(ThumbnailHoverPhase.Empty, state.BeginRequest());
        Assert.False(state.HasFrame);
    }

    [Fact]
    public void ANewFileDropsThePreviousFrame()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.Frame);

        state.ClearFrame();

        Assert.False(state.HasFrame);
        Assert.Equal(ThumbnailHoverPhase.Empty, state.BeginRequest());
        Assert.Equal(ThumbnailHoverPhase.Empty, state.Complete(ThumbnailOutcome.Pending));
    }

    [Fact]
    public void ClearFrameWhileHiddenStaysHidden()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.Frame);
        state.Clear();

        state.ClearFrame();

        Assert.Equal(ThumbnailHoverPhase.Hidden, state.Phase);
        Assert.False(state.IsVisible);
    }

    [Fact]
    public void ClearDropsFrameAndBubble()
    {
        var state = new ThumbnailHoverState();
        state.BeginRequest();
        state.Complete(ThumbnailOutcome.Frame);

        state.Clear();

        Assert.Equal(ThumbnailHoverPhase.Hidden, state.Phase);
        Assert.False(state.HasFrame);
        Assert.False(state.IsVisible);
    }
}
