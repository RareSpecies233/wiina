using Wiina.Core.Text;
using Xunit;

namespace Wiina.Core.Tests;

public class TimeFormatterTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(59.9, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    [InlineData(7325, "2:02:05")]
    public void FormatsAsMinutesOrHours(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormatter.Format(seconds));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidInputsFallBackToZero(double seconds)
    {
        Assert.Equal("0:00", TimeFormatter.Format(seconds));
    }
}
