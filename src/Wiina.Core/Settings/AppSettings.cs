using Wiina.Core.Filters;
using Wiina.Core.Input;
using Wiina.Core.Models;

namespace Wiina.Core.Settings;

/// <summary>
/// User settings persisted to %LOCALAPPDATA%\Wiina\settings.json.
/// Stage 1 uses only the window/OSC/theme subset; later stages add more.
/// </summary>
public sealed partial class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public WindowLayoutMode Layout { get; set; } = WindowLayoutMode.EdgeToEdge;

    public OscPosition OscPosition { get; set; } = OscPosition.Floating;

    public SidebarSide SidebarSide { get; set; } = SidebarSide.Trailing;

    public double SidebarWidth { get; set; } = 360;

    /// <summary>Last sidebar page shown by a tab/menu action.</summary>
    public SidebarType SidebarType { get; set; } = SidebarType.Settings;

    /// <summary>Last quick-settings tab (layout/video/audio/subtitles).</summary>
    public QuickSettingsPage QuickSettingsPage { get; set; } = QuickSettingsPage.Video;

    /// <summary>App-level animation switch (quick settings → layout).</summary>
    public bool AnimationsEnabled { get; set; } = true;

    public bool OscAutoHide { get; set; } = true;

    /// <summary>Idle seconds before the OSC/titlebar fade out (0.5–30).</summary>
    public double OscAutoHideSeconds { get; set; } = 2.5;

    // ---- Stage 5: OSD, status overlays and timeline thumbnails ----

    public bool OsdEnabled { get; set; } = true;

    public OsdPosition OsdPosition { get; set; } = OsdPosition.BottomRight;

    /// <summary>OSD text scale in percent (1–500).</summary>
    public double OsdSizePercent { get; set; } = 100;

    /// <summary>Hold time before the 0.5 s fade-out.</summary>
    public double OsdDurationSeconds { get; set; } = 1;

    /// <summary>Fullscreen bottom-left clock.</summary>
    public bool FullscreenClockEnabled { get; set; } = true;

    /// <summary>Fullscreen bottom-left battery indicator.</summary>
    public bool FullscreenBatteryEnabled { get; set; } = true;

    /// <summary>Include the media title in the fullscreen status line.</summary>
    public bool FullscreenTitleEnabled { get; set; }

    public bool ThumbnailPreviewEnabled { get; set; } = true;

    public int ThumbnailWidth { get; set; } = 160;

    public double ThumbnailBucketSeconds { get; set; } = 5;

    /// <summary>
    /// Legacy compatibility only: the settings UI no longer exposes a cache
    /// floor because the memory cache always sizes itself to every bucket of
    /// the playing video (see <c>ThumbnailService.SetSource</c>).
    /// </summary>
    public int ThumbnailCacheCount { get; set; } = 60;

    /// <summary>Disk budget for pre-generated thumbnails, in megabytes; 0 disables it.</summary>
    public int ThumbnailCacheMegabytes { get; set; } = 512;

    /// <summary>
    /// Slow thumbnail mode: one low-priority worker extracts frames one at a
    /// time and nothing is pre-generated up front, so weak devices stay smooth
    /// at the cost of a slower first hover.
    /// </summary>
    public bool ThumbnailSlowMode { get; set; }

    /// <summary>
    /// Whether timeline thumbnails may use the GPU decoder (libmpv probes the
    /// driver and falls back on its own) or must always decode on the CPU.
    /// </summary>
    public ThumbnailAcceleration ThumbnailAcceleration { get; set; }

    /// <summary>
    /// Pre-generate the whole file (default) or extract only the hovered frame
    /// on demand; see <see cref="ThumbnailMode"/>.
    /// </summary>
    public ThumbnailMode ThumbnailMode { get; set; }

    /// <summary>
    /// How long the pointer must rest on a timeline position before an instant
    /// thumbnail is extracted, so scrubbing across the bar does not queue a
    /// decode per pixel. Clamped to 0–2000 ms by the settings row and service.
    /// </summary>
    public int ThumbnailHoverDelayMs { get; set; } = 250;

    // ---- Quick settings: video (persisted and re-applied on startup) ----

    public string VideoAspect { get; set; } = "no";

    /// <summary>0 = no crop; otherwise the crop aspect ratio.</summary>
    public double VideoCropAspect { get; set; }

    public int VideoRotation { get; set; }

    public bool VideoFlipHorizontal { get; set; }

    public bool VideoFlipVertical { get; set; }

    public bool VideoDeinterlace { get; set; }

    public bool VideoHardwareDecode { get; set; } = true;

    public double VideoZoom { get; set; }

    public double VideoPanX { get; set; }

    public double VideoPanY { get; set; }

    /// <summary>
    /// What the OSC prev/next buttons do: skip playlist tracks (default) or
    /// seek inside the current file.
    /// </summary>
    public OscPrevNextMode OscPrevNextMode { get; set; } = OscPrevNextMode.TrackSkip;

    /// <summary>
    /// Include the current subtitle track when saving a screenshot. Off keeps
    /// only the video frame.
    /// </summary>
    public bool ScreenshotWithSubtitles { get; set; } = true;

    /// <summary>
    /// mpv <c>deband</c> post-processing for the current window. Off by default;
    /// it stays a global video toggle instead of being part of a scaler profile.
    /// </summary>
    public bool DebandEnabled { get; set; }

    // ---- Quick settings: audio ----

    public double Volume { get; set; } = 100;

    public double MaxVolume { get; set; } = 130;

    public bool Muted { get; set; }

    public double AudioDelay { get; set; }

    public string AudioDevice { get; set; } = "auto";

    // ---- Quick settings: subtitles ----

    public bool SubtitleVisible { get; set; } = true;

    public double SubtitleDelay { get; set; }

    public double SubtitlePosition { get; set; } = 100;

    public double SubtitleScale { get; set; } = 1;

    public double SubtitleFontSize { get; set; } = 55;

    public string SubtitleColor { get; set; } = "#FFFFFFFF";

    public double SubtitleBorderSize { get; set; } = 3;

    public int? SecondarySubtitleId { get; set; }

    public bool AlwaysOnTop { get; set; }

    // ---- Stage 6: modes, PiP, mini/music mode, multi-window ----

    /// <summary>Audio-only files enter mini/music mode automatically.</summary>
    public bool AutoSwitchToMusicMode { get; set; } = true;

    /// <summary>Last mini-player playlist panel state (spec §10.4).</summary>
    public bool MusicModeShowPlaylist { get; set; }

    /// <summary>Last PiP window width in physical pixels (16:9 is enforced).</summary>
    public int PipWidth { get; set; } = 480;

    /// <summary>Last PiP window height in physical pixels (16:9 is enforced).</summary>
    public int PipHeight { get; set; } = 270;

    public bool LockAspectRatio { get; set; } = true;

    /// <summary>Floating OSC centre X as a 0..1 fraction of the video area.</summary>
    public double OscX { get; set; } = 0.5;

    /// <summary>Floating OSC centre Y as a 0..1 fraction of the video area.</summary>
    public double OscY { get; set; } = 0.85;

    public int WindowWidth { get; set; } = 1280;

    public int WindowHeight { get; set; } = 720;

    public int WindowX { get; set; } = int.MinValue;

    public int WindowY { get; set; } = int.MinValue;

    /// <summary>Resume local files from the saved history position.</summary>
    public bool ResumePlayback { get; set; } = true;

    /// <summary>Single click on the video area (Stage 3 basic preference).</summary>
    public SingleClickAction SingleClickAction { get; set; } =
        SingleClickAction.HideOsc;

    /// <summary>Recently opened URLs, newest first (max 10).</summary>
    public List<string> RecentUrls { get; set; } = new();

    /// <summary>Volume change per wheel notch over the video.</summary>
    public double WheelVolumeStep { get; set; } = 5;

    /// <summary>Seek seconds per horizontal wheel notch over the video.</summary>
    public double WheelSeekSeconds { get; set; } = 5;

    /// <summary>Folder used by Ctrl+S screenshots.</summary>
    public string ScreenshotFolder { get; set; } = string.Empty;

    // ---- Stage 7: welcome, settings window, network and key bindings ----

    /// <summary>Show the welcome window on startup when no media was given.</summary>
    public bool ShowWelcomeWindow { get; set; } = true;

    /// <summary>Persisted page of the settings window (enum name).</summary>
    public string SettingsPage { get; set; } = "General";

    /// <summary>Restore the last window position/size on startup.</summary>
    public bool RestoreWindowPosition { get; set; } = true;

    /// <summary>UI language preference; resource strings land in Stage 11.</summary>
    public string Language { get; set; } = "system";

    /// <summary>mpv <c>sub-font</c> family used for subtitle text.</summary>
    public string SubtitleFont { get; set; } = "Segoe UI";

    /// <summary>Forward demuxer cache size in MiB (<c>demuxer-max-bytes</c>).</summary>
    public int CacheSizeMb { get; set; } = 150;

    /// <summary>Backward demuxer cache size in MiB (<c>demuxer-max-back-bytes</c>).</summary>
    public int CacheBackMb { get; set; } = 50;

    /// <summary>HTTP proxy ('' = direct).</summary>
    public string Proxy { get; set; } = string.Empty;

    public string UserAgent { get; set; } = "Wiina/0.1";

    public double NetworkTimeoutSeconds { get; set; } = 30;

    /// <summary>mpv log level requested from libmpv: no/info/v/verbose.</summary>
    public string MpvLogLevel { get; set; } = "info";

    /// <summary>Optional yt-dlp executable path (Stage 10 uses it).</summary>
    public string YtDlpPath { get; set; } = string.Empty;

    /// <summary>Reverse the wheel direction for volume/seek gestures.</summary>
    public bool InvertWheelDirection { get; set; }

    /// <summary>
    /// Registers SystemMediaTransportControls and handles the keyboard media
    /// keys; off means Wiina never claims them (other apps stay in charge).
    /// </summary>
    public bool MediaKeysEnabled { get; set; } = true;

    /// <summary>User key bindings; empty means the documented defaults.</summary>
    public List<KeyBindingSetting> KeyBindings { get; set; } = new();

    /// <summary>Named custom key-binding profiles (each owns its bindings).</summary>
    public List<KeyBindingUserProfile> UserKeyProfiles { get; set; } = new();

    // ---- Stage 8: video filters, equalizer and advanced A/V ----

    /// <summary>Ordered user video filters (the editor list).</summary>
    public List<VideoFilterSetting> VideoFilters { get; set; } = new();

    public List<VideoFilterPreset> VideoFilterPresets { get; set; } = new();

    /// <summary>Extra audio filters after the equalizer, in chain order.</summary>
    public List<AudioFilterSetting> AudioFilters { get; set; } = new();

    public List<AudioFilterPreset> AudioFilterPresets { get; set; } = new();

    public bool EqualizerEnabled { get; set; }

    /// <summary>Exactly ten gains in dB (-12..+12), low to high frequency.</summary>
    public List<double> EqualizerGains { get; set; } = new()
    {
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    /// <summary>Last selected EQ preset name, or 自定义 after manual edits.</summary>
    public string EqualizerPreset { get; set; } = "Flat";

    /// <summary>mpv <c>tone-mapping</c> algorithm (auto/bt.2390/st2094-40/...).</summary>
    public string VideoToneMapping { get; set; } = "auto";

    /// <summary>mpv <c>hdr-compute-peak</c>; auto lets libmpv decide.</summary>
    public string VideoHdrComputePeak { get; set; } = "auto";

    /// <summary>mpv <c>target-peak</c>: 0 means auto, otherwise nits.</summary>
    public double VideoTargetPeak { get; set; }

    /// <summary>hwdec mode used when hardware decode is enabled.</summary>
    public string VideoHwdecMode { get; set; } = "auto-safe";

    /// <summary>mpv <c>video-output-levels</c>: auto/limited/full.</summary>
    public string VideoOutputLevels { get; set; } = "auto";

    // Legacy delogo controls, kept separate from the ordered filter list.
    public bool VideoDelogoEnabled { get; set; }

    public int VideoDelogoX { get; set; }

    public int VideoDelogoY { get; set; }

    public int VideoDelogoWidth { get; set; } = 320;

    public int VideoDelogoHeight { get; set; } = 80;

    /// <summary>mpv <c>sub-ass-override</c>: no/yes/scale/strip/force.</summary>
    public string SubtitleAssOverride { get; set; } = "scale";

    public bool SubtitleBold { get; set; }

    public bool SubtitleItalic { get; set; }
}
