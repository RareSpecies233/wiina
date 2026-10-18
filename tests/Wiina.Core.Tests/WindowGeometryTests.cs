using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class WindowGeometryTests
{
    private const double Wide = 16.0 / 9.0;

    [Theory]
    [InlineData(WindowResizeEdge.Right, 100, 0, 1100, 500, 100, 0, 1100, 562)]
    [InlineData(WindowResizeEdge.Left, 100, 0, 1380, 900, 100, 0, 1380, 720)]
    [InlineData(WindowResizeEdge.Bottom, 0, 0, 1280, 500, 0, 0, 889, 500)]
    [InlineData(WindowResizeEdge.Top, 0, 100, 1280, 1000, 0, 100, 1600, 1000)]
    public void StraightEdgesDriveTheirOwnAxisAndKeepOppositeEdgeAnchored(
        WindowResizeEdge edge,
        int left,
        int top,
        int right,
        int bottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        WindowRect result = WindowGeometry.ResizeWithAspect(
            new WindowRect(left, top, right, bottom), edge, Wide, 285, 120);

        Assert.Equal(new WindowRect(expectedLeft, expectedTop, expectedRight, expectedBottom), result);
    }

    [Theory]
    [InlineData(WindowResizeEdge.TopLeft, 100, 100, 1380, 820, 100, 100, 1380, 820)]
    [InlineData(WindowResizeEdge.TopRight, 0, 100, 1280, 820, 0, 100, 1280, 820)]
    [InlineData(WindowResizeEdge.BottomLeft, 100, 0, 1380, 720, 100, 0, 1380, 720)]
    [InlineData(WindowResizeEdge.BottomRight, 0, 0, 1280, 720, 0, 0, 1280, 720)]
    public void CornersKeepTheOppositeCornerFixed(
        WindowResizeEdge edge,
        int left,
        int top,
        int right,
        int bottom,
        int expectedLeft,
        int expectedTop,
        int expectedRight,
        int expectedBottom)
    {
        WindowRect result = WindowGeometry.ResizeWithAspect(
            new WindowRect(left, top, right, bottom), edge, Wide, 285, 120);

        Assert.Equal(new WindowRect(expectedLeft, expectedTop, expectedRight, expectedBottom), result);
    }

    [Fact]
    public void HorizontalEdgeNeverGoesBelowMinimumTrackSize()
    {
        WindowRect result = WindowGeometry.ResizeWithAspect(
            new WindowRect(0, 0, 100, 60),
            WindowResizeEdge.Right,
            Wide,
            285,
            120);

        Assert.Equal(285, result.Width);
        Assert.True(result.Height >= 120);
    }

    [Fact]
    public void VerticalEdgeNeverGoesBelowMinimumTrackSize()
    {
        WindowRect result = WindowGeometry.ResizeWithAspect(
            new WindowRect(0, 0, 100, 50),
            WindowResizeEdge.Bottom,
            Wide,
            285,
            120);

        Assert.True(result.Width >= 285);
        Assert.True(result.Height >= 120);
    }

    [Fact]
    public void ReapplyingTheResolvedRectangleIsStable()
    {
        WindowRect proposed = new(10, 20, 1010, 900);
        WindowRect once = WindowGeometry.ResizeWithAspect(
            proposed, WindowResizeEdge.Right, Wide, 285, 120);
        WindowRect twice = WindowGeometry.ResizeWithAspect(
            once, WindowResizeEdge.Right, Wide, 285, 120);

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAspectLeavesTheProposalUntouched(double aspect)
    {
        WindowRect proposed = new(0, 0, 800, 600);
        Assert.Equal(
            proposed,
            WindowGeometry.ResizeWithAspect(
                proposed, WindowResizeEdge.Right, aspect, 285, 120));
    }

    [Fact]
    public void DegenerateRectangleIsIgnored()
    {
        WindowRect proposed = new(10, 10, 10, 500);
        Assert.Equal(
            proposed,
            WindowGeometry.ResizeWithAspect(
                proposed, WindowResizeEdge.Right, Wide, 285, 120));
    }
}
