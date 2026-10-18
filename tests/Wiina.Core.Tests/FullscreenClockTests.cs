using Wiina.Core.Text;
using Xunit;

namespace Wiina.Core.Tests;

public class FullscreenClockTests
{
    [Theory]
    [InlineData(0, 0, 0, "00:00:00")]
    [InlineData(1, 2, 3, "01:02:03")]
    [InlineData(23, 59, 59, "23:59:59")]
    public void FormatTimeUsesTwentyFourHourClock(int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(
            expected,
            FullscreenClock.FormatTime(new TimeSpan(hours, minutes, seconds)));
    }

    [Fact]
    public void FormatTimeCanOmitSeconds()
    {
        Assert.Equal(
            "07:08",
            FullscreenClock.FormatTime(new TimeSpan(7, 8, 9), includeSeconds: false));
    }

    [Fact]
    public void FormatTimeNormalizesNegativeAndOverflowValues()
    {
        Assert.Equal(
            "00:00:00",
            FullscreenClock.FormatTime(TimeSpan.FromHours(-24)));
        Assert.Equal(
            "02:00:00",
            FullscreenClock.FormatTime(TimeSpan.FromHours(26)));
    }

    [Fact]
    public void FormatUsesLocalClockFormat()
    {
        var instant = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        string expected = instant.ToLocalTime().ToString(
            "HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, FullscreenClock.Format(instant));
    }

    [Theory]
    [InlineData(85, false, false, "85%")]
    [InlineData(85, true, false, "85% · 充电中")]
    [InlineData(85, false, true, "85% · 电源")]
    [InlineData(85, true, true, "85% · 充电中")]
    public void FormatBatteryUsesPercentAndPowerState(
        int percent,
        bool charging,
        bool ac,
        string expected)
    {
        Assert.Equal(expected, FullscreenClock.FormatBattery(percent, charging, ac));
    }

    [Fact]
    public void UnknownBatteryFallsBackToAcState()
    {
        Assert.Equal("交流电源", FullscreenClock.FormatBattery(null, false, true));
        Assert.Equal(string.Empty, FullscreenClock.FormatBattery(null, false, false));
    }

    [Fact]
    public void BatteryGlyphReflectsChargeLevel()
    {
        Assert.Equal("\uE83E", FullscreenClock.BatteryGlyph(100, charging: false));
        Assert.Equal("\uE83E", FullscreenClock.BatteryGlyph(20, charging: true));
        Assert.Equal("\uE83F", FullscreenClock.BatteryGlyph(5, charging: false));
        Assert.Equal("\uE851", FullscreenClock.BatteryGlyph(50, charging: false));
    }
}
