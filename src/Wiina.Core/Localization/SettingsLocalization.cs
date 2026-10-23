using Wiina.Core.Settings;

namespace Wiina.Core.Localization;

/// <summary>
/// English overlay for the eleven-page settings catalog. The zh-Hans strings
/// stay in <see cref="SettingsCatalog"/> (the canonical data model), while this
/// overlay supplies the English title, description and choice labels for every
/// row/page/section. A unit test fails if a catalog row or page is missing here.
/// </summary>
public static class SettingsLocalization
{
    private sealed record RowText(string Title, string Description, string[]? Choices);

    private static readonly Dictionary<string, RowText> EnglishRows = new(StringComparer.Ordinal)
    {
        ["general.theme"] = new("Theme", "Dark, light, or follow the system; applied to every playback window immediately.", new[] { "Follow system", "Dark", "Light" }),
        ["general.animations"] = new("Interface animations", "When off, OSC, sidebars, and OSD switch instantly without fades.", null),
        ["general.language"] = new("Interface language", "Choose the UI language; changes apply immediately and are remembered.", new[] { "Follow system", "Simplified Chinese", "English" }),
        ["general.welcome"] = new("Show Welcome on startup", "Show the welcome page when no media was opened from the command line or recent list.", null),
        ["general.resume"] = new("Resume last position", "Resume local files from the saved playback history position.", null),
        ["general.auto-playlist"] = new("Auto-add to playlist", "When a single video opens, also queue other media from its folder; manual mode plays only the selected file.", new[] { "Same folder", "Manual only", "Same folder + subfolders" }),
        ["general.restore-window"] = new("Restore window position", "Restore window size and position at startup; when off, always center the window.", null),
        ["general.edge-snap"] = new("Snap to screen edges", "After dragging, snap the window to the nearest work-area edge, macOS style.", null),
        ["general.screenshot-folder"] = new("Screenshot folder", @"Folder where Ctrl+S saves screenshots (default Pictures\Wiina Screenshots).", null),
        ["general.data-folder"] = new("Open data folder", "Folder containing settings.json, history.json, and the mpv configuration.", null),
        ["ui.layout"] = new("Layout", "Edge-to-edge fills the window with video; Docked reserves space for the titlebar and bottom OSC.", new[] { "Edge-to-edge (overlay video)", "Docked (video makes room)" }),
        ["ui.blur-mode"] = new("Glass performance mode", "Fixed: blur one frame when chrome appears and reuse it. Dynamic: update while chrome is visible (default). Always: keep computing when chrome is hidden. Off: skip capture/blur and use a flat accent-tinted panel (best for low-end devices).", new[] { "Fixed (pre-render one frame)", "Dynamic (default)", "Always compute", "Off (solid tint)" }),
        ["ui.always-on-top"] = new("Always on top", "Keep the playback window above other windows.", null),
        ["ui.lock-aspect"] = new("Lock window aspect ratio", "Preserve the current video aspect while resizing window edges.", null),
        ["ui.sidebar-side"] = new("Sidebar side", "Which side quick settings, playlist, and chapters slide in from.", new[] { "Left", "Right" }),
        ["ui.sidebar-width"] = new("Sidebar width", "300–500 DIP; the sidebar auto-collapses when the window is too narrow.", null),
        ["ui.osc-position"] = new("OSC position", "Playback controls floating, docked at the bottom, or in the top titlebar.", new[] { "Floating", "Bottom", "Top" }),
        ["ui.osc-autohide"] = new("OSC auto-hide", "Fade out after 2.5 seconds idle; stays visible in Docked layout.", null),
        ["ui.osd-enabled"] = new("Show OSD messages", "Status hints for volume, seeking, track changes, and more.", null),
        ["ui.osd-position"] = new("OSD position", "Six placements: bottom/top left or right, plus top or bottom center; the fullscreen clock stays bottom left.", new[] { "Bottom right", "Bottom left", "Top right", "Top left", "Top center", "Bottom center" }),
        ["ui.osd-size"] = new("OSD font size", "Scale the primary text as a percentage; 1–500%.", null),
        ["ui.osd-duration"] = new("OSD hold time", "Seconds before auto-hide; the hide animation stays at 0.5 seconds.", null),
        ["ui.osd-persistent"] = new("Enable persistent OSD", "Toggle an interactive technical info panel; Esc closes it.", null),
        ["ui.fullscreen-clock"] = new("Fullscreen clock", "Show the time in the bottom-left corner in fullscreen.", null),
        ["ui.fullscreen-battery"] = new("Fullscreen battery", "Show battery and charging state in the bottom-left corner in fullscreen.", null),
        ["ui.fullscreen-title"] = new("Show title in fullscreen", "Include the current media title in the bottom-left status line.", null),
        ["ui.thumbnail-preview"] = new("Timeline thumbnails", "Show a time preview and video frame when hovering the progress bar.", null),
        ["ui.thumbnail-width"] = new("Thumbnail width", "80–320 DIP; height follows the video aspect.", null),
        ["ui.thumbnail-bucket"] = new("Thumbnail interval", "Cache buckets per second; larger saves memory but is less precise.", null),
        ["ui.thumbnail-cache"] = new("Thumbnail cache size", "Number of thumbnails kept in memory.", null),
        ["video.hwdec"] = new("Hardware decoding", "Use auto-safe; falls back to software decoding when unsupported.", null),
        ["video.hwdec-mode"] = new("Hardware decoding mode", "mpv hwdec mode used when hardware decoding is on; auto-safe is safest.", null),
        ["video.deinterlace"] = new("Deinterlace", "Enable mpv deinterlacing for interlaced sources.", null),
        ["video.quality"] = new("Video quality preset", "mpv scaling and deband profile; Auto keeps IINA/libmpv defaults, High quality is sharper but uses more GPU.", new[] { "Auto (IINA default)", "Balanced", "High quality", "Performance" }),
        ["video.quality-editor"] = new("Manage quality presets…", "Create, rename, import, export, and edit named mpv scaling profiles, then apply them live.", null),
        ["video.tone-mapping"] = new("HDR tone mapping", "mpv tone-mapping algorithm used to show HDR content on SDR screens.", new[] { "Auto", "BT.2390", "ST2094-40", "ST2094-10", "Spline", "Hable", "Clip", "Gamma", "Reinhard", "Mobius" }),
        ["video.hdr-compute-peak"] = new("HDR peak detection", "Let libmpv compute peak brightness from content; off saves some performance.", new[] { "Auto", "On", "Off" }),
        ["video.target-peak"] = new("Target peak brightness", "Target brightness for tone mapping (nits); 0 means automatic.", null),
        ["video.output-levels"] = new("Output range", "auto/limited/full; try full when full-range grays look washed out.", new[] { "Auto", "Limited (16–235)", "Full (0–255)" }),
        ["video.filters"] = new("Open video filters…", "Add, remove, reorder, and edit filters, save named presets, and apply them to mpv vf immediately.", null),
        ["video.delogo-enabled"] = new("Enable delogo", "Cover a fixed logo position with surrounding pixels; mutually exclusive with delogo in the filter list.", null),
        ["video.delogo-x"] = new("Delogo X", "X coordinate of the rectangle's top-left corner (pixels).", null),
        ["video.delogo-y"] = new("Delogo Y", "Y coordinate of the rectangle's top-left corner (pixels).", null),
        ["video.delogo-width"] = new("Delogo width", "Rectangle width (pixels, at least 2).", null),
        ["video.delogo-height"] = new("Delogo height", "Rectangle height (pixels, at least 2).", null),
        ["video.aspect"] = new("Aspect ratio", "Override the displayed video aspect; by default it follows the container.", new[] { "Auto", "16:9", "4:3", "2.35:1", "1:1", "5:4" }),
        ["video.crop"] = new("Crop", "Center-crop to the selected ratio; recalculated the next time a video loads.", new[] { "None", "16:9", "4:3", "2.35:1", "1:1", "5:4" }),
        ["video.rotation"] = new("Rotation", "Rotate clockwise in 90° increments.", null),
        ["video.flip-h"] = new("Flip horizontally", "Mirror the picture left to right.", null),
        ["video.flip-v"] = new("Flip vertically", "Mirror the picture top to bottom.", null),
        ["video.zoom"] = new("Zoom", "−1 (zoom out) to 1 (zoom in); 0 is original size.", null),
        ["video.pan-x"] = new("Pan horizontally", "−1 to 1 as a fraction of picture width.", null),
        ["video.pan-y"] = new("Pan vertically", "−1 to 1 as a fraction of picture height.", null),
        ["video.reset"] = new("Reset picture settings", "Restore aspect, crop, rotation, flips, zoom, and pan to defaults.", null),
        ["audio.volume"] = new("Volume", "0–200%; above 100% the OSC draws a gain notch.", null),
        ["audio.max-volume"] = new("Volume limit", "Maximum value reachable by the slider and mouse wheel.", null),
        ["audio.mute"] = new("Mute", "Keep the volume value unchanged while muting output.", null),
        ["audio.delay"] = new("Audio delay", "Positive delays audio, negative advances it; −10 to 10 seconds.", null),
        ["audio.device"] = new("Audio output device", "mpv device name; leave empty or auto to follow the system default.", null),
        ["audio.equalizer"] = new("Enable 10-band equalizer", "31 Hz–16 kHz with −12…+12 dB per band; written to mpv af in real time.", null),
        ["audio.equalizer-preset"] = new("Equalizer preset", "Choosing a preset overwrites all ten gains; manual edits switch to \"Custom\".", new[] { "Flat", "Rock", "Jazz", "Classical", "Pop", "Bass", "Treble", "Vocal", "Custom" }),
        ["audio.equalizer-reset"] = new("Reset equalizer", "Set all ten gains to zero and select Flat; the enabled state is unchanged.", null),
        ["audio.filters"] = new("Open audio filters…", "Loudness normalization, dynamic volume, equalizer, and named presets; pitch shift is not supported (see the window for details).", null),
        ["audio.reset"] = new("Reset audio settings", "Restore volume, mute, delay, and device to defaults.", null),
        ["subtitles.visible"] = new("Show subtitles", "Turning this off keeps the subtitle track but stops rendering it.", null),
        ["subtitles.delay"] = new("Subtitle delay", "Positive delays subtitles, negative advances them; −30 to 30 seconds.", null),
        ["subtitles.position"] = new("Subtitle position", "0 (bottom) to 150 (near the top of the picture).", null),
        ["subtitles.scale"] = new("Subtitle scale", "0.5–2.0×.", null),
        ["subtitles.font-size"] = new("Subtitle font size", "8–120, mapped to mpv sub-font-size.", null),
        ["subtitles.font"] = new("Subtitle font", "Choose an installed system font; applied to mpv sub-font.", null),
        ["subtitles.color"] = new("Subtitle color", "Common colors; embedded ASS styles take priority.", new[] { "White", "Yellow", "Cyan", "Green", "Gray" }),
        ["subtitles.border"] = new("Subtitle border", "0–10, mapped to mpv sub-border-size.", null),
        ["subtitles.ass-override"] = new("ASS style override", "no uses the subtitle's own styles; yes/scale/strip/force apply Wiina's style.", new[] { "Use subtitle styles (no)", "Override styles (yes)", "Scale (scale)", "Strip (strip)", "Force (force)" }),
        ["subtitles.bold"] = new("Bold", "Use bold for plain text and styled overrides; embedded ASS styles can still override.", null),
        ["subtitles.italic"] = new("Italic", "Use italic for plain text and styled overrides; embedded ASS styles can still override.", null),
        ["subtitles.image-warning"] = new("Image subtitle notice", "Bitmap subtitles such as PGS/VobSub cannot change font, color, or border; quick settings shows a warning when they are detected.", null),
        ["subtitles.reset"] = new("Reset subtitle settings", "Restore delay, position, scale, size, font, color, and border to defaults.", null),
        ["network.cache"] = new("Forward cache", "demuxer-max-bytes; larger caches tolerate network jitter better.", null),
        ["network.cache-back"] = new("Backward cache", "demuxer-max-back-bytes; used when seeking backward.", null),
        ["network.proxy"] = new("HTTP proxy", "For example http://127.0.0.1:7890; leave empty for a direct connection.", null),
        ["network.user-agent"] = new("User-Agent", "Request header for network streams; empty uses the mpv default.", null),
        ["network.timeout"] = new("Network timeout", "Maximum seconds to wait when opening or reading a stream.", null),
        ["network.ytdlp"] = new("yt-dlp path", "Only uses yt-dlp on the system PATH or an explicit path; never downloads it.", null),
        ["control.single-click"] = new("Video click action", "Action performed when clicking the video area; double-click still toggles fullscreen.", new[] { "Hide OSC", "Play / Pause", "Fullscreen", "None" }),
        ["control.wheel-volume"] = new("Wheel volume step", "Volume change per wheel notch over the video.", null),
        ["control.wheel-seek"] = new("Wheel seek step", "Seconds seeked per horizontal wheel notch.", null),
        ["control.invert-wheel"] = new("Invert wheel direction", "Wheel up lowers volume/seeks back, matching natural scrolling.", null),
        ["control.keybindings"] = new("Edit keyboard shortcuts", "Open the Key Bindings page to record, clear, import, or export shortcuts.", null),
        ["control.media-keys"] = new("System media keys", "Enabled: play/pause, stop, previous, next, and seek.", null),
        ["control.reset"] = new("Reset control settings", "Restore mouse actions, wheel steps, and direction to defaults.", null),
        ["keybindings.editor"] = new("Key bindings", "Record new chords, resolve conflicts, and import/export JSON; changes apply immediately and persist.", null),
        ["advanced.mpv-log"] = new("mpv log level", "Minimum level forwarded from libmpv to the Wiina log.", new[] { "Off (no)", "Info (info)", "Verbose (v)", "Debug (verbose)" }),
        ["advanced.renderer"] = new("Renderer", "gpu-api / gpu-context / hwdec-current for the current window.", null),
        ["advanced.libmpv"] = new("libmpv version", "Client API version of the loaded libmpv.", null),
        ["advanced.config-folder"] = new("Open config folder", @"%LOCALAPPDATA%\Wiina (settings.json / mpv subfolder).", null),
        ["advanced.log-folder"] = new("Open log folder", @"%LOCALAPPDATA%\Wiina\logs and crash logs.", null),
        ["advanced.export-diagnostics"] = new("Export diagnostics", "Write versions, renderer, and key settings as text into the log folder.", null),
        ["advanced.clear-history"] = new("Clear playback history", "Delete recent files and resume positions.", null),
        ["advanced.clear-thumbnails"] = new("Clear thumbnail cache", "Free timeline thumbnails held in memory.", null),
        ["advanced.reset-all"] = new("Reset all settings", "Restore every default (key bindings, window position, and history are unaffected).", null),
        ["plugin.status"] = new("Plugin system status", "The WebView2 host, Info.json, and permission model ship in Stage 9.", null),
        ["plugin.install"] = new("Install plugin…", "Install from a folder or zip and review required permissions.", null),
        ["plugin.folder"] = new("Open plugin folder", @"%LOCALAPPDATA%\Wiina\plugins (currently creates an empty folder).", null),
        ["plugin.list"] = new("Installed plugins", "From Stage 9, shows plugin name, version, author, and permissions.", null),
        ["utilities.font"] = new("Subtitle font", "Shares the setting with the Subtitles page; this is a shortcut.", null),
        ["utilities.open-url"] = new("Open URL…", "Validate an address and open it in the current playback window.", null),
        ["utilities.log-viewer"] = new("Log viewer", "View %TEMP% trace logs and crash logs; refresh, filter, or open the folder.", null),
        ["utilities.inspector"] = new("Inspector", "Show file/video/audio/subtitle technical properties for the current media.", null),
        ["utilities.about"] = new("About Wiina", "Version, license, mpv, and WinUI information.", null),
        ["utilities.technical-osd"] = new("Technical info OSD", "Toggle the persistent technical info panel in the current playback window.", null),
        ["subtitles.smart-autoload"] = new("Smart subtitles", "Automatically find sidecar subtitles next to the video and pick the preferred language.", null),
        ["subtitles.preferred-language"] = new("Preferred subtitle language", "Auto prefers Simplified Chinese, then Traditional Chinese, English, and Japanese.", new[] { "Auto", "Simplified Chinese", "Traditional Chinese", "English", "Japanese" }),
        ["audio.auto-external"] = new("Auto-load external audio", "Automatically load a same-name .mka next to the video; unrelated audio files are never loaded.", null),
        ["control.key-profile"] = new("Keyboard profile", "Choose Default, IINA, or Custom. The IINA profile uses common IINA/mpv shortcuts.", new[] { "Default", "IINA", "Custom" }),
    };

