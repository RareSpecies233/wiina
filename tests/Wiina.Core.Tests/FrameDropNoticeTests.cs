using Wiina.Core.Playback;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// The frame-drop message used to be pushed through mpv's show-text with ASS
/// positioning tags; this libmpv build printed those tags as literal text. The
/// notice is now drawn by an in-app card whose wording comes from
/// <see cref="FrameDropNotice.Describe"/>, so the format is testable here.
/// </summary>
public sealed class FrameDropNoticeTests
{
    [Fact]
    public void DescribeFormatsRateWindowAndTotals()
    {
        var notice = new FrameDropNotice(
            Drops: 7,
            Seconds: 3.2,
            PerMinute: 132,
            TotalDrops: 41);

        Assert.Equal(
            "Wiina · 丢帧 132 帧/分（3.2 秒内 7 帧，累计 41）",
            notice.Describe());
    }

    [Fact]
    public void DescribeRoundsRateAndWindowForDisplay()
    {
        var notice = new FrameDropNotice(
            Drops: 3,
            Seconds: 2.9,
            PerMinute: 60.4,
            TotalDrops: 9);

        string text = notice.Describe();
        Assert.Equal("Wiina · 丢帧 60 帧/分（2.9 秒内 3 帧，累计 9）", text);
    }

    [Fact]
    public void DescribeNeverEmitsAssPositioningMarkup()
    {
        // Regression guard: the old message began with {\an5\pos(x,y)} and the
        // OSD rendered that markup as text.
        string text = new FrameDropNotice(1, 1, 60, 1).Describe();
        Assert.DoesNotContain("\\an", text);
        Assert.DoesNotContain("\\pos", text);
        Assert.DoesNotContain("{", text);
    }
}
