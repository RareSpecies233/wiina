using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class WindowSizingTests
{
    [Fact]
    public void FullHdVideoMapsOneToOneAtHundredPercent()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            1920, 1080, 1.0, 2560, 1440);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void PhysicalVideoPixelsAreConvertedToDips()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            1920, 1080, 1.5, 3840, 2160);

        Assert.Equal(new DipSize(1280, 720), size);
        Assert.Equal((1920, 1080), WindowSizing.ToPhysical(size!.Value, 1.5));
    }

    [Fact]
    public void OversizedVideoUsesHalfDimensions()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            3840, 2160, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void OversizedVideoOnHidpiUsesHalfPhysicalDimensions()
    {
        // 1080p exceeds a 1040 px-tall work area, so 960x540 physical px.
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            1920, 1080, 1.5, 1920, 1040);

        Assert.Equal(new DipSize(640, 360), size);
        Assert.Equal((960, 540), WindowSizing.ToPhysical(size!.Value, 1.5));
    }

    [Fact]
    public void HalfOfAVeryLargeFrameStillFitsProportionally()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            7680, 4320, 1.0, 1920, 1080);

        Assert.Equal(new DipSize(1920, 1080), size);
    }

    [Fact]
    public void SmallVideoKeepsTheMinimumWindowSize()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            320, 180, 1.0, 1920, 1040);

        Assert.Equal(new DipSize(320, 180), size);
    }

    [Fact]
    public void MinimumNeverExceedsTheWorkArea()
    {
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            1280, 720, 2.0, 200, 100);

        Assert.Equal(new DipSize(100, 50), size);
        Assert.Equal((200, 100), WindowSizing.ToPhysical(size!.Value, 2.0));
    }

    [Theory]
    [InlineData(0, 1080, 1.0, 1920, 1080)]
    [InlineData(1920, 0, 1.0, 1920, 1080)]
    [InlineData(1920, 1080, 0, 1920, 1080)]
    [InlineData(1920, 1080, 1.0, 0, 1080)]
    [InlineData(1920, 1080, 1.0, 1920, 0)]
    [InlineData(1920, 1080, double.NaN, 1920, 1080)]
    public void InvalidInputsReturnNull(
        int videoWidth,
        int videoHeight,
        double scale,
        int workWidth,
        int workHeight)
    {
        Assert.Null(WindowSizing.InitialVideoWindowSize(
            videoWidth, videoHeight, scale, workWidth, workHeight));
    }

    [Theory]
    [InlineData(1280, 720, 1.0, 1280, 720)]
    [InlineData(1280, 720, 1.25, 1600, 900)]
    [InlineData(1280, 720, 1.5, 1920, 1080)]
    [InlineData(1280, 720, 2.0, 2560, 1440)]
    public void ToPhysicalMultipliesByTheRasterizationScale(
        int width,
        int height,
        double scale,
        int expectedWidth,
        int expectedHeight)
    {
        Assert.Equal(
            (expectedWidth, expectedHeight),
            WindowSizing.ToPhysical(new DipSize(width, height), scale));
    }

    [Fact]
    public void ToPhysicalFallsBackToUnityForInvalidScale()
    {
        Assert.Equal(
            (1280, 720),
            WindowSizing.ToPhysical(new DipSize(1280, 720), 0));
    }
}
