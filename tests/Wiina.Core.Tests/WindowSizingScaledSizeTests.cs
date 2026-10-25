using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// The 0.5x / 1x / 2x window-size command must be derived from the actual video
/// resolution: one video display pixel maps to one physical window pixel, the
/// aspect ratio survives, the result never leaves the work area and never drops
/// below the minimum window size.
/// </summary>
public class WindowSizingScaledSizeTests
{
    [Fact]
    public void NoVideoUsesTheFallbackFrame()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            0, 0, 1.0, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1280, 720), size);
        Assert.Equal((1280, 720), WindowSizing.ToPhysical(size!.Value, 1.0));
    }

    [Fact]
    public void HalfScaleOfFullHdAtHundredPercent()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 0.5, 1.0, 2560, 1440);

        Assert.Equal(new DipSize(960, 540), size);
        Assert.Equal((960, 540), WindowSizing.ToPhysical(size!.Value, 1.0));
    }

    [Fact]
    public void OneToOneShowsEveryVideoPixel()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 1.0, 1.0, 2560, 1440);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void TwoTimesShowsEveryVideoPixelTwice()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 2.0, 1.0, 3840, 2160);

        Assert.Equal(new DipSize(3840, 2160), size);
        Assert.Equal((3840, 2160), WindowSizing.ToPhysical(size!.Value, 1.0));
    }

    [Fact]
    public void FullHdAtOneToOneOnA150PercentDisplay()
    {
        // Physical pixels are the video pixels; DIP divides by the DPI scale.
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 1.0, 1.5, 2560, 1440);

        Assert.Equal(new DipSize(1280, 720), size);
        Assert.Equal((1920, 1080), WindowSizing.ToPhysical(size!.Value, 1.5));
    }

    [Fact]
    public void HalfScaleOnA200PercentDisplay()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 0.5, 2.0, 2560, 1440);

        Assert.Equal(new DipSize(480, 270), size);
        Assert.Equal((960, 540), WindowSizing.ToPhysical(size!.Value, 2.0));
    }

    [Fact]
    public void TwoTimesFourKIsClampedToTheWorkArea()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            3840, 2160, 2.0, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void FourKAtOneToOneOnA200PercentDisplay()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            3840, 2160, 1.0, 2.0, 3840, 2160);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void PortraitVideoIsClampedByTheWorkAreaHeight()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1080, 1920, 1.0, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(608, 1080), size);
    }

    [Fact]
    public void TinyVideoGrowsToTheMinimumWindowSize()
    {
        // 320x180 at 0.5x is 160x90 physical pixels, below the 285x120 DIP floor.
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            320, 180, 0.5, 1.0, 1920, 1080);

        Assert.NotNull(size);
        Assert.Equal(285, size!.Value.Width);
        Assert.Equal(160, size.Value.Height);
    }

    [Fact]
    public void AnamorphicDisplaySizeKeepsTheWideAspect()
    {
        // dw/dh (854x480) is already aspect-corrected for a 720x480 source.
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            854, 480, 1.0, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(854, 480), size);
        Assert.Equal(854.0 / 480.0, size!.Value.Width / (double)size.Value.Height, 2);
    }

    [Fact]
    public void MinimumSizeNeverPushesTheWindowOffASmallWorkArea()
    {
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1280, 720, 2.0, 2.0, 200, 100);

        Assert.NotNull(size);
        (int width, int height) = WindowSizing.ToPhysical(size!.Value, 2.0);
        Assert.True(width <= 200, $"width {width} exceeds the work area");
        Assert.True(height <= 100, $"height {height} exceeds the work area");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void OneToOneRoundTripsAtEveryDpiScale(double scale)
    {
        // 1x must land back on the exact video resolution after the DIP round trip.
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            1920, 1080, 1.0, scale, 3840, 2160);

        Assert.NotNull(size);
        Assert.Equal((1920, 1080), WindowSizing.ToPhysical(size!.Value, scale));
    }

    [Theory]
    [InlineData(1920, 1080, 0.0, 1.0, 1920, 1080)]
    [InlineData(1920, 1080, -1.0, 1.0, 1920, 1080)]
    [InlineData(1920, 1080, double.NaN, 1.0, 1920, 1080)]
    [InlineData(1920, 1080, 1.0, 0.0, 1920, 1080)]
    [InlineData(1920, 1080, 1.0, 1.0, 0, 1080)]
    [InlineData(1920, 1080, 1.0, 1.0, 1920, 0)]
    public void InvalidInputsReturnNull(
        int videoWidth,
        int videoHeight,
        double factor,
        double scale,
        int workWidth,
        int workHeight)
    {
        Assert.Null(WindowSizing.ScaledVideoWindowSize(
            videoWidth, videoHeight, factor, scale, workWidth, workHeight));
    }

    [Fact]
    public void FitUsesTheVideoAspectNotSixteenByNine()
    {
        // 4:3 inside a 1920x1040 work area: height-limited, so 1387x1040.
        DipSize? size = WindowSizing.FitVideoWindowSize(
            640, 480, 1.0, 1920, 1040);

        Assert.Equal(new DipSize(1387, 1040), size);
    }

    [Fact]
    public void FitUsesTheFallbackAspectWithoutVideo()
    {
        DipSize? size = WindowSizing.FitVideoWindowSize(
            0, 0, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void FitOnA150PercentDisplay()
    {
        DipSize? size = WindowSizing.FitVideoWindowSize(
            1920, 1080, 1.5, 1920, 1080);

        Assert.Equal(new DipSize(1280, 720), size);
        Assert.Equal((1920, 1080), WindowSizing.ToPhysical(size!.Value, 1.5));
    }

    [Fact]
    public void FitOfAPortraitVideo()
    {
        DipSize? size = WindowSizing.FitVideoWindowSize(
            1080, 1920, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(608, 1080), size);
    }

    [Fact]
    public void FitStillHonoursTheMinimumWindowSize()
    {
        DipSize? size = WindowSizing.FitVideoWindowSize(
            100, 100, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1080, 1080), size);
    }

    [Theory]
    [InlineData(1920, 1080, 0.0, 1920, 1080)]
    [InlineData(1920, 1080, 1.0, 0, 1080)]
    [InlineData(1920, 1080, 1.0, 1920, 0)]
    public void FitInvalidInputsReturnNull(
        int videoWidth,
        int videoHeight,
        double scale,
        int workWidth,
        int workHeight)
    {
        Assert.Null(WindowSizing.FitVideoWindowSize(
            videoWidth, videoHeight, scale, workWidth, workHeight));
    }
}