    private static readonly Dictionary<string, (string Title, string Summary)> EnglishPages = new(StringComparer.Ordinal)
    {
        ["General"] = ("General", "Startup, appearance, and history"),
        ["Ui"] = ("Interface", "Window layout, OSC, OSD, and thumbnails"),
        ["Video"] = ("Video", "Hardware decoding, aspect, crop, and picture transforms"),
        ["Audio"] = ("Audio", "Volume limits, delay, and output device"),
        ["Subtitles"] = ("Subtitles", "Font, color, position, and delay"),
        ["Network"] = ("Network", "Cache, proxy, and request headers"),
        ["Control"] = ("Controls", "Mouse, wheel, and keyboard entry points"),
        ["KeyBindings"] = ("Key Bindings", "Recording, conflict detection, import/export"),
        ["Advanced"] = ("Advanced", "Diagnostics, folders, and maintenance"),
        ["Plugin"] = ("Plugins", "Plugin management (Stage 9)"),
        ["Utilities"] = ("Utilities", "Fonts, logs, inspector, and about"),
    };

    private static readonly Dictionary<string, string> EnglishSections = new(StringComparer.Ordinal)
    {
        ["外观与启动"] = "Appearance & Startup",
        ["播放与历史"] = "Playback & History",
        ["播放列表"] = "Playlist",
        ["窗口与 OSC"] = "Window & OSC",
        ["OSD"] = "OSD",
        ["全屏状态栏"] = "Fullscreen Status Bar",
        ["缩略图"] = "Thumbnails",
        ["解码"] = "Decoding",
        ["HDR 与色彩"] = "HDR & Color",
        ["滤镜"] = "Filters",
        ["去 Logo"] = "Remove Logo",
        ["画面"] = "Picture",
        ["重置"] = "Reset",
        ["音量与输出"] = "Volume & Output",
        ["延迟"] = "Delay",
        ["均衡器"] = "Equalizer",
        ["外观"] = "Appearance",
        ["时间与位置"] = "Position & Timing",
        ["ASS 字幕"] = "ASS Subtitles",
        ["智能加载"] = "Smart loading",
        ["缓存"] = "Cache",
        ["网络"] = "Network",
        ["鼠标与滚轮"] = "Mouse & Wheel",
        ["键盘"] = "Keyboard",
        ["快捷键"] = "Shortcuts",
        ["诊断"] = "Diagnostics",
        ["文件夹"] = "Folders",
        ["维护"] = "Maintenance",
        ["插件"] = "Plugins",
        ["已安装插件"] = "Installed Plugins",
        ["工具"] = "Tools",
    };

