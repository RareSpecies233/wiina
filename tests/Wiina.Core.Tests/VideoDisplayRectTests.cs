using Wiina.Core.Playback;
using Xunit;

namespace Wiina.Core.Tests;

public class VideoDisplayRectTests
{
    [Fact]
    public void OsdLetterboxOnTallWidgetKeepsFrameCentred()
    {
        // 506x993 DIP widget at 100% DPI showing a 16:9 frame: 506x285 with
        // 354 px bars top and bottom (mpv reports physical pixels).
        bool ok = VideoDisplayRect.TryFromOsd(
            506, 993, 1.0, 506, 285, 0, 354, 0, 354, out DisplayRect rect);

        Assert.True(ok);
        Assert.Equal(0, rect.X, 3);
        Assert.Equal(354, rect.Y, 3);
        Assert.Equal(506, rect.Width, 3);
        Assert.Equal(285, rect.Height, 3);
    }

    [Fact]
    public void OsdConvertsPhysicalPixelsToDipsAtFractionalScale()
    {
        // 150% DPI: a 960x540 physical frame inside a 960x800 physical widget.
        bool ok = VideoDisplayRect.TryFromOsd(
            640, 533.333, 1.5, 960, 540, 0, 130, 0, 130, out DisplayRect rect);

        Assert.True(ok);
        Assert.Equal(640, rect.Width, 3);
        Assert.Equal(360, rect.Height, 3);
        Assert.Equal(130.0 / 1.5, rect.Y, 3);
    }

    [Fact]
    public void OsdPillarboxOffsetsHorizontally()
    {
        // 1000x500 widget, 4:3 frame => 666x500 pillarboxed by 167 px each side.
        bool ok = VideoDisplayRect.TryFromOsd(
            1000, 500, 1.0, 667, 500, 166, 0, 167, 0, out DisplayRect rect);

        Assert.True(ok);
        Assert.Equal(166, rect.X, 3);
        Assert.Equal(667, rect.Width, 3);
        Assert.Equal(500, rect.Height, 3);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]          // mpv has no video yet
    [InlineData(1920, 1080, 0, 0, 0, 0)]    // stale values from another surface
    [InlineData(506, 285, 0, 354, 0, 0)]    // margins do not add up to the widget
    [InlineData(-1, 285, -500, 0, 0, 0)]    // negative margins
    public void OsdValuesThatDoNotDescribeTheWidgetAreRejected(
        double width, double height, double left, double top, double right, double bottom)
    {
        Assert.False(VideoDisplayRect.TryFromOsd(
            506, 993, 1.0, width, height, left, top, right, bottom, out _));
    }

    [Fact]
    public void FitLetterboxesAWideFrame()
    {
        DisplayRect rect = VideoDisplayRect.Fit(1000, 1000, 16.0 / 9.0);

        Assert.Equal(0, rect.X, 3);
        Assert.Equal(218.75, rect.Y, 3);
        Assert.Equal(1000, rect.Width, 3);
        Assert.Equal(562.5, rect.Height, 3);
    }

    [Fact]
    public void FitPillarboxesANarrowFrame()
    {
        DisplayRect rect = VideoDisplayRect.Fit(1000, 500, 4.0 / 3.0);

        Assert.Equal(166.667, rect.X, 3);
        Assert.Equal(0, rect.Y, 3);
        Assert.Equal(666.667, rect.Width, 3);
        Assert.Equal(500, rect.Height, 3);
    }

    [Fact]
    public void FitRejectsDegenerateInputs()
    {
        Assert.True(VideoDisplayRect.Fit(0, 500, 1.78).IsEmpty);
        Assert.True(VideoDisplayRect.Fit(1000, 500, 0).IsEmpty);
        Assert.True(VideoDisplayRect.Fit(1000, 500, double.NaN).IsEmpty);
    }

    [Fact]
    public void ClampPullsAnOverflowingRectInsideTheWidget()
    {
        DisplayRect clamped = VideoDisplayRect.Clamp(new DisplayRect(-20, 100, 1200, 400), 1000, 500);

        Assert.Equal(0, clamped.X, 3);
        Assert.Equal(100, clamped.Y, 3);
        Assert.Equal(1000, clamped.Width, 3);
        Assert.Equal(400, clamped.Height, 3);
    }

    [Fact]
    public void ClampDropsRectanglesLyingOutside()
    {
        Assert.True(VideoDisplayRect.Clamp(new DisplayRect(0, 0, 0, 0), 1000, 500).IsEmpty);
        Assert.True(VideoDisplayRect.Clamp(new DisplayRect(1000, 0, 10, 10), 1000, 500).IsEmpty);
    }
}
