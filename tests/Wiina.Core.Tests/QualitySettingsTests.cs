using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// 1.2.4: the quality preset manager keeps two built-ins and deband becomes an
/// independent Video-page toggle (<see cref="AppSettings.DebandEnabled"/>).
/// </summary>
public sealed class QualitySettingsTests
{
    [Fact]
    public void DebandIsOffByDefault()
    {
        Assert.False(new AppSettings().DebandEnabled);
    }

    [Fact]
    public void VideoQualityRowExposesOnlyTheTwoBuiltIns()
    {
        SettingsRowDefinition row = SettingsCatalog.Find("video.quality")!;
        Assert.Equal(SettingsRowKind.Choice, row.Kind);
        Assert.Equal(SettingsPageId.Video, row.Page);
        Assert.Equal(new[] { "默认", "性能优先" }, row.Choices);
    }

    [Fact]
    public void VideoQualityRowMapsPerformanceAndDefault()
    {
        SettingsRowDefinition row = SettingsCatalog.Find("video.quality")!;
        var settings = new AppSettings();

        Assert.Equal(0, Convert.ToInt32(row.Value(settings)));

        row.Apply(settings, 1);
        Assert.Equal(VideoQualityPreset.Performance, settings.VideoQualityPreset);
        Assert.Equal("性能优先", settings.ActiveQualityProfile);
        Assert.Equal(1, Convert.ToInt32(row.Value(settings)));

        row.Apply(settings, 0);
        Assert.Equal(VideoQualityPreset.Auto, settings.VideoQualityPreset);
        Assert.Equal("默认", settings.ActiveQualityProfile);
    }

    [Fact]
    public void LegacyPresetValuesRenderAsTheDefaultProfile()
    {
        SettingsRowDefinition row = SettingsCatalog.Find("video.quality")!;

        // Old settings.json may still carry Balanced/HighQuality; those values
        // stay in the enum for deserialization but behave as the default.
        Assert.Equal(0, Convert.ToInt32(row.Value(
            new AppSettings { VideoQualityPreset = VideoQualityPreset.Balanced })));
        Assert.Equal(0, Convert.ToInt32(row.Value(
            new AppSettings { VideoQualityPreset = VideoQualityPreset.HighQuality })));
    }

    [Fact]
    public void DebandRowIsAToggleOnTheVideoPage()
    {
        SettingsRowDefinition row = SettingsCatalog.Find("video.deband")!;
        Assert.Equal(SettingsRowKind.Toggle, row.Kind);
        Assert.Equal(SettingsPageId.Video, row.Page);
        Assert.Equal("画面", row.Section);

        var settings = new AppSettings();
        Assert.False((bool)row.Value(settings)!);

        row.Apply(settings, true);
        Assert.True(settings.DebandEnabled);
        Assert.True((bool)row.Value(settings)!);
    }

    [Fact]
    public void QualityRowsAllLiveOnTheVideoPage()
    {
        foreach (string id in new[] { "video.quality", "video.quality-editor", "video.deband" })
        {
            Assert.Equal(SettingsPageId.Video, SettingsCatalog.Find(id)!.Page);
        }
    }
}