    /// <summary>True when the catalogue carries an English title/description for the row.</summary>
    public static bool HasRow(string id) => EnglishRows.ContainsKey(id);

    public static string Title(SettingsRowDefinition row, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishRows.TryGetValue(row.Id, out RowText? text)
            ? text.Title
            : row.Title;

    public static string Description(SettingsRowDefinition row, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishRows.TryGetValue(row.Id, out RowText? text)
            ? text.Description
            : row.Description;

    public static IReadOnlyList<string> Choices(SettingsRowDefinition row, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishRows.TryGetValue(row.Id, out RowText? text) &&
        text.Choices is { Length: > 0 } choices
            ? choices
            : row.Choices;

    /// <summary>Unavailable reasons are catalog English overlays keyed by exact zh text.</summary>
    public static string UnavailableReason(string reason, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English
            ? reason switch
            {
                "界面本地化资源在 Stage 11 提供。" => "UI localization is provided in Stage 11.",
                "yt-dlp 解析在 Stage 10 接入。" => "yt-dlp resolution arrives in Stage 10.",
                "插件加载、权限与 Info.json 解析尚未实现（Stage 9）。" =>
                    "Plugin loading, permissions, and Info.json parsing are not implemented (Stage 9).",
                _ => reason,
            }
            : reason;

    public static string PageTitle(SettingsPageInfo page, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishPages.TryGetValue(page.Id.ToString(), out (string Title, string Summary) text)
            ? text.Title
            : page.Title;

    public static string PageSummary(SettingsPageInfo page, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishPages.TryGetValue(page.Id.ToString(), out (string Title, string Summary) text)
            ? text.Summary
            : page.Summary;

    public static string Section(string section, string? language) =>
        AppLanguage.NormalizeTag(language) == AppLanguage.English &&
        EnglishSections.TryGetValue(section, out string? text)
            ? text
            : section;

    public static bool HasSection(string section) => EnglishSections.ContainsKey(section);

    public static bool HasPage(string id) => EnglishPages.ContainsKey(id);
}

