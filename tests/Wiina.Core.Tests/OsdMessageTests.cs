using Wiina.Core.Osd;
using Xunit;

namespace Wiina.Core.Tests;

public class OsdMessageTests
{
    [Fact]
    public void VolumeUsesPercentIconAndProgress()
    {
        OsdMessage message = OsdMessages.Volume(50);

        Assert.Equal("音量 50%", message.Text);
        Assert.Equal(OsdMessageKind.Volume, message.Kind);
        Assert.Equal(OsdIcons.Volume, message.IconGlyph);
        Assert.Equal(0.5, message.Progress);
    }

    [Fact]
    public void VolumeAtZeroUsesMuteIconAndOverAmplificationClamps()
    {
        Assert.Equal(OsdIcons.Mute, OsdMessages.Volume(0).IconGlyph);
        Assert.Equal(1, OsdMessages.Volume(130).Progress);
    }

    [Fact]
    public void SeekShowsLeftToRightTimeAndDuration()
    {
        OsdMessage message = OsdMessages.Seek(60, 120);

        Assert.Equal("1:00", message.Text);
        Assert.Equal("2:00", message.Secondary);
        Assert.Equal(0.5, message.Progress);
        Assert.True(message.LeftToRight);
        Assert.Equal(OsdMessageKind.Seek, message.Kind);
    }

    [Fact]
    public void SeekWithoutDurationHasNoProgress()
    {
        OsdMessage message = OsdMessages.Seek(10, 0);
        Assert.Null(message.Progress);
    }

    [Fact]
    public void SpeedFormatsWithProgress()
    {
        OsdMessage message = OsdMessages.Speed(2);
        Assert.Equal("速度 2x", message.Text);
        Assert.Equal(0.5, message.Progress);
    }

    [Fact]
    public void TrackAndPauseCarrySemanticIcons()
    {
        Assert.Equal("已切换音频", OsdMessages.Track("音频", "AAC").Text);
        Assert.Equal(OsdIcons.Pause, OsdMessages.Pause(true).IconGlyph);
        Assert.Equal(OsdIcons.Play, OsdMessages.Pause(false).IconGlyph);
        Assert.True(OsdMessages.Error("打开失败").AlwaysShow);
    }

    [Fact]
    public void BufferAndNetworkMessagesHaveKinds()
    {
        Assert.Equal(0.25, OsdMessages.Buffer(percent: 25).Progress);
        Assert.Equal(OsdMessageKind.Network, OsdMessages.NetworkOpening().Kind);
        Assert.Equal(OsdMessageKind.Network, OsdMessages.NetworkSeeking().Kind);
    }

    [Theory]
    [InlineData("音量 42%", OsdMessageKind.Volume)]
    [InlineData("速度 1.5x", OsdMessageKind.Speed)]
    [InlineData("暂停", OsdMessageKind.Pause)]
    [InlineData("静音", OsdMessageKind.Volume)]
    [InlineData("截图已保存", OsdMessageKind.Screenshot)]
    [InlineData("字幕显示", OsdMessageKind.Subtitle)]
    public void FromTextRecognizesCatalogueEntries(string text, OsdMessageKind kind)
    {
        Assert.Equal(kind, OsdMessages.FromText(text).Kind);
    }

    [Fact]
    public void FromTextParsesTimePairs()
    {
        OsdMessage message = OsdMessages.FromText("1:23 / 4:56");
        Assert.Equal(OsdMessageKind.Seek, message.Kind);
        Assert.Equal("1:23", message.Text);
        Assert.Equal("4:56", message.Secondary);
        Assert.True(message.LeftToRight);
        Assert.True(message.Progress is > 0);
    }

    [Fact]
    public void FromTextMapsScreenshotFailuresToErrors()
    {
        OsdMessage message = OsdMessages.FromText("截图失败", "磁盘已满");
        Assert.Equal(OsdMessageKind.Error, message.Kind);
        Assert.True(message.AlwaysShow);
    }

    [Theory]
    [InlineData("1:02", 62)]
    [InlineData("1:02:03", 3723)]
    [InlineData("0:00", 0)]
    public void TryParseTimeParsesClockForms(string text, double expected)
    {
        Assert.True(OsdMessages.TryParseTime(text, out double seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("aa:bb")]
    [InlineData("1:2:3:4")]
    public void TryParseTimeRejectsInvalidInput(string text)
    {
        Assert.False(OsdMessages.TryParseTime(text, out _));
    }

    [Fact]
    public void QueueReplacesAndCoalescesContinuousMessages()
    {
        var queue = new OsdQueue();

        OsdQueueResult first = queue.Enqueue(OsdMessages.Normal("打开"));
        OsdQueueResult volume1 = queue.Enqueue(OsdMessages.Volume(10));
        OsdQueueResult volume2 = queue.Enqueue(OsdMessages.Volume(20));
        OsdQueueResult seek1 = queue.Enqueue(OsdMessages.Seek(1, 10));
        OsdQueueResult seek2 = queue.Enqueue(OsdMessages.Seek(2, 10));
        OsdQueueResult pause = queue.Enqueue(OsdMessages.Pause(true));

        Assert.False(first.Coalesced);
        Assert.False(volume1.Coalesced);
        Assert.True(volume2.Coalesced);
        Assert.False(seek1.Coalesced);
        Assert.True(seek2.Coalesced);
        Assert.False(pause.Coalesced);
        Assert.Equal(6, pause.Version);
        Assert.Equal(OsdMessageKind.Pause, queue.Current!.Kind);
    }

    [Fact]
    public void QueueDoesNotCoalesceDifferentKinds()
    {
        Assert.False(OsdQueue.ShouldCoalesce(OsdMessages.Volume(10), OsdMessages.Seek(1, 2)));
        Assert.False(OsdQueue.ShouldCoalesce(null, OsdMessages.Volume(10)));
        Assert.True(OsdQueue.ShouldCoalesce(OsdMessages.Volume(10), OsdMessages.Volume(20)));
        Assert.True(OsdQueue.IsContinuous(OsdMessageKind.Speed));
        Assert.False(OsdQueue.IsContinuous(OsdMessageKind.Track));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(600, 500)]
    [InlineData(double.NaN, 100)]
    public void SizeClampsToConfiguredRange(double input, double expected)
    {
        Assert.Equal(expected, OsdSettings.ClampSize(input));
    }

    [Theory]
    [InlineData(0, 0.1)]
    [InlineData(60, 30)]
    [InlineData(double.NaN, 1)]
    public void DurationClampsToConfiguredRange(double input, double expected)
    {
        Assert.Equal(expected, OsdSettings.ClampDuration(input));
    }

    [Fact]
    public void TextScaleProducesSpecSizes()
    {
        Assert.Equal(20, OsdSettings.MainFontSize(100));
        Assert.Equal(10, OsdSettings.SecondaryFontSize(100));
        Assert.Equal(16, OsdSettings.IconFontSize(100));
        Assert.Equal(100, OsdSettings.MainFontSize(500));
        Assert.Equal(25, OsdSettings.SecondaryFontSize(500));
        Assert.Equal(80, OsdSettings.IconFontSize(500));
    }
}
