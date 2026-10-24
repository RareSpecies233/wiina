using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Issue 3 contract: the panel layout size (full window) and the mpv render /
/// composition size (capped) are two different numbers. Only the render size is
/// capped; the cap must never leak into the XAML surface size.
/// </summary>
public sealed class RenderSizeTests
{
    [Fact]
    public void CapIsSixFortyByThreeSixty()
    {
        Assert.Equal(640, PipGeometry.MaxRenderWidth);
        Assert.Equal(360, PipGeometry.MaxRenderHeight);
    }

    [Theory]
    [InlineData(480, 270, 480, 270)]
    [InlineData(640, 360, 640, 360)]
    [InlineData(320, 180, 320, 180)]
    public void SmallSurfacesRenderAtWindowSize(
        int pixelWidth,
        int pixelHeight,
        int expectedWidth,
        int expectedHeight)
    {
        (int width, int height) = PipGeometry.RenderSize(pixelWidth, pixelHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData(1920, 1080, 640, 360)]
    [InlineData(1280, 720, 640, 360)]
    [InlineData(800, 450, 640, 360)]
    [InlineData(960, 540, 640, 360)]
    public void LargeSixteenByNineSurfacesCapAtSixFortyByThreeSixty(
        int pixelWidth,
        int pixelHeight,
        int expectedWidth,
        int expectedHeight)
    {
        (int width, int height) = PipGeometry.RenderSize(pixelWidth, pixelHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData(1000, 300)]
    [InlineData(700, 394)]
    [InlineData(2560, 1080)]
    public void CapKeepsTheWindowAspectRatio(int pixelWidth, int pixelHeight)
    {
        (int width, int height) = PipGeometry.RenderSize(pixelWidth, pixelHeight);

        Assert.True(width <= PipGeometry.MaxRenderWidth);
        Assert.True(height <= PipGeometry.MaxRenderHeight);
        double sourceAspect = (double)pixelWidth / pixelHeight;
        double renderAspect = (double)width / height;
        Assert.InRange(renderAspect, sourceAspect * 0.98, sourceAspect * 1.02);
    }

    [Fact]
    public void DegenerateSizeBecomesOnePixel()
    {
        Assert.Equal((1, 1), PipGeometry.RenderSize(0, 0));
        Assert.Equal((1, 1), PipGeometry.RenderSize(-5, 10));
    }

    [Fact]
    public void RenderSizeNeverExceedsLayoutSize()
    {
        (int width, int height) = PipGeometry.RenderSize(500, 281);

        Assert.True(width <= 500);
        Assert.True(height <= 281);
    }
}
