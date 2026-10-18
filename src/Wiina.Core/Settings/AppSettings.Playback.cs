using Wiina.Core.Input;
using Wiina.Core.Subtitles;

namespace Wiina.Core.Settings;

/// <summary>
/// Playback preferences added after v1: smart subtitle discovery, external
/// audio sidecars and the keyboard profile selection.
/// </summary>
public sealed partial class AppSettings
{
    /// <summary>Automatically load the best matching subtitle sidecar on open.</summary>
    public bool AutoLoadSmartSubtitles { get; set; } = true;

    /// <summary>One of <see cref="SubtitleLanguagePreferences.All"/>.</summary>
    public string PreferredSubtitleLanguage { get; set; } = SubtitleLanguagePreferences.Auto;

    /// <summary>
    /// Automatically load a same-base .mka sidecar. On by default because the
    /// match is deliberately strict (only files next to the video with the same
    /// base name), and the user can switch it off in settings.
    /// </summary>
    public bool AutoLoadExternalAudio { get; set; } = true;

    /// <summary>One of <see cref="KeyBindingProfiles.All"/>.</summary>
    public string KeyBindingProfile { get; set; } = KeyBindingProfiles.Default;
}
