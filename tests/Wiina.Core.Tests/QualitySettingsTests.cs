using System.Text.Json;
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

    // ── 1.2.5: 23-option profiles, mpv defaults, JSON compatibility ─────────

    [Fact]
    public void DefaultProfileMatchesMpv041Defaults()
    {
        // Values verified with option-info/<name>/default-value on the bundled
        // mpv 0.41 (v0.41.0-1205-g01e2533f3); IINA does not override them.
        VideoQualityProfile profile = VideoQualityProfiles.CreateDefault();

        Assert.Equal("默认", profile.Name);
        Assert.Equal("lanczos", profile.Scale);
        Assert.Equal("hermite", profile.Dscale);
        Assert.Equal("bilinear", profile.Cscale);
        Assert.Equal(0, profile.ScaleAntiring);
        Assert.Equal(0, profile.DscaleAntiring);
        Assert.Equal(0, profile.CscaleAntiring);
        Assert.True(profile.CorrectDownscaling);
        Assert.True(profile.LinearDownscaling);
        Assert.True(profile.SigmoidUpscaling);
        Assert.Equal("fruit", profile.Dither);
        Assert.Equal("auto", profile.DitherDepth);
        Assert.Equal(6, profile.DitherSizeFruit);
        Assert.False(profile.TemporalDither);
        Assert.False(profile.Deband);
        Assert.Equal(1, profile.DebandIterations);
        Assert.Equal(48, profile.DebandThreshold);
        Assert.Equal(16, profile.DebandRange);
        Assert.Equal(32, profile.DebandGrain);
        Assert.False(profile.Interpolation);
        Assert.Equal("oversample", profile.Tscale);
        Assert.Equal("audio", profile.VideoSync);
        Assert.Equal(1.0, profile.VideoSyncMaxVideoChange);
        Assert.True(profile.ScalerResizesOnly);
        Assert.Equal("auto", profile.GpuDumbMode);
    }

    [Fact]
    public void PerformanceProfileIsTheCheapestCoherentSet()
    {
        VideoQualityProfile profile = VideoQualityProfiles.CreatePerformance();

        Assert.Equal("性能优先", profile.Name);
        Assert.Equal("bilinear", profile.Scale);
        Assert.Equal("bilinear", profile.Dscale);
        Assert.Equal("bilinear", profile.Cscale);
        Assert.False(profile.CorrectDownscaling);
        Assert.False(profile.LinearDownscaling);
        Assert.False(profile.SigmoidUpscaling);
        Assert.Equal("no", profile.Dither);
        Assert.False(profile.TemporalDither);
        Assert.False(profile.Deband);
        Assert.False(profile.Interpolation);

        string[] keys = VideoQualityProfiles.ToMpvOptions(profile, debandEnabled: false)
            .Select(option => option.Key)
            .ToArray();
        Assert.True(Array.IndexOf(keys, "scale") < Array.IndexOf(keys, "dscale"));
        Assert.True(Array.IndexOf(keys, "dscale") < Array.IndexOf(keys, "cscale"));
        Assert.Contains("interpolation", keys);
        Assert.DoesNotContain("deband-motion", keys);
    }

    [Fact]
    public void DebandParametersOnlyApplyWhileTheGlobalToggleIsOn()
    {
        VideoQualityProfile profile = VideoQualityProfiles.CreateDefault();
        profile.DebandIterations = 3;
        profile.DebandThreshold = 64;

        Dictionary<string, string> off = VideoQualityProfiles.ToMpvOptions(profile, debandEnabled: false)
            .ToDictionary(option => option.Key, option => option.Value);
        Assert.Equal("no", off["deband"]);
        Assert.False(off.ContainsKey("deband-iterations"));
        Assert.False(off.ContainsKey("deband-threshold"));
        Assert.False(off.ContainsKey("deband-range"));
        Assert.False(off.ContainsKey("deband-grain"));

        Dictionary<string, string> on = VideoQualityProfiles.ToMpvOptions(profile, debandEnabled: true)
            .ToDictionary(option => option.Key, option => option.Value);
        Assert.Equal("yes", on["deband"]);
        Assert.Equal("3", on["deband-iterations"]);
        Assert.Equal("64", on["deband-threshold"]);
        Assert.Equal("16", on["deband-range"]);
        Assert.Equal("32", on["deband-grain"]);
    }

    [Fact]
    public void LegacyExportedProfileFillsMissingOptionsFromMpvDefaults()
    {
        // A 1.2.4 export only had the four scaler/flag fields.
        const string json = """
            [{"Name":"旧预设","Scale":"bicubic","Dscale":"bilinear","Cscale":"bilinear","Deband":true}]
            """;

        List<VideoQualityProfile>? loaded = JsonSerializer.Deserialize<List<VideoQualityProfile>>(json);
        VideoQualityProfile profile = Assert.Single(loaded!);

        Assert.Equal("旧预设", profile.Name);
        Assert.Equal("bicubic", profile.Scale);
        Assert.True(profile.Deband);
        // Missing fields keep the mpv defaults, so the profile still applies.
        Assert.Equal("fruit", profile.Dither);
        Assert.Equal("oversample", profile.Tscale);
        Assert.Equal("audio", profile.VideoSync);
        Assert.True(profile.ScalerResizesOnly);
        Assert.Null(profile.ExtraOptions);
    }

    [Fact]
    public void UnknownImportedOptionsArePreservedAcrossExportAndNeverApplied()
    {
        const string json = """
            {"Name":"未来预设","Scale":"xbr","Dither":"fruit","FutureOption":42,"Another":{"a":1}}
            """;

        VideoQualityProfile? profile = JsonSerializer.Deserialize<VideoQualityProfile>(json);
        Assert.NotNull(profile);
        Assert.Equal("xbr", profile!.Scale);
        Assert.NotNull(profile.ExtraOptions);
        Assert.Equal(42, profile.ExtraOptions!["FutureOption"].GetInt32());
        Assert.Equal(1, profile.ExtraOptions["Another"].GetProperty("a").GetInt32());

        // Never sent to mpv: an extension option could bypass a master gate.
        Assert.DoesNotContain(
            VideoQualityProfiles.ToMpvOptions(profile, debandEnabled: true),
            option => option.Key == "FutureOption");

        // Saving (export) writes the extension data back verbatim.
        string saved = JsonSerializer.Serialize(profile);
        VideoQualityProfile roundTripped = JsonSerializer.Deserialize<VideoQualityProfile>(saved)!;
        Assert.Equal(42, roundTripped.ExtraOptions!["FutureOption"].GetInt32());
        Assert.Equal("xbr", roundTripped.Scale);
    }

    [Theory]
    [InlineData("自动（IINA 默认）", "默认")]
    [InlineData("自动", "默认")]
    [InlineData("IINA 默认", "默认")]
    [InlineData("IINA默认", "默认")]
    [InlineData("Auto", "默认")]
    [InlineData("Auto (IINA default)", "默认")]
    [InlineData("IINA (default)", "默认")]
    [InlineData("  我的预设  ", "我的预设")]
    [InlineData("", "")]
    public void NormalizeNameMapsLegacyAutomaticAliases(string input, string expected)
    {
        Assert.Equal(expected, VideoQualityProfiles.NormalizeName(input));
    }

    [Fact]
    public void BlankImportedScalerFallsBackToTheMpvDefault()
    {
        var profile = new VideoQualityProfile { Scale = "  ", Dscale = "", Cscale = null! };
        Dictionary<string, string> options = VideoQualityProfiles.ToMpvOptions(profile, debandEnabled: false)
            .ToDictionary(option => option.Key, option => option.Value);

        Assert.Equal("lanczos", options["scale"]);
        Assert.Equal("hermite", options["dscale"]);
        Assert.Equal("bilinear", options["cscale"]);
    }
}
