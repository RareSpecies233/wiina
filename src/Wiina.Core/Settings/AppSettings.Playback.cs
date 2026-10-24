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

    private string _keyBindingProfile = KeyBindingProfiles.Default;

    /// <summary>One of <see cref="KeyBindingProfiles.All"/>.</summary>
    public string KeyBindingProfile
    {
        get => _keyBindingProfile;
        // Older builds could persist the removed IINA profile. Normalizing in
        // the setter migrates it on load and guarantees it never reaches the UI.
        set => _keyBindingProfile = KeyBindingProfiles.Normalize(value);
    }

    /// <summary>
    /// How opening one media file fills the playlist. Defaults to the files
    /// beside the opened video; manual keeps only the picked file.
    /// </summary>
    public AutoPlaylistMode AutoPlaylistMode { get; set; } = AutoPlaylistMode.SameFolder;

    /// <summary>
    /// mpv scaling preset; <see cref="VideoQualityPreset.Auto"/> is the
    /// "默认" built-in (IINA/libmpv defaults).
    /// </summary>
    public VideoQualityPreset VideoQualityPreset { get; set; } = VideoQualityPreset.Auto;

    /// <summary>
    /// Selected named quality profile. Built-ins are "默认" and "性能优先";
    /// anything else is looked up in <see cref="UserQualityProfiles"/>.
    /// </summary>
    public string ActiveQualityProfile { get; set; } = "默认";

    /// <summary>User-created quality profiles imported or edited in the quality editor.</summary>
    public List<VideoQualityProfile> UserQualityProfiles { get; set; } = new();
}
