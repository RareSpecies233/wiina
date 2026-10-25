using Wiina.Core.Localization;
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

    [Theory]
    // Pre-generation, switch off: hover stays silent until the pass finished.
    [InlineData(ThumbnailMode.PreGenerate, false, true, false)]
    [InlineData(ThumbnailMode.PreGenerate, false, false, true)]
    // Pre-generation, switch on: partial results are allowed while generating.
    [InlineData(ThumbnailMode.PreGenerate, true, true, true)]
    [InlineData(ThumbnailMode.PreGenerate, true, false, true)]
    // Instant mode has no pass at all, so the switch never blocks it.
    [InlineData(ThumbnailMode.Instant, false, true, true)]
    [InlineData(ThumbnailMode.Instant, false, false, true)]
    public void HoverPreviewPolicyMatchesTheIinaRule(
        ThumbnailMode mode,
        bool previewWhileGenerating,
        bool preGenerationPending,
        bool expected)
    {
        Assert.Equal(
            expected,
            ThumbnailPolicy.AllowsHoverPreview(mode, previewWhileGenerating, preGenerationPending));
    }

    [Fact]
    public void PreviewWhileGeneratingRowDefaultsOff()
    {
        Assert.False(new AppSettings().ThumbnailPreviewWhileGenerating);
    }

    [Theory]
    [InlineData(ThumbnailMode.PreGenerate, false, ThumbnailMode.PreGenerate)]
    [InlineData(ThumbnailMode.Instant, false, ThumbnailMode.Instant)]
    [InlineData(ThumbnailMode.PreGenerate, true, ThumbnailMode.Instant)]
    [InlineData(ThumbnailMode.Instant, true, ThumbnailMode.Instant)]
    public void SlowModeRunsAsInstantWithoutChangingTheStoredMode(
        ThumbnailMode stored,
        bool slowMode,
        ThumbnailMode expected)
    {
        Assert.Equal(expected, ThumbnailPolicy.EffectiveMode(stored, slowMode));

        // The persisted choice is never rewritten: the catalog getter maps it
        // to the effective mode, the setter still stores what the user picked.
        SettingsRowDefinition row = SettingsCatalog.Find("video.thumbnail-mode")!;
        var settings = new AppSettings { ThumbnailMode = stored, ThumbnailSlowMode = slowMode };
        Assert.Equal((int)expected, (int)row.Value(settings)!);
        Assert.Equal(stored, settings.ThumbnailMode);
    }

    [Theory]
    [InlineData(ThumbnailMode.PreGenerate, false, true)]
    [InlineData(ThumbnailMode.Instant, false, false)]
    [InlineData(ThumbnailMode.PreGenerate, true, true)]
    [InlineData(ThumbnailMode.Instant, true, true)]
    public void DiskCacheIsSkippedOnlyByPlainInstantPreview(
        ThumbnailMode mode,
        bool slowMode,
        bool expected)
    {
        Assert.Equal(expected, ThumbnailPolicy.AllowsDiskCache(mode, slowMode));
    }

    [Theory]
    [InlineData(ThumbnailMode.PreGenerate, false, false)]
    [InlineData(ThumbnailMode.Instant, false, true)]
    [InlineData(ThumbnailMode.PreGenerate, true, true)]
    public void HoverDelayAppliesToInstantAndSlowMode(
        ThumbnailMode mode,
        bool slowMode,
        bool expected)
    {
        Assert.Equal(expected, ThumbnailPolicy.RequiresHoverDelay(mode, slowMode));
    }

    [Fact]
    public void KeyframeSeekRowDefaultsOffAndLivesOnTheVideoPage()
    {
        var settings = new AppSettings();
        Assert.False(settings.ThumbnailKeyframeSeek);

        SettingsRowDefinition? row = SettingsCatalog.Find("video.thumbnail-keyframe-seek");
        Assert.NotNull(row);
        Assert.Equal(SettingsPageId.Video, row!.Page);
        Assert.Equal(SettingsRowKind.Toggle, row.Kind);
        Assert.False((bool)row.Value(settings)!);

        row.Apply(settings, true);
        Assert.True(settings.ThumbnailKeyframeSeek);
    }

    [Fact]
    public void PreviewWhileGeneratingRowIsUnavailableWithoutAWholeFilePass()
    {
        SettingsRowDefinition row = SettingsCatalog.Find("video.thumbnail-preview-while-generating")!;

        var preGenerate = new AppSettings { ThumbnailMode = ThumbnailMode.PreGenerate, ThumbnailSlowMode = false };
        Assert.True(row.IsAvailableFor(preGenerate));
        Assert.Null(row.UnavailableReasonFor(preGenerate));

        var slow = new AppSettings { ThumbnailMode = ThumbnailMode.PreGenerate, ThumbnailSlowMode = true };
        Assert.False(row.IsAvailableFor(slow));
        Assert.Equal(
            "Slow mode and instant preview have no whole-file pass, so this option does not apply.",
            SettingsLocalization.UnavailableReason(row.UnavailableReasonFor(slow)!, "en-US"));

        var instant = new AppSettings { ThumbnailMode = ThumbnailMode.Instant, ThumbnailSlowMode = false };
        Assert.False(row.IsAvailableFor(instant));

        // Slow mode never rewrites the stored mode, so turning it off restores
        // the row without any extra state.
        slow.ThumbnailSlowMode = false;
        Assert.True(row.IsAvailableFor(slow));
        Assert.Equal(ThumbnailMode.PreGenerate, slow.ThumbnailMode);
    }
}
