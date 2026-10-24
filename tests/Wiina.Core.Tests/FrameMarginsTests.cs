using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Issue 2 contract: snapping must operate on the visible frame, not on the
/// outer window rect. Windows reports outer rects (GetWindowRect / AppWindow /
/// WM_MOVING) that include the invisible resize border (≈8 px on left, right and
/// bottom at 100%, 0 at the top), so the old code left a 5–10 px gap on three
/// sides while the top edge looked flush.
/// </summary>
public sealed class FrameMarginsTests
{
    private static readonly FrameMargins Synthetic = new(8, 0, 8, 8);

    [Fact]
    public void NoneIsTheIdentityConversion()
    {
        WindowRect outer = new(100, 150, 916, 608);

        Assert.Equal(outer, FrameMargins.None.ToVisible(outer));
        Assert.Equal(outer, FrameMargins.None.ToOuter(outer));
        Assert.True(FrameMargins.None.IsZero);
        Assert.Equal(0, FrameMargins.None.Horizontal);
        Assert.Equal(0, FrameMargins.None.Vertical);
    }

    [Fact]
    public void VisibleAndOuterAreExactInverses()
    {
        WindowRect outer = new(100, 150, 916, 608);

        WindowRect visible = Synthetic.ToVisible(outer);
        Assert.Equal(new WindowRect(108, 150, 908, 600), visible);
        Assert.Equal(outer, Synthetic.ToOuter(visible));
    }

    [Fact]
    public void VisibleFrameIsInsetByTheMarginsOnEverySide()
    {
        WindowRect visible = Synthetic.ToVisible(new WindowRect(0, 0, 816, 458));

        Assert.Equal(8, visible.Left);
        Assert.Equal(0, visible.Top);
        Assert.Equal(800, visible.Width);
        Assert.Equal(450, visible.Height);
    }

    [Fact]
    public void MarginsAreRebuiltFromMeasuredRects()
    {
        FrameMargins margins = FrameMargins.FromRects(
            new WindowRect(0, 0, 800, 600),
            new WindowRect(8, 0, 792, 592));

        Assert.Equal(Synthetic, margins);
    }

    [Fact]
    public void NegativeMarginsAreClampedToZero()
    {
        // Some maximized / DWM-rounded states report a visible frame slightly
        // larger than GetWindowRect; the conversion must never push inwards, and
        // genuinely positive insets are kept.
        FrameMargins margins = FrameMargins.FromRects(
            new WindowRect(0, 0, 800, 600),
            new WindowRect(-4, 2, 804, 604));

        Assert.Equal(0, margins.Left);
        Assert.Equal(2, margins.Top);
        Assert.Equal(0, margins.Right);
        Assert.Equal(0, margins.Bottom);
    }

    [Fact]
    public void VisibleSizeSubtractsBothBorders()
    {
        Assert.Equal((800, 450), Synthetic.VisibleSize(816, 458));
        Assert.Equal((0, 0), Synthetic.VisibleSize(8, 4));
    }
}

/// <summary>
/// The snap session keeps its existing threshold / breakaway behaviour, but the
/// distances are measured from the visible frame and the returned rectangle is
/// the outer rect the caller applies to AppWindow.Move / WM_MOVING.
/// </summary>
public sealed class EdgeSnapFrameMarginTests
{
    private static readonly WorkArea Area = new(0, 0, 1920, 1040);

    private static readonly FrameMargins Margins = new(8, 0, 8, 8);

    // 816x458 outer == 800x450 visible.
    private static WindowRect Rect(int x, int y) => new(x, y, x + 816, y + 458);

