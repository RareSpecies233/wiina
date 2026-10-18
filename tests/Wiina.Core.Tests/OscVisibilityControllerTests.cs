using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class OscVisibilityControllerTests
{
    [Fact]
    public void InitiallyShown()
    {
        var controller = new OscVisibilityController();
        Assert.Equal(OscVisibilityState.Shown, controller.State);
    }

    [Fact]
    public void ShowAndHideGoThroughWillStates()
    {
        var controller = new OscVisibilityController();
        Assert.Equal(OscVisibilityState.WillHide, controller.Hide());
        Assert.Equal(OscVisibilityState.Hidden, controller.CompleteHide());
        Assert.Equal(OscVisibilityState.WillShow, controller.Show());
        Assert.Equal(OscVisibilityState.Shown, controller.CompleteShow());
    }

    [Fact]
    public void ReverseEventCancelsAnimation()
    {
        var controller = new OscVisibilityController();
        controller.Hide();
        Assert.Equal(OscVisibilityState.WillShow, controller.Show());
        Assert.Equal(OscVisibilityState.Shown, controller.CompleteShow());
        Assert.Equal(OscVisibilityState.Shown, controller.State);
    }

    [Fact]
    public void DraggingBlocksTimerHide()
    {
        var controller = new OscVisibilityController { IsDragging = true };
        Assert.Equal(OscVisibilityState.Shown, controller.TimerElapsed());
        Assert.False(controller.CanHide);
    }

    [Fact]
    public void AutoHideDisabledBlocksHide()
    {
        var controller = new OscVisibilityController { AutoHideEnabled = false };
        Assert.Equal(OscVisibilityState.Shown, controller.Hide());
        Assert.Equal(OscVisibilityState.Shown, controller.TimerElapsed());
    }

    [Fact]
    public void SuppressedBlocksHide()
    {
        var controller = new OscVisibilityController { IsSuppressed = true };
        Assert.Equal(OscVisibilityState.Shown, controller.Hide());
    }

    [Fact]
    public void PointerExitForcesHide()
    {
        var controller = new OscVisibilityController
        {
            AutoHideEnabled = false,
            IsDragging = true,
        };
        Assert.Equal(OscVisibilityState.WillHide, controller.PointerExited());
    }

    [Fact]
    public void TimerElapsedHidesWhenAllowed()
    {
        var controller = new OscVisibilityController();
        Assert.Equal(OscVisibilityState.WillHide, controller.TimerElapsed());
    }
}
