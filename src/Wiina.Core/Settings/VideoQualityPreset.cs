using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wiina.Core.Settings;

/// <summary>
/// The two mpv scaling presets exposed in Settings. Only <see cref="Auto"/> and
/// <see cref="Performance"/> are offered since 1.2.4. <see cref="Balanced"/> and
/// <see cref="HighQuality"/> are kept purely so settings.json files written by
/// older builds (which serialize enum names) still deserialize; both are shown
/// and applied as <see cref="Auto"/>.
/// </summary>
public enum VideoQualityPreset
{
    Auto = 0,

    /// <summary>Legacy 1.2.3 preset; now treated as <see cref="Auto"/>.</summary>
    Balanced = 1,

    /// <summary>Legacy 1.2.3 preset; now treated as <see cref="Auto"/>.</summary>
    HighQuality = 2,

    Performance = 3,
}

/// <summary>
/// A named collection of mpv video-quality options. Every value is stored exactly
/// as mpv expects it on the command line / in an option property, so a profile can
/// be applied with <c>mpv_set_property_string</c> without translation. Defaults are
/// the mpv 0.41 defaults (IINA does not override them), so an empty profile behaves
/// like an untouched mpv/IINA session.
/// <para>
/// Unknown option names from an imported JSON file are preserved in
/// <see cref="ExtraOptions"/> and never discarded; they are shown in the editor as
/// "未识别（保留原值）" but are deliberately not applied, so they cannot bypass a
/// runtime gate such as <see cref="AppSettings.DebandEnabled"/>.
/// </para>
/// <para>
/// <see cref="Deband"/> is kept only so exported profile JSON keeps round-tripping;
/// debanding itself is a global <see cref="AppSettings.DebandEnabled"/> toggle.
/// </para>
/// </summary>
public sealed class VideoQualityProfile
{
    public string Name { get; set; } = "自定义";

    // ── 缩放（放大 / 缩小）───────────────────────────────────────────────
    public string Scale { get; set; } = VideoQualityProfiles.DefaultScale;
    public string Dscale { get; set; } = VideoQualityProfiles.DefaultDscale;
    public double ScaleAntiring { get; set; }
    public double DscaleAntiring { get; set; }
    public bool CorrectDownscaling { get; set; } = true;
    public bool LinearDownscaling { get; set; } = true;
    public bool SigmoidUpscaling { get; set; } = true;

    // ── 色度 ────────────────────────────────────────────────────────────
    public string Cscale { get; set; } = VideoQualityProfiles.DefaultCscale;
    public double CscaleAntiring { get; set; }

    // ── 抖动（dither）───────────────────────────────────────────────────
    public string Dither { get; set; } = VideoQualityProfiles.DefaultDither;
    public string DitherDepth { get; set; } = VideoQualityProfiles.DefaultDitherDepth;
    public int DitherSizeFruit { get; set; } = 6;
    public bool TemporalDither { get; set; }

    // ── 去色带（参数始终保存，是否生效由全局开关决定）──────────────────
    public bool Deband { get; set; }
    public int DebandIterations { get; set; } = 1;
    public double DebandThreshold { get; set; } = 48;
    public double DebandRange { get; set; } = 16;
    public double DebandGrain { get; set; } = 32;

    // ── 插帧 ────────────────────────────────────────────────────────────
    public bool Interpolation { get; set; }
    public string Tscale { get; set; } = VideoQualityProfiles.DefaultTscale;

    // ── 同步 ────────────────────────────────────────────────────────────
    public string VideoSync { get; set; } = VideoQualityProfiles.DefaultVideoSync;
    public double VideoSyncMaxVideoChange { get; set; } = 1.0;

    // ── 其他 ────────────────────────────────────────────────────────────
    public bool ScalerResizesOnly { get; set; } = true;
    public string GpuDumbMode { get; set; } = VideoQualityProfiles.DefaultGpuDumbMode;

    /// <summary>
    /// Option names from an imported JSON file that this build does not know.
    /// Kept verbatim so a save → export round-trip never loses them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraOptions { get; set; }
}

/// <summary>
/// Creates the built-in profiles and turns a <see cref="VideoQualityProfile"/> into
/// the ordered list of raw mpv options that should be applied.
/// </summary>
public static class VideoQualityProfiles
{
    // mpv 0.41 defaults (verified via option-info/&lt;name&gt;/default-value on the
    // bundled libmpv-2.dll). IINA queries the same table at runtime and does not
    // override these values, so this is also the IINA-equivalent default.
    public const string DefaultScale = "lanczos";
    public const string DefaultDscale = "hermite";

    // mpv's option-info reports cscale as unset/empty; "bilinear" is the documented
    // mpv default and is what we store so a profile always carries a usable raw key.
    public const string DefaultCscale = "bilinear";
    public const string DefaultTscale = "oversample";
    public const string DefaultDither = "fruit";
    public const string DefaultDitherDepth = "auto";
    public const string DefaultVideoSync = "audio";
    public const string DefaultGpuDumbMode = "auto";

