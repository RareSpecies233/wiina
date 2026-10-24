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

    [Fact]
    public void JitterUnderTheThresholdIsNotMovement()
    {
        var controller = new OscVisibilityController();
        controller.EnterWindow(100, 100, TimeSpan.Zero);

        Assert.False(controller.PointerMoved(104, 104, TimeSpan.FromSeconds(1)));
        Assert.False(controller.PointerMoved(96, 96, TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.Zero, controller.LastActivity);
    }

    [Fact]
    public void RealMovementResetsTheIdleClock()
    {
        var controller = new OscVisibilityController();
        controller.EnterWindow(100, 100, TimeSpan.Zero);

        Assert.True(controller.PointerMoved(110, 100, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(1), controller.LastActivity);

        Assert.False(controller.IsIdle(TimeSpan.FromSeconds(3.4)));
        Assert.True(controller.IsIdle(TimeSpan.FromSeconds(3.5)));
        Assert.True(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(3.5)));
    }

    [Fact]
    public void BoundsAreIgnoredWithoutAnAnchor()
    {
        var controller = new OscVisibilityController();
        Assert.True(controller.PointerMoved(5, 5, TimeSpan.Zero));
        Assert.False(controller.PointerMoved(5, 5, TimeSpan.FromSeconds(1)));

        controller.ResetPointerAnchor();
        Assert.True(controller.PointerMoved(5, 5, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void IdleDelayUsesTheConfiguredValue()
    {
        var controller = new OscVisibilityController { AutoHideSeconds = 0.5 };
        controller.EnterWindow(0, 0, TimeSpan.Zero);

        Assert.False(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(0.49)));
        Assert.True(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(0.5)));
    }

    [Fact]
    public void AutoHideDelayIsClampedToTheSupportedRange()
    {
        Assert.Equal(0.5, new OscVisibilityController { AutoHideSeconds = 0.1 }.AutoHideSeconds);
        Assert.Equal(30, new OscVisibilityController { AutoHideSeconds = 120 }.AutoHideSeconds);
        Assert.Equal(2.5, new OscVisibilityController().AutoHideSeconds);
    }

    [Fact]
    public void RestingPointerOverTheOscStillHides()
    {
        // Regression: a pointer parked on the OSC (fullscreen) used to count as
        // activity forever because every pointer move re-armed the timer.
        var controller = new OscVisibilityController();
        controller.EnterWindow(500, 500, TimeSpan.Zero);
        Assert.False(controller.PointerMoved(502, 503, TimeSpan.FromSeconds(1)));
        Assert.False(controller.PointerMoved(501, 498, TimeSpan.FromSeconds(2)));

        Assert.True(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(2.5)));
    }

    [Fact]
    public void ClickHidesOnlyOverVideoAndNeverForDrags()
    {
        var controller = new OscVisibilityController { AutoHideEnabled = false };
        Assert.True(controller.ShouldHideOnClick(chromeVisible: true, overChrome: false, isDrag: false));
        Assert.False(controller.ShouldHideOnClick(chromeVisible: true, overChrome: true, isDrag: false));
        Assert.False(controller.ShouldHideOnClick(chromeVisible: true, overChrome: false, isDrag: true));
        Assert.False(controller.ShouldHideOnClick(chromeVisible: false, overChrome: false, isDrag: false));

        controller.IsSuppressed = true;
        Assert.False(controller.ShouldHideOnClick(chromeVisible: true, overChrome: false, isDrag: false));
    }

    [Fact]
    public void DeactivateHidesOnlyForOverlayLayouts()
    {
        Assert.True(OscVisibilityController.ShouldHideOnDeactivate(docked: false, sidebarVisible: false));
        Assert.False(OscVisibilityController.ShouldHideOnDeactivate(docked: true, sidebarVisible: false));
        Assert.False(OscVisibilityController.ShouldHideOnDeactivate(docked: false, sidebarVisible: true));
    }

    [Fact]
    public void ShouldHideOnIdleRespectsEveryGate()
    {
        var controller = new OscVisibilityController { AutoHideEnabled = false };
        controller.EnterWindow(0, 0, TimeSpan.Zero);
        Assert.False(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(10)));

        controller.AutoHideEnabled = true;
        Assert.True(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(10)));

        controller.IsSuppressed = true;
        Assert.False(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(10)));

        controller.IsSuppressed = false;
        controller.IsDragging = true;
        Assert.False(controller.ShouldHideOnIdle(TimeSpan.FromSeconds(10)));
    }
}
