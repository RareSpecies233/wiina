using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Edge magnet contract (issue 4): threshold snap, breakaway hysteresis,
/// raw-cursor recovery for WM_MOVING, monitor offsets, DPI scaling and size
/// preservation (16:9 / minimum sizes must never be altered by snapping).
/// </summary>
public sealed class EdgeSnapTests
{
    private static readonly WorkArea Area = new(0, 0, 1920, 1040);

    private static WindowRect Rect(int x, int y, int width = 800, int height = 450) =>
        new(x, y, x + width, y + height);

    [Fact]
    public void FarFromEveryEdgeStaysPut()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(200, 150), Area);

        Assert.Equal(new WindowRect(200, 150, 1000, 600), result.Rect);
        Assert.Equal(SnapEdges.None, result.Edges);
        Assert.False(result.IsSnapped);
    }

    [Fact]
    public void NearLeftEdgeSnapsFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(6, 150), Area);

        Assert.Equal(0, result.Rect.Left);
        Assert.Equal(SnapEdges.Left, result.Edges);
        Assert.Equal(800, result.Rect.Width);
        Assert.Equal(450, result.Rect.Height);
    }

    [Fact]
    public void NearRightAndBottomEdgesSnapFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(1114, 586), Area);

        Assert.Equal(1120, result.Rect.Left);
        Assert.Equal(590, result.Rect.Top);
        Assert.Equal(SnapEdges.Right | SnapEdges.Bottom, result.Edges);
        Assert.Equal(Area.Right, result.Rect.Right);
        Assert.Equal(Area.Bottom, result.Rect.Bottom);
    }

    [Fact]
    public void CornerSnapsBothAxes()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(3, 4), Area);

        Assert.Equal(new WindowRect(0, 0, 800, 450), result.Rect);
        Assert.Equal(SnapEdges.Left | SnapEdges.Top, result.Edges);
    }

    [Fact]
    public void ThresholdIsMeasuredInDipsAndScaledByDpi()
    {
        var session = new EdgeSnapSession();

        // 10 DIP snap distance at 150% is 15 physical pixels.
        Assert.Equal(0, session.Apply(Rect(14, 150), Area, scale: 1.5).Rect.Left);
        session.Reset();
        Assert.Equal(16, session.Apply(Rect(16, 150), Area, scale: 1.5).Rect.Left);
    }

    [Fact]
    public void MagnetHoldsUntilBreakawayDistance()
    {
        var session = new EdgeSnapSession();
        EdgeSnapResult snapped = session.Apply(Rect(2, 150), Area);
        Assert.Equal(0, snapped.Rect.Left);

        // 15 px away from the applied (snapped) position is still inside the
        // 22 DIP breakaway window: the window stays flush.
        EdgeSnapResult held = session.Apply(Rect(17, 150), Area, 1.0, fromApplied: true);
        Assert.Equal(0, held.Rect.Left);
        Assert.Equal(SnapEdges.Left, held.Edges);

        // 40 px away is past the breakaway: the magnet lets go and the raw
        // position is applied.
        EdgeSnapResult released = session.Apply(Rect(42, 150), Area, 1.0, fromApplied: true);
        Assert.Equal(SnapEdges.None, released.Edges);
        Assert.True(released.Rect.Left > 0, "window must leave the edge after breakaway");
    }

    /// <summary>
    /// Regression for issue 4: Windows proposes every WM_MOVING rectangle from
    /// the already snapped position, so re-snapping from that rectangle saw a
    /// 1 px distance forever and the window could never be pulled off the edge.
    /// The session must recover the raw cursor position and let the cumulative
    /// movement escape.
    /// </summary>
    [Fact]
    public void CumulativeSmallProposalsCanEscapeTheMagnet()
    {
        var session = new EdgeSnapSession();
        EdgeSnapResult first = session.Apply(Rect(10, 200), Area);
        Assert.Equal(0, first.Rect.Left);
        Assert.Equal(SnapEdges.Left, first.Edges);

        EdgeSnapResult step = first;
        int steps = 0;
        while (step.IsSnapped && steps < 100)
        {
            steps++;
            step = session.Apply(
                Rect(step.Rect.Left + 1, 200),
                Area,
                1.0,
                fromApplied: true);
        }

        Assert.False(step.IsSnapped);
        // The magnet was entered at raw x=10, so the raw position must travel
        // past 22 before the 1 px proposals release it: 13 proposals.
        Assert.Equal(13, steps);
        Assert.Equal(23, step.Rect.Left);
    }

    [Fact]
    public void ReleasedWindowDoesNotReSnapWhileMovingAway()
    {
        var session = new EdgeSnapSession();
        session.Apply(Rect(2, 150), Area);

        EdgeSnapResult released = session.Apply(Rect(40, 150), Area, 1.0, fromApplied: true);
        Assert.Equal(SnapEdges.None, released.Edges);

        EdgeSnapResult next = session.Apply(Rect(41, 150), Area, 1.0, fromApplied: true);
        Assert.Equal(41, next.Rect.Left);
        Assert.Equal(SnapEdges.None, next.Edges);
    }

    [Fact]
    public void ResetForgetsTheMagnet()
    {
        var session = new EdgeSnapSession();
        Assert.Equal(0, session.Apply(Rect(2, 150), Area).Rect.Left);

        session.Reset();

        EdgeSnapResult afterReset = session.Apply(Rect(2, 150), Area, 1.0, fromApplied: true);
        Assert.Equal(SnapEdges.Left, afterReset.Edges);
        Assert.Equal(0, afterReset.Rect.Left);
    }

    [Fact]
    public void WorkAreaOffsetIsHonouredOnSecondaryMonitor()
    {
        var session = new EdgeSnapSession();
        var secondary = new WorkArea(1920, 0, 1920, 1040);

        EdgeSnapResult result = session.Apply(Rect(1924, 300), secondary);

        Assert.Equal(1920, result.Rect.Left);
        Assert.Equal(SnapEdges.Left, result.Edges);
    }

    [Fact]
    public void SnappingNeverChangesSize()
    {
        var session = new EdgeSnapSession();
        WindowRect proposed = new(0, 0, 1280, 720);

        EdgeSnapResult result = session.Apply(proposed, Area);

        Assert.Equal(1280, result.Rect.Width);
        Assert.Equal(720, result.Rect.Height);
        Assert.True(PipGeometry.IsAspectLocked(result.Rect.Width, result.Rect.Height));
    }

    [Fact]
    public void EmptyRectangleOrWorkAreaIsIgnored()
    {
        var session = new EdgeSnapSession();

        Assert.Equal(SnapEdges.None, session.Apply(new WindowRect(0, 0, 0, 0), Area).Edges);
        Assert.Equal(
            SnapEdges.None,
            session.Apply(Rect(2, 2), new WorkArea(0, 0, 0, 0)).Edges);
    }

    [Fact]
    public void BreakawayMustNotBeShorterThanSnapDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeSnapSession(10, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeSnapSession(-1, 22));
        _ = new EdgeSnapSession(10, 10);
    }

    [Fact]
    public void WindowLargerThanWorkAreaSticksToLeadingEdges()
    {
        var session = new EdgeSnapSession();
        var small = new WorkArea(0, 0, 640, 360);

        EdgeSnapResult result = session.Apply(Rect(0, 0, 1000, 700), small);

        Assert.Equal(0, result.Rect.Left);
        Assert.Equal(0, result.Rect.Top);
        Assert.Equal(SnapEdges.Left | SnapEdges.Top, result.Edges);
    }
}
