namespace Wiina.Core.Settings;

/// <summary>One of the mpv scaler/deband profiles exposed in Settings.</summary>
public enum VideoQualityPreset
{
    Auto = 0,
    Balanced = 1,
    HighQuality = 2,
    Performance = 3,
}

/// <summary>A named mpv scaler/deband profile; built-ins and user profiles share this shape.</summary>
public sealed class VideoQualityProfile
{
    public string Name { get; set; } = "自定义";
    public string Scale { get; set; } = "bilinear";
    public string Dscale { get; set; } = "bilinear";
    public string Cscale { get; set; } = "bilinear";
    public bool Deband { get; set; }
}
