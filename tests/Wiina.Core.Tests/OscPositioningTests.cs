using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public class OscPositioningTests
{
    [Theory]
    [InlineData(10, 100, 1000, 50)]
    [InlineData(990, 100, 1000, 950)]
    [InlineData(500, 100, 1000, 500)]
    public void ClampCenterKeepsElementInside(double center, double element, double container, double expected)
    {
        Assert.Equal(expected, OscPositioning.ClampCenter(center, element, container), 3);
    }

    [Fact]
    public void ClampCenterCentersOversizedElement()
    {
        Assert.Equal(200, OscPositioning.ClampCenter(10, 500, 400), 3);
    }

    [Theory]
    [InlineData(504, 1000, 500)]
    [InlineData(494, 1000, 494)]
    [InlineData(495, 1000, 500)]
    public void SnapToCenterUsesFivePixelThreshold(double center, double container, double expected)
    {
        Assert.Equal(expected, OscPositioning.SnapToCenter(center, container), 3);
    }

    [Fact]
    public void NormalizeAndDenormalizeRoundTrip()
    {
        Assert.Equal(0.5, OscPositioning.NormalizeCenter(500, 1000), 3);
        Assert.Equal(750, OscPositioning.DenormalizeCenter(0.75, 1000), 3);
    }

    [Fact]
    public void NormalizeClampsOutOfRangeFractions()
    {
        Assert.Equal(1, OscPositioning.NormalizeCenter(2000, 1000), 3);
        Assert.Equal(0, OscPositioning.NormalizeCenter(-20, 1000), 3);
        Assert.Equal(0.5, OscPositioning.NormalizeCenter(10, 0), 3);
    }
}