    [Fact]
    public void LeftEdgeSnapsTheVisibleFrameFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(-5, 150), Area, margins: Margins);

        // Visible left would be 3, so the outer rect must move to -8.
        Assert.Equal(-8, result.Rect.Left);
        Assert.Equal(SnapEdges.Left, result.Edges);
        Assert.Equal(Area.X, Margins.ToVisible(result.Rect).Left);
        Assert.Equal(816, result.Rect.Width);
    }

    [Fact]
    public void RightEdgeSnapsTheVisibleFrameFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(1108, 150), Area, margins: Margins);

        Assert.Equal(1112, result.Rect.Left);
        Assert.Equal(SnapEdges.Right, result.Edges);
        Assert.Equal(Area.Right, Margins.ToVisible(result.Rect).Right);
    }

    [Fact]
    public void BottomEdgeSnapsTheVisibleFrameFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(200, 594), Area, margins: Margins);

        Assert.Equal(590, result.Rect.Top);
        Assert.Equal(SnapEdges.Bottom, result.Edges);
        Assert.Equal(Area.Bottom, Margins.ToVisible(result.Rect).Bottom);
    }

    [Fact]
    public void TopEdgeSnapsTheVisibleFrameFlush()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(200, -2), Area, margins: Margins);

        Assert.Equal(0, result.Rect.Top);
        Assert.Equal(SnapEdges.Top, result.Edges);
        Assert.Equal(Area.Y, Margins.ToVisible(result.Rect).Top);
    }

    [Fact]
    public void AllFourEdgesReachTheWorkAreaEdgesExactly()
    {
        var session = new EdgeSnapSession();

        Assert.Equal(new WindowRect(-8, 150, 808, 608), session.Apply(Rect(-5, 150), Area, margins: Margins).Rect);
        session.Reset();
        Assert.Equal(new WindowRect(1112, 150, 1928, 608), session.Apply(Rect(1108, 150), Area, margins: Margins).Rect);
        session.Reset();
        Assert.Equal(new WindowRect(200, 0, 1016, 458), session.Apply(Rect(200, -2), Area, margins: Margins).Rect);
        session.Reset();
        Assert.Equal(new WindowRect(200, 590, 1016, 1048), session.Apply(Rect(200, 594), Area, margins: Margins).Rect);
    }

    [Fact]
    public void ThresholdStillUsesVisibleSpace()
    {
        var session = new EdgeSnapSession();

        // Visible left 10 -> exactly the 10 DIP snap distance.
        Assert.Equal(-8, session.Apply(Rect(2, 150), Area, margins: Margins).Rect.Left);

        session.Reset();

        // Visible left 11 -> outside the threshold, so it must not move.
        EdgeSnapResult outside = session.Apply(Rect(3, 150), Area, margins: Margins);
        Assert.Equal(3, outside.Rect.Left);
        Assert.Equal(SnapEdges.None, outside.Edges);
    }

    [Fact]
    public void ThresholdScalesWithDpiInVisibleSpace()
    {
        var session = new EdgeSnapSession();
        var margins = new FrameMargins(12, 0, 12, 12);
        WindowRect outer = new(6, 150, 6 + 1224, 150 + 687); // visible left 18, width 1200

        // 10 DIP at 150% is 15 px; visible left 16 is outside.
        EdgeSnapResult outside = session.Apply(outer, Area, scale: 1.5, margins: margins);
        Assert.Equal(6, outside.Rect.Left);
        Assert.Equal(SnapEdges.None, outside.Edges);

        session.Reset();

        // Visible left 14 is inside the 15 px threshold -> visible flush, outer -12.
        EdgeSnapResult inside = session.Apply(
            new WindowRect(2, 150, 2 + 1224, 150 + 687),
            Area,
            scale: 1.5,
            margins: margins);
        Assert.Equal(-12, inside.Rect.Left);
        Assert.Equal(SnapEdges.Left, inside.Edges);
    }

    [Fact]
    public void MagnetHoldsAndBreaksAwayInVisibleSpace()
    {
        var session = new EdgeSnapSession();
        EdgeSnapResult snapped = session.Apply(Rect(-5, 150), Area, margins: Margins);
        Assert.Equal(-8, snapped.Rect.Left);

        // 15 px away from the applied position is still inside the breakaway.
        EdgeSnapResult held = session.Apply(Rect(7, 150), Area, 1.0, fromApplied: true, margins: Margins);
        Assert.Equal(-8, held.Rect.Left);
        Assert.Equal(SnapEdges.Left, held.Edges);

        // 40 px away is past the breakaway: the magnet lets go and the recovered
        // raw visible position is applied (it may overshoot by the hysteresis
        // window, exactly like the outer-rect path).
        EdgeSnapResult released = session.Apply(Rect(32, 150), Area, 1.0, fromApplied: true, margins: Margins);
        Assert.Equal(SnapEdges.None, released.Edges);
        Assert.True(released.Rect.Left > 0, "window must leave the edge after breakaway");
        Assert.Equal(8, Margins.ToVisible(released.Rect).Left - released.Rect.Left);
    }

    [Fact]
    public void SnappingNeverChangesTheOuterSize()
    {
        var session = new EdgeSnapSession();

        EdgeSnapResult result = session.Apply(Rect(1108, 594), Area, margins: Margins);

        Assert.Equal(816, result.Rect.Width);
        Assert.Equal(458, result.Rect.Height);
        Assert.Equal(1112, result.Rect.Left);
        Assert.Equal(590, result.Rect.Top);
    }

    [Fact]
    public void ZeroMarginsKeepTheOldOuterRectBehaviour()
    {
        var session = new EdgeSnapSession();

        Assert.Equal(new WindowRect(0, 150, 816, 608), session.Apply(Rect(-5, 150), Area).Rect);
    }
}
