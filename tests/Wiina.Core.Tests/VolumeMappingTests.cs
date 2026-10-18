using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class VolumeMappingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(42, 42)]
    [InlineData(100, 100)]
    [InlineData(130, 100)]
    [InlineData(200, 100)]
    [InlineData(-5, 0)]
    public void ToUiNeverExceedsOneHundred(double actual, double expected)
    {
        Assert.Equal(expected, VolumeMapping.ToUi(actual));
    }

    [Theory]
    [InlineData(130, 100, 130)]
    [InlineData(130, 101, 130)]
    [InlineData(130, 99, 99)]
    [InlineData(130, 0, 0)]
    [InlineData(100, 100, 100)]
    [InlineData(80, 100, 100)]
    [InlineData(80, 120, 100)]
    [InlineData(-10, 50, 50)]
    public void SliderValueKeepsAmplificationOnlyWhileAtOrAboveOneHundred(
        double actual,
        double requested,
        double expected)
    {
        Assert.Equal(expected, VolumeMapping.ApplySliderValue(actual, requested));
    }

    [Theory]
    [InlineData(95, 5, 100)]
    [InlineData(100, 5, 100)]
    [InlineData(130, 5, 130)]
    [InlineData(130, -5, 125)]
    [InlineData(101, -5, 100)]
    [InlineData(100, -5, 95)]
    [InlineData(3, -5, 0)]
    [InlineData(42, 0, 42)]
    public void WheelDeltaStopsGoingUpAtOneHundred(
        double actual,
        double delta,
        double expected)
    {
        Assert.Equal(expected, VolumeMapping.ApplyWheelDelta(actual, delta));
    }

    [Theory]
    [InlineData(130, 100, 100)]
    [InlineData(180, 200, 180)]
    [InlineData(250, 200, 200)]
    [InlineData(-1, 150, 0)]
    [InlineData(120, 50, 100)]
    [InlineData(120, 150, 120)]
    public void ClampActualUsesTheConfiguredLimit(
        double value,
        double maximum,
        double expected)
    {
        Assert.Equal(expected, VolumeMapping.ClampActual(value, maximum));
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(0.4, "0%")]
    [InlineData(0.6, "1%")]
    [InlineData(99.5, "100%")]
    [InlineData(100, "100%")]
    [InlineData(130, "100%")]
    public void FormatUiRoundsHalfAwayFromZero(double actual, string expected)
    {
        Assert.Equal(expected, VolumeMapping.FormatUi(actual));
    }
}
