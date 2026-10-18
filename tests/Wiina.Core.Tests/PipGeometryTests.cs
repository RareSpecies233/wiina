using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class PipGeometryTests
{
    [Fact]
    public void DefaultIsSixteenByNine()
    {
        Assert.Equal(480, PipGeometry.DefaultWidth);
        Assert.Equal(270, PipGeometry.DefaultHeight);
        Assert.True(PipGeometry.IsAspectLocked(
            PipGeometry.DefaultWidth,
            PipGeometry.DefaultHeight));
    }

    [Theory]
    [InlineData(480, 270, 480, 270)]
    [InlineData(200, 100, 320, 180)]
    [InlineData(320, 180, 320, 180)]
    [InlineData(1000, 300, 1000, 562)]
    [InlineData(0, 0, 320, 180)]
    public void ClampKeepsAspectAndMinimum(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight)
    {
        (int actualWidth, int actualHeight) = PipGeometry.Clamp(width, height);

        Assert.Equal(expectedWidth, actualWidth);
        Assert.Equal(expectedHeight, actualHeight);
        Assert.True(PipGeometry.IsAspectLocked(actualWidth, actualHeight));
    }

    [Theory]
    [InlineData(400, 225)]
    [InlineData(800, 450)]
    public void HeightFollowsSixteenByNine(double width, double expectedHeight)
    {
        Assert.Equal(expectedHeight, PipGeometry.HeightForWidth(width), 3);
    }

    [Fact]
    public void FitIntoNeverExceedsTheBudget()
    {
        (int width, int height) = PipGeometry.FitInto(1920, 1000);

        Assert.InRange(width, PipGeometry.MinWidth, 1920);
        Assert.InRange(height, PipGeometry.MinHeight, 1000);
        Assert.True(PipGeometry.IsAspectLocked(width, height));
    }

    [Fact]
    public void PlaceBottomRightKeepsAWindowInsideTheWorkArea()
    {
        (int x, int y) = PipGeometry.PlaceBottomRight(0, 0, 1920, 1080, 480, 270, 16);

        Assert.Equal(1424, x);
        Assert.Equal(794, y);

        (int tinyX, int tinyY) = PipGeometry.PlaceBottomRight(0, 0, 400, 300, 480, 270, 16);
        Assert.Equal(0, tinyX);
        Assert.Equal(14, tinyY);
    }

    [Theory]
    [InlineData(480, 270, true)]
    [InlineData(1000, 563, true)]
    [InlineData(480, 480, false)]
    [InlineData(0, 270, false)]
    public void AspectLockedChecker(int width, int height, bool expected)
    {
        Assert.Equal(expected, PipGeometry.IsAspectLocked(width, height));
    }
}
