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
/// A named mpv scaler profile; built-ins and user profiles share this shape.
/// <see cref="Deband"/> is kept only so exported profile JSON keeps round-tripping;
/// debanding itself is a global <see cref="AppSettings.DebandEnabled"/> toggle.
/// </summary>
public sealed class VideoQualityProfile
{
    public string Name { get; set; } = "自定义";
    public string Scale { get; set; } = "bilinear";
    public string Dscale { get; set; } = "bilinear";
    public string Cscale { get; set; } = "bilinear";
    public bool Deband { get; set; }
}
