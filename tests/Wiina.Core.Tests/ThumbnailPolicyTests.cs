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
    public void SlowModeCooldownIsUsable()
    {
        Assert.InRange(ThumbnailPolicy.SlowModeCooldownMilliseconds, 50, 2_000);
    }
}
