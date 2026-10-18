using Wiina.Core.Text;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class InputValidationTests
{
    [Theory]
    [InlineData("https://example.com/video.mp4", "https://example.com/video.mp4")]
    [InlineData("http://example.com/a.m3u8", "http://example.com/a.m3u8")]
    [InlineData("example.com/watch?v=1", "https://example.com/watch?v=1")]
    [InlineData("ftp://example.com/file.mkv", "ftp://example.com/file.mkv")]
    [InlineData("rtsp://camera.local/stream", "rtsp://camera.local/stream")]
    public void ValidUrlsAreNormalized(string input, string expected)
    {
        Assert.True(UrlValidation.TryNormalize(input, out string normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hello world")]
    [InlineData("http://")]
    [InlineData("https://example.com/a b")]
    [InlineData("C:\\videos\\movie.mp4")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("gopher://example.com/file")]
    public void InvalidUrlsAreRejected(string? input)
    {
        Assert.False(UrlValidation.TryNormalize(input, out string normalized));
        Assert.Empty(normalized);
    }

    [Fact]
    public void OverlongUrlsAreRejected()
    {
        string url = "https://example.com/" + new string('a', UrlValidation.MaxLength);
        Assert.False(UrlValidation.TryNormalize(url, out _));
    }

    [Fact]
    public void StreamDetectionDistinguishesFiles()
    {
        Assert.True(UrlValidation.IsStreamUrl("https://example.com/a.m3u8"));
        Assert.False(UrlValidation.IsStreamUrl("not a url"));
    }

    [Theory]
    [InlineData("  Segoe   UI  ", "Segoe UI")]
    [InlineData("Microsoft YaHei", "Microsoft YaHei")]
    [InlineData("A\u0001B", "AB")]
    [InlineData("", "Segoe UI")]
    [InlineData(null, "Segoe UI")]
    public void FontNamesAreNormalized(string? input, string expected)
    {
        Assert.Equal(expected, FontNameValidation.Normalize(input));
    }

    [Fact]
    public void FontValidationRejectsBlankAndOverlongNames()
    {
        Assert.False(FontNameValidation.TryNormalize("  ", out string blank));
        Assert.Equal("Segoe UI", blank);
        Assert.False(FontNameValidation.TryNormalize(
            new string('a', FontNameValidation.MaxLength + 1),
            out _));
        Assert.True(FontNameValidation.TryNormalize("Source Han Sans SC", out string valid));
        Assert.Equal("Source Han Sans SC", valid);
    }
}
