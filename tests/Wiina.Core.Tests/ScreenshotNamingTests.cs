using Wiina.Core.Files;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ScreenshotNamingTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(8));

    [Fact]
    public void NamesCaptureAfterVideoAndPosition()
    {
        string name = ScreenshotNaming.BuildFileName(
            @"D:\videos\xxx.mkv",
            (1 * 3600) + (28 * 60) + 39.980,
            Stamp);

        Assert.Equal("xxx-1-28-39-980.png", name);
    }

    [Theory]
    [InlineData(0, "xxx-0-00-00-000.png")]
    [InlineData(5.05, "xxx-0-00-05-050.png")]
    [InlineData(59.9996, "xxx-0-01-00-000.png")]
    [InlineData(3600, "xxx-1-00-00-000.png")]
    [InlineData(36000.123, "xxx-10-00-00-123.png")]
    public void PositionIsRenderedAsHoursMinutesSecondsMilliseconds(
        double seconds,
        string expected)
    {
        Assert.Equal(
            expected,
            ScreenshotNaming.BuildFileName(@"C:\media\xxx.mkv", seconds, Stamp));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-12.5)]
    public void InvalidPositionFallsBackToZero(double seconds)
    {
        Assert.Equal(
            "xxx-0-00-00-000.png",
            ScreenshotNaming.BuildFileName(@"C:\media\xxx.mkv", seconds, Stamp));
    }

    [Fact]
    public void PathCharactersAreSanitised()
    {
        Assert.Equal(
            "a-b-c-d-e-f-g-h-i-",
            ScreenshotNaming.Sanitise("a<b>c:d\"e/f\\g|h?i*"));
    }

    [Fact]
    public void ExtensionIsDroppedFromTheMediaName()
    {
        Assert.Equal("movie", ScreenshotNaming.MediaStem(@"D:\videos\movie.mkv"));
        Assert.Equal("movie", ScreenshotNaming.MediaStem(@"D:\videos\movie"));
    }

    [Fact]
    public void ControlCharactersAreReplaced()
    {
        Assert.Equal("a-b", ScreenshotNaming.Sanitise("a\u0001b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\media\   ")]
    public void FallsBackToLegacyNameWithoutMedia(string? mediaPath)
    {
        Assert.Equal(
            "wiina-20261025-013000.png",
            ScreenshotNaming.BuildFileName(mediaPath, 42, Stamp));
    }

    [Fact]
    public void LongMediaNamesAreTruncated()
    {
        string stem = new('x', 250);
        string media = @"C:\media\" + stem + ".mkv";

        Assert.Equal(
            new string('x', ScreenshotNaming.MaxMediaNameLength),
            ScreenshotNaming.MediaStem(media));
        Assert.Equal(
            new string('x', ScreenshotNaming.MaxMediaNameLength),
            ScreenshotNaming.BuildFileName(media, 1, Stamp)
                .Replace("-0-00-01-000.png", string.Empty));
    }

    [Fact]
    public void CollisionsGetNumericSuffix()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xxx-0-00-10-000.png",
            "xxx-0-00-10-000-2.png",
        };

        string name = ScreenshotNaming.BuildUniqueFileName(
            @"C:\media\xxx.mkv",
            10,
            Stamp,
            candidate => taken.Contains(candidate));

        Assert.Equal("xxx-0-00-10-000-3.png", name);
    }

    [Fact]
    public void FreeNameIsReturnedUnchanged()
    {
        Assert.Equal(
            "xxx-0-00-10-000.png",
            ScreenshotNaming.BuildUniqueFileName(
                @"C:\media\xxx.mkv",
                10,
                Stamp,
                _ => false));
    }
}