    /// <summary>The "默认" profile: exactly mpv's own defaults (IINA-equivalent).</summary>
    public static VideoQualityProfile CreateDefault(string name = "默认") => new()
    {
        Name = name,
        Scale = DefaultScale,
        Dscale = DefaultDscale,
        Cscale = DefaultCscale,
        ScaleAntiring = 0,
        DscaleAntiring = 0,
        CorrectDownscaling = true,
        LinearDownscaling = true,
        SigmoidUpscaling = true,
        CscaleAntiring = 0,
        Dither = DefaultDither,
        DitherDepth = DefaultDitherDepth,
        DitherSizeFruit = 6,
        TemporalDither = false,
        Deband = false,
        DebandIterations = 1,
        DebandThreshold = 48,
        DebandRange = 16,
        DebandGrain = 32,
        Interpolation = false,
        Tscale = DefaultTscale,
        VideoSync = DefaultVideoSync,
        VideoSyncMaxVideoChange = 1.0,
        ScalerResizesOnly = true,
        GpuDumbMode = DefaultGpuDumbMode,
    };

    /// <summary>
    /// The "性能优先" profile: the cheapest coherent set — bilinear on all three
    /// planes, extra scaling passes and deband/interpolation disabled, cheapest dither.
    /// </summary>
    public static VideoQualityProfile CreatePerformance(string name = "性能优先")
    {
        VideoQualityProfile profile = CreateDefault(name);
        profile.Scale = "bilinear";
        profile.Dscale = "bilinear";
        profile.Cscale = "bilinear";
        profile.ScaleAntiring = 0;
        profile.DscaleAntiring = 0;
        profile.CscaleAntiring = 0;
        profile.CorrectDownscaling = false;
        profile.LinearDownscaling = false;
        profile.SigmoidUpscaling = false;
        profile.Dither = "no";
        profile.DitherDepth = "auto";
        profile.TemporalDither = false;
        profile.Deband = false;
        profile.Interpolation = false;
        profile.ScalerResizesOnly = true;
        profile.GpuDumbMode = DefaultGpuDumbMode;
        return profile;
    }

    /// <summary>
    /// Maps the names the 1.2.3 build used for its "automatic" preset onto the
    /// current "默认" name, so old exported JSON keeps working after import.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return trimmed is "自动（IINA 默认）" or "自动" or "IINA 默认" or "IINA默认"
            or "Auto" or "Auto (IINA default)" or "IINA (default)"
            ? "默认"
            : trimmed;
    }

    /// <summary>
    /// The exact mpv option/value pairs a profile stands for, in a safe apply order.
    /// Deband parameters are only emitted while the global toggle is on; when it is
    /// off the profile's deband settings stay saved but never reach mpv.
    /// <see cref="VideoQualityProfile.ExtraOptions"/> is intentionally not applied.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> ToMpvOptions(
        VideoQualityProfile profile,
        bool debandEnabled)
    {
        var options = new List<(string, string)>(24)
        {
            ("scale", Text(profile.Scale, DefaultScale)),
            ("dscale", Text(profile.Dscale, DefaultDscale)),
            ("cscale", Text(profile.Cscale, DefaultCscale)),
            ("scale-antiring", Number(profile.ScaleAntiring)),
            ("dscale-antiring", Number(profile.DscaleAntiring)),
            ("cscale-antiring", Number(profile.CscaleAntiring)),
            ("correct-downscaling", Flag(profile.CorrectDownscaling)),
            ("linear-downscaling", Flag(profile.LinearDownscaling)),
            ("sigmoid-upscaling", Flag(profile.SigmoidUpscaling)),
            ("scaler-resizes-only", Flag(profile.ScalerResizesOnly)),
            ("dither", Text(profile.Dither, DefaultDither)),
            ("dither-depth", Text(profile.DitherDepth, DefaultDitherDepth)),
            ("dither-size-fruit", Number(profile.DitherSizeFruit)),
            ("temporal-dither", Flag(profile.TemporalDither)),
            ("deband", Flag(debandEnabled)),
        };

        if (debandEnabled)
        {
            options.Add(("deband-iterations", Number(profile.DebandIterations)));
            options.Add(("deband-threshold", Number(profile.DebandThreshold)));
            options.Add(("deband-range", Number(profile.DebandRange)));
            options.Add(("deband-grain", Number(profile.DebandGrain)));
        }

        options.Add(("interpolation", Flag(profile.Interpolation)));
        options.Add(("tscale", Text(profile.Tscale, DefaultTscale)));
        options.Add(("video-sync", Text(profile.VideoSync, DefaultVideoSync)));
        options.Add(("video-sync-max-video-change", Number(profile.VideoSyncMaxVideoChange)));
        options.Add(("gpu-dumb-mode", Text(profile.GpuDumbMode, DefaultGpuDumbMode)));
        return options;
    }

    private static string Text(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Flag(bool value) => value ? "yes" : "no";

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
