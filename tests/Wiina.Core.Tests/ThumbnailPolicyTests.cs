using Wiina.Core.Settings;
using Wiina.Core.State;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ThumbnailPolicyTests
{
    [Fact]
    public void PreGenerationRunsByDefault()
    {
        Assert.True(ThumbnailPolicy.AllowsPreGeneration(
            thumbnailPreviewEnabled: true,
            slowMode: false,
            diskCacheMegabytes: 512));
    }

    [Fact]
    public void SlowModeDisablesPreGeneration()
    {
        Assert.False(ThumbnailPolicy.AllowsPreGeneration(
            thumbnailPreviewEnabled: true,
            slowMode: true,
            diskCacheMegabytes: 512));
    }

    [Theory]
    [InlineData(false, 512)]
    [InlineData(true, 0)]
    public void PreviewOrDiskCacheOffDisablesPreGeneration(
        bool previewEnabled,
        int diskCacheMegabytes)
    {
        Assert.False(ThumbnailPolicy.AllowsPreGeneration(
            previewEnabled,
            slowMode: false,
            diskCacheMegabytes));
    }

    [Fact]
    public void InstantModeDisablesPreGenerationEvenWhenEverythingElseIsOn()
    {
        Assert.False(ThumbnailPolicy.AllowsPreGeneration(
            thumbnailPreviewEnabled: true,
            slowMode: false,
            diskCacheMegabytes: 512,
            ThumbnailMode.Instant));

        Assert.True(ThumbnailPolicy.AllowsPreGeneration(
            thumbnailPreviewEnabled: true,
            slowMode: false,
            diskCacheMegabytes: 512,
            ThumbnailMode.PreGenerate));
    }

    [Fact]
    public void InstantModeSkipsTheDiskCache()
    {
        Assert.False(ThumbnailPolicy.AllowsDiskCache(ThumbnailMode.Instant));
        Assert.True(ThumbnailPolicy.AllowsDiskCache(ThumbnailMode.PreGenerate));
    }

    [Fact]
    public void OnlyInstantModeWaitsForTheHoverDelay()
    {
        Assert.True(ThumbnailPolicy.RequiresHoverDelay(ThumbnailMode.Instant));
        Assert.False(ThumbnailPolicy.RequiresHoverDelay(ThumbnailMode.PreGenerate));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(250, 250)]
    [InlineData(2_000, 2_000)]
    [InlineData(9_999, 2_000)]
    public void HoverDelayIsClampedToATwoSecondCeiling(int raw, int expected)
    {
        Assert.Equal(expected, ThumbnailPolicy.ClampHoverDelayMilliseconds(raw));
    }

    [Fact]
    public void DefaultHoverDelayMatchesTheSettingDefault()
    {
        Assert.InRange(ThumbnailPolicy.DefaultHoverDelayMilliseconds, 0, ThumbnailPolicy.MaxHoverDelayMilliseconds);
        Assert.Equal(250, ThumbnailPolicy.DefaultHoverDelayMilliseconds);
        Assert.Equal(250, new AppSettings().ThumbnailHoverDelayMs);
        Assert.Equal(ThumbnailMode.PreGenerate, new AppSettings().ThumbnailMode);
    }

    [Fact]
    public void SlowModeCooldownIsUsable()
    {
        Assert.InRange(ThumbnailPolicy.SlowModeCooldownMilliseconds, 50, 2_000);
    }
}
