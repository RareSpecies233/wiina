using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Issue 7 contract: the mini/PiP window renders at exactly the full window
/// pixel size. There is no low-resolution render buffer and therefore no
/// compositor upscale — the mini window is as sharp as the main window and only
/// saves work by dropping the heavy UI chrome.
/// </summary>
public sealed class RenderSizeTests
{
    [Theory]
    [InlineData(320, 180)]
    [InlineData(480, 270)]
    [InlineData(640, 360)]
    [InlineData(800, 450)]
    [InlineData(960, 540)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void RenderSizeIsTheFullWindowPixelSize(int pixelWidth, int pixelHeight)
    {
        (int width, int height) = PipGeometry.RenderSize(pixelWidth, pixelHeight);

        Assert.Equal(pixelWidth, width);
        Assert.Equal(pixelHeight, height);
    }

    [Theory]
    [InlineData(1000, 300)]
    [InlineData(700, 394)]
    [InlineData(2560, 1080)]
    public void OddAspectRatiosAreNotReshaped(int pixelWidth, int pixelHeight)
    {
        (int width, int height) = PipGeometry.RenderSize(pixelWidth, pixelHeight);

        // The render size must match the surface exactly; any scaling (including
        // aspect-preserving scaling) would show up as blur or bars.
        Assert.Equal((pixelWidth, pixelHeight), (width, height));
    }

    [Fact]
    public void HighDpiPhysicalPixelsAreKept()
    {
        // 480x270 DIP at 150% / 200% -> the full physical pixel size is rendered.
        Assert.Equal((720, 405), PipGeometry.RenderSize(720, 405));
        Assert.Equal((960, 540), PipGeometry.RenderSize(960, 540));
    }

    [Fact]
    public void DegenerateSizeBecomesOnePixel()
    {
        Assert.Equal((1, 1), PipGeometry.RenderSize(0, 0));
        Assert.Equal((1, 1), PipGeometry.RenderSize(-5, 10));
        Assert.Equal((1, 1), PipGeometry.RenderSize(10, 0));
    }
}
