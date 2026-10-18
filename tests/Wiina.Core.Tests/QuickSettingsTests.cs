using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class QuickSettingsTests
{
    [Theory]
    [InlineData(-5, 130, 0)]
    [InlineData(50, 130, 50)]
    [InlineData(140, 130, 130)]
    [InlineData(120, 90, 100)]
    public void VolumeIsClampedToMaxVolume(double value, double maxVolume, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampVolume(value, maxVolume));
    }

    [Theory]
    [InlineData(80, 100)]
    [InlineData(130, 130)]
    [InlineData(500, 200)]
    public void MaxVolumeStaysInAudioRange(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampMaxVolume(value));
    }

    [Theory]
    [InlineData(-20, -10)]
    [InlineData(1.5, 1.5)]
    [InlineData(20, 10)]
    public void AudioDelayIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampAudioDelay(value));
    }

    [Theory]
    [InlineData(-100, -30)]
    [InlineData(0.5, 0.5)]
    [InlineData(100, 30)]
    public void SubtitleDelayIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampSubtitleDelay(value));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(75, 75)]
    [InlineData(200, 150)]
    public void SubtitlePositionIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampSubtitlePosition(value));
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(1.25, 1.25)]
    [InlineData(4, 2.0)]
    public void SubtitleScaleIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampSubtitleScale(value));
    }

    [Theory]
    [InlineData(4, 8)]
    [InlineData(55, 55)]
    [InlineData(500, 120)]
    public void SubtitleFontSizeIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampSubtitleFontSize(value));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(3, 3)]
    [InlineData(30, 10)]
    public void SubtitleBorderIsClamped(double value, double expected)
    {
        Assert.Equal(expected, QuickSettings.ClampSubtitleBorder(value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(44, 0)]
    [InlineData(46, 90)]
    [InlineData(91, 90)]
    [InlineData(200, 180)]
    [InlineData(300, 270)]
    [InlineData(360, 0)]
    [InlineData(-90, 270)]
    public void RotationSnapsToQuarterTurns(int value, int expected)
    {
        Assert.Equal(expected, QuickSettings.NormalizeRotation(value));
    }

    [Fact]
    public void RotationCyclesThroughFourSteps()
    {
        Assert.Equal(90, QuickSettings.NextRotation(0));
        Assert.Equal(180, QuickSettings.NextRotation(90));
        Assert.Equal(270, QuickSettings.NextRotation(180));
        Assert.Equal(0, QuickSettings.NextRotation(270));
    }

    [Theory]
    [InlineData(0, 1920, 1080, "no")]
    [InlineData(16.0 / 9.0, 1920, 1080, "no")]
    [InlineData(4.0 / 3.0, 1920, 1080, "1440x1080")]
    [InlineData(2.35, 1920, 1080, "1920x816")]
    [InlineData(16.0 / 9.0, 1080, 1920, "1080x608")]
    [InlineData(4.0 / 3.0, 0, 1080, "no")]
    public void CropSizeKeepsTheRequestedAspect(
        double aspect,
        int width,
        int height,
        string expected)
    {
        Assert.Equal(expected, QuickSettings.ComputeCrop(aspect, width, height));
    }

    [Fact]
    public void CropDimensionsAreEven()
    {
        string crop = QuickSettings.ComputeCrop(2.35, 1921, 1081);
        string[] parts = crop.Split('x');
        Assert.Equal(2, parts.Length);
        Assert.Equal(0, int.Parse(parts[0]) % 2);
        Assert.Equal(0, int.Parse(parts[1]) % 2);
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "hflip")]
    [InlineData(false, true, "vflip")]
    [InlineData(true, true, "hflip,vflip")]
    public void VideoFiltersCombineFlips(bool horizontal, bool vertical, string expected)
    {
        Assert.Equal(expected, QuickSettings.BuildVideoFilters(horizontal, vertical));
    }

    [Theory]
    [InlineData(true, "auto-safe")]
    [InlineData(false, "no")]
    public void HardwareDecodeMapsToMpvValues(bool enabled, string expected)
    {
        Assert.Equal(expected, QuickSettings.HwdecValue(enabled));
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public void DeinterlaceMapsToMpvValues(bool enabled, string expected)
    {
        Assert.Equal(expected, QuickSettings.DeinterlaceValue(enabled));
    }

    [Theory]
    [InlineData(null, "no")]
    [InlineData("", "no")]
    [InlineData("auto", "no")]
    [InlineData("16:9", "16:9")]
    [InlineData(" 4:3 ", "4:3")]
    public void AspectOverrideFallsBackToAuto(string? value, string expected)
    {
        Assert.Equal(expected, QuickSettings.NormalizeAspect(value));
    }

    [Theory]
    [InlineData(null, "#FFFFFFFF")]
    [InlineData("", "#FFFFFFFF")]
    [InlineData("FF0000", "#FFFF0000")]
    [InlineData("#ff0000", "#FFFF0000")]
    [InlineData("#CC00FF00", "#CC00FF00")]
    [InlineData("not-a-color", "#FFFFFFFF")]
    public void SubtitleColorsAreNormalized(string? value, string expected)
    {
        Assert.Equal(expected, QuickSettings.NormalizeColor(value));
    }

    [Fact]
    public void OptionListsExposeTheDocumentedChoices()
    {
        Assert.Equal(6, QuickSettings.AspectRatios.Count);
        Assert.Equal(6, QuickSettings.CropOptions.Count);
        Assert.Equal(4, QuickSettings.Rotations.Count);
        Assert.Equal(5, QuickSettings.SubtitleColors.Count);
        Assert.Equal("no", QuickSettings.AspectRatios[0].Value);
        Assert.Equal(0, QuickSettings.Rotations[0]);
    }
}
