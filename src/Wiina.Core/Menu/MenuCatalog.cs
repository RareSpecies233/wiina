using Wiina.Core.Localization;

namespace Wiina.Core.Menu;

/// <summary>Stable command ids used by the application menu and its handlers.</summary>
public static class MenuCommands
{
    public const string OpenFile = "file.open";
    public const string OpenFolder = "file.open-folder";
    public const string OpenUrl = "file.open-url";
    public const string OpenUrlNewWindow = "file.open-url-new-window";
    public const string OpenInNewWindow = "file.open-in-new-window";
    public const string OpenPlaylist = "file.open-playlist";
    public const string Recent = "file.recent";
    public const string ClearRecent = "file.clear-recent";
    public const string SavePlaylist = "file.save-playlist";
    public const string ScreenshotFolder = "file.screenshot-folder";
    public const string CloseWindow = "file.close";
    public const string Quit = "file.quit";

    public const string PlayPause = "playback.play-pause";
    public const string Stop = "playback.stop";
    public const string StopAndClear = "playback.stop-clear";
    public const string SeekBack5 = "playback.seek-back-5";
    public const string SeekForward5 = "playback.seek-forward-5";
    public const string FramePrevious = "playback.frame-previous";
    public const string FrameNext = "playback.frame-next";
    public const string JumpToStart = "playback.jump-start";
    public const string Speed = "playback.speed";
    public const string SpeedReset = "playback.speed-reset";
    public const string Screenshot = "playback.screenshot";
    public const string AbLoop = "playback.ab-loop";
    public const string Loop = "playback.loop";
    public const string Shuffle = "playback.shuffle";
    public const string PlaylistPrevious = "playback.previous";
    public const string PlaylistNext = "playback.next";
    public const string ShowPlaylist = "playback.show-playlist";
    public const string ShowChapters = "playback.show-chapters";
    public const string ChapterPrevious = "playback.chapter-previous";
    public const string ChapterNext = "playback.chapter-next";

    public const string VideoPanel = "video.panel";
    public const string VideoTracks = "video.tracks";
    public const string VideoHalf = "video.half";
    public const string VideoNormal = "video.normal";
    public const string VideoDouble = "video.double";
    public const string VideoFit = "video.fit";
    public const string VideoZoomIn = "video.zoom-in";
    public const string VideoZoomOut = "video.zoom-out";
    public const string Pip = "video.pip";
    public const string Fullscreen = "video.fullscreen";
    public const string Topmost = "video.topmost";
    public const string LockAspect = "video.lock-aspect";
    public const string MusicMode = "video.music-mode";
    public const string Aspect = "video.aspect";
    public const string Crop = "video.crop";
    public const string Rotate = "video.rotate";
    public const string FlipHorizontal = "video.flip-h";
    public const string FlipVertical = "video.flip-v";
    public const string Deinterlace = "video.deinterlace";
    public const string VideoFilters = "video.filters";

    public const string AudioPanel = "audio.panel";
    public const string AudioTracks = "audio.tracks";
    public const string VolumeUp5 = "audio.volume-up-5";
    public const string VolumeDown5 = "audio.volume-down-5";
    public const string VolumeUp1 = "audio.volume-up-1";
    public const string VolumeDown1 = "audio.volume-down-1";
    public const string Mute = "audio.mute";
    public const string AudioDelayDown = "audio.delay-down";
    public const string AudioDelayUp = "audio.delay-up";
    public const string AudioDelayReset = "audio.delay-reset";
    public const string AudioDevice = "audio.device";
    public const string AudioFilters = "audio.filters";

    public const string SubtitlesPanel = "subtitles.panel";
    public const string SubtitleTracks = "subtitles.tracks";
    public const string SubtitleToggle = "subtitles.toggle";
    public const string SecondarySubtitles = "subtitles.secondary";
    public const string LoadSubtitle = "subtitles.load";
    public const string OnlineSubtitles = "subtitles.online";
    public const string SubtitleDelayDown = "subtitles.delay-down";
    public const string SubtitleDelayUp = "subtitles.delay-up";
    public const string SubtitleDelayReset = "subtitles.delay-reset";
    public const string SubtitlePositionUp = "subtitles.position-up";
    public const string SubtitlePositionDown = "subtitles.position-down";
    public const string SubtitleScaleUp = "subtitles.scale-up";
    public const string SubtitleScaleDown = "subtitles.scale-down";
    public const string SubtitleScaleReset = "subtitles.scale-reset";
    public const string SubtitleFont = "subtitles.font";
    public const string SubtitleSettings = "subtitles.settings";

    public const string PluginManage = "plugin.manage";
    public const string PluginInstall = "plugin.install";
    public const string PluginFolder = "plugin.folder";

    public const string NewWindow = "window.new";
    public const string OpenInNew = "window.open-in-new";
    public const string Minimize = "window.minimize";
    public const string Maximize = "window.maximize";
    public const string Inspector = "window.inspector";
    public const string LogViewer = "window.log";
    public const string Settings = "window.settings";

    public const string Welcome = "help.welcome";
    public const string About = "help.about";
    public const string LogFolder = "help.log-folder";
    public const string ExportDebug = "help.export-debug";
    public const string ProjectPage = "help.project";
    public const string Shortcuts = "help.shortcuts";
}

/// <summary>Snapshot of player state used to resolve menu enable/check rules.</summary>
public sealed record MenuState
{
    public bool HasMedia { get; init; }
    public bool IsPlaying { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudioTrack { get; init; }
    public bool HasSubtitleTrack { get; init; }
    public bool SubtitlesVisible { get; init; } = true;
    public int PlaylistCount { get; init; }
    public int ChapterCount { get; init; }
    public bool IsFullscreen { get; init; }
    public bool IsPip { get; init; }
    public bool IsMini { get; init; }
    public bool AlwaysOnTop { get; init; }
    public bool LockAspect { get; init; } = true;
    public bool Shuffle { get; init; }
    public bool AbLoopActive { get; init; }
    public bool Muted { get; init; }

    /// <summary>0 = off, 1 = playlist loop, 2 = single-file loop.</summary>
    public int LoopMode { get; init; }

    public double Speed { get; init; } = 1;

    public string Aspect { get; init; } = "no";
    public double CropAspect { get; init; }
    public int Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public bool Deinterlace { get; init; }
    public bool HasAudioDevice { get; init; } = true;
    public bool PluginHostAvailable { get; init; }
}

public enum MenuItemKind
{
    Item,
    Separator,
    Submenu,
}

/// <summary>One definition in the application menu tree.</summary>
public sealed record MenuItemDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public string Command { get; init; } = string.Empty;

    public string? Argument { get; init; }

    public string? Shortcut { get; init; }

    public bool Enabled { get; init; } = true;

    public bool IsChecked { get; init; }

    public string? DisabledReason { get; init; }

    public MenuItemKind Kind { get; init; } = MenuItemKind.Item;

    public IReadOnlyList<MenuItemDefinition> Children { get; init; } =
        Array.Empty<MenuItemDefinition>();
}

public sealed record MenuGroupDefinition(
    string Id,
    string Title,
    IReadOnlyList<MenuItemDefinition> Items);

/// <summary>
/// Builds the eight top-level menus with dynamic labels and the enable rules
/// from spec §14.3. Dynamic submenu contents (tracks, chapters, recent files,
/// devices) are appended by the window.
/// </summary>
public static class MenuCatalog
{
    public static IReadOnlyList<MenuGroupDefinition> Build(
        MenuState state,
        string? language = AppLanguage.ZhHans)
    {
        var groups = new[]
        {
            FileMenu(state),
            PlaybackMenu(state),
            VideoMenu(state),
            AudioMenu(state),
            SubtitlesMenu(state),
            PluginMenu(state),
            WindowMenu(state),
            HelpMenu(state),
        };
        return MenuLocalization.Translate(groups, language);
    }

    /// <summary>Flattens a built tree (parents included, depth first).</summary>
    public static IReadOnlyList<MenuItemDefinition> Flatten(
        IEnumerable<MenuGroupDefinition> groups)
    {
        var items = new List<MenuItemDefinition>();
        void Add(MenuItemDefinition item)
        {
            items.Add(item);
            foreach (MenuItemDefinition child in item.Children)
            {
                Add(child);
            }
        }

        foreach (MenuGroupDefinition group in groups)
        {
            foreach (MenuItemDefinition item in group.Items)
            {
                Add(item);
            }
        }

        return items;
    }

    private static MenuGroupDefinition FileMenu(MenuState state) =>
        new(
            "file",
            "文件",
            new[]
            {
                Item("file.open", "打开文件…", MenuCommands.OpenFile, shortcut: "Ctrl+O"),
                Item("file.open-folder", "打开文件夹…", MenuCommands.OpenFolder),
                Item("file.open-url", "打开 URL…", MenuCommands.OpenUrl, shortcut: "Ctrl+Shift+O"),
                Item("file.open-url-new", "在新窗口打开 URL…", MenuCommands.OpenUrlNewWindow),
                Item("file.open-in-new", "在新窗口打开…", MenuCommands.OpenInNewWindow, shortcut: "Ctrl+Shift+N"),
                Item("file.open-playlist", "打开播放列表…", MenuCommands.OpenPlaylist),
                Submenu("file.recent", "打开最近", MenuCommands.Recent),
                Separator(),
                Item("file.save-playlist", "保存播放列表…", MenuCommands.SavePlaylist,
                    enabled: state.PlaylistCount > 0),
                Item("file.screenshot-folder", "截图到文件夹…", MenuCommands.ScreenshotFolder,
                    enabled: state.HasMedia),
                Separator(),
                Item("file.close", "关闭窗口", MenuCommands.CloseWindow, shortcut: "Ctrl+W"),
                Item("file.quit", "退出", MenuCommands.Quit, shortcut: "Ctrl+Q"),
            });

    private static MenuGroupDefinition PlaybackMenu(MenuState state) =>
        new(
            "playback",
            "播放",
            new[]
            {
                Item(
                    "playback.play-pause",
                    state.IsPlaying ? "暂停" : "继续播放",
                    MenuCommands.PlayPause,
                    shortcut: "Space",
                    enabled: state.HasMedia),
                Item("playback.stop", "停止", MenuCommands.Stop, shortcut: "Ctrl+.",
                    enabled: state.HasMedia),
                Item("playback.stop-clear", "停止并清空播放列表", MenuCommands.StopAndClear,
                    enabled: state.HasMedia && state.PlaylistCount > 0),
                Separator(),
                Item("playback.seek-back", "后退 5 秒", MenuCommands.SeekBack5, shortcut: "←",
                    enabled: state.HasMedia),
                Item("playback.seek-forward", "前进 5 秒", MenuCommands.SeekForward5, shortcut: "→",
                    enabled: state.HasMedia),
                Item("playback.frame-previous", "上一帧", MenuCommands.FramePrevious, shortcut: "Alt+←",
                    enabled: state.HasMedia),
                Item("playback.frame-next", "下一帧", MenuCommands.FrameNext, shortcut: "Alt+→",
                    enabled: state.HasMedia),
                Item("playback.jump-start", "跳到开头", MenuCommands.JumpToStart,
                    enabled: state.HasMedia),
                Separator(),
                Submenu("playback.speed", $"速度：{state.Speed:0.##}×", MenuCommands.Speed,
                    enabled: state.HasMedia,
                    children: new[]
                    {
                        SpeedItem(state, 0.5),
                        SpeedItem(state, 0.75),
                        SpeedItem(state, 1.0),
                        SpeedItem(state, 1.25),
                        SpeedItem(state, 1.5),
                        SpeedItem(state, 2.0),
                        Sep(),
                        Item("playback.speed-reset", "重置 1×", MenuCommands.SpeedReset,
                            shortcut: "Ctrl+Backspace", enabled: state.HasMedia),
                    }),
                Item("playback.screenshot", "截图", MenuCommands.Screenshot, shortcut: "Ctrl+S",
                    enabled: state.HasMedia),
                Item("playback.ab-loop", "A-B 循环", MenuCommands.AbLoop, shortcut: "Ctrl+L",
                    enabled: state.HasMedia, isChecked: state.AbLoopActive),
                Submenu("playback.loop", "循环模式", MenuCommands.Loop,
                    enabled: state.HasMedia,
                    children: new[]
                    {
                        Item("playback.loop-off", "关闭", MenuCommands.Loop, argument: "0",
                            enabled: state.HasMedia, isChecked: state.LoopMode == 0),
                        Item("playback.loop-list", "列表循环", MenuCommands.Loop, argument: "1",
                            enabled: state.HasMedia, isChecked: state.LoopMode == 1),
                        Item("playback.loop-one", "单曲循环", MenuCommands.Loop, argument: "2",
                            enabled: state.HasMedia, isChecked: state.LoopMode == 2),
                    }),
                Item("playback.shuffle", "随机播放", MenuCommands.Shuffle,
                    enabled: state.HasMedia, isChecked: state.Shuffle),
                Separator(),
                Item("playback.previous", "上一首", MenuCommands.PlaylistPrevious,
                    shortcut: "Ctrl+←", enabled: state.PlaylistCount > 1),
                Item("playback.next", "下一首", MenuCommands.PlaylistNext,
                    shortcut: "Ctrl+→", enabled: state.PlaylistCount > 1),
                Item("playback.show-playlist", "显示播放列表", MenuCommands.ShowPlaylist),
                Item("playback.show-chapters", "显示章节", MenuCommands.ShowChapters),
                Item("playback.chapter-previous", "上一章", MenuCommands.ChapterPrevious,
                    enabled: state.ChapterCount > 0),
                Item("playback.chapter-next", "下一章", MenuCommands.ChapterNext,
                    enabled: state.ChapterCount > 0),
            });

    private static MenuGroupDefinition VideoMenu(MenuState state) =>
        new(
            "video",
            "视频",
            new[]
            {
                Item("video.panel", "显示视频面板", MenuCommands.VideoPanel),
                Submenu("video.tracks", "视频轨道", MenuCommands.VideoTracks,
                    enabled: state.HasVideo),
                Separator(),
                Item("video.half", "0.5× 窗口", MenuCommands.VideoHalf, enabled: state.HasMedia),
                Item("video.normal", "1× 窗口", MenuCommands.VideoNormal, enabled: state.HasMedia),
                Item("video.double", "2× 窗口", MenuCommands.VideoDouble, enabled: state.HasMedia),
                Item("video.fit", "适合屏幕", MenuCommands.VideoFit, enabled: state.HasMedia),
                Item("video.zoom-in", "放大窗口", MenuCommands.VideoZoomIn, enabled: state.HasMedia),
                Item("video.zoom-out", "缩小窗口", MenuCommands.VideoZoomOut, enabled: state.HasMedia),
                Separator(),
                Item("video.pip", state.IsPip ? "退出画中画" : "画中画", MenuCommands.Pip,
                    shortcut: "Ctrl+Shift+P", isChecked: state.IsPip),
                Item("video.fullscreen", state.IsFullscreen ? "退出全屏" : "进入全屏",
                    MenuCommands.Fullscreen, shortcut: "F11", isChecked: state.IsFullscreen),
                Item("video.topmost", "窗口置顶", MenuCommands.Topmost,
                    isChecked: state.AlwaysOnTop),
                Item("video.lock-aspect", "锁定宽高比", MenuCommands.LockAspect,
                    isChecked: state.LockAspect),
                Item("video.music-mode", state.IsMini ? "退出音乐模式" : "音乐模式",
                    MenuCommands.MusicMode, shortcut: "Ctrl+Shift+M", isChecked: state.IsMini),
                Separator(),
                Submenu("video.aspect", "宽高比", MenuCommands.Aspect,
                    enabled: state.HasMedia,
                    children: AspectItems(state)),
                Submenu("video.crop", "裁剪", MenuCommands.Crop,
                    enabled: state.HasMedia,
                    children: CropItems(state)),
                Submenu("video.rotate", "旋转", MenuCommands.Rotate,
                    enabled: state.HasMedia,
                    children: new[]
                    {
                        RotationItem(state, 0, "0°"),
                        RotationItem(state, 90, "90°"),
                        RotationItem(state, 180, "180°"),
                        RotationItem(state, 270, "270°"),
                    }),
                Item("video.flip-h", "水平翻转", MenuCommands.FlipHorizontal,
                    enabled: state.HasMedia, isChecked: state.FlipHorizontal),
                Item("video.flip-v", "垂直翻转", MenuCommands.FlipVertical,
                    enabled: state.HasMedia, isChecked: state.FlipVertical),
                Item("video.deinterlace", "去隔行", MenuCommands.Deinterlace,
                    enabled: state.HasMedia, isChecked: state.Deinterlace),
                Item("video.filters", "视频滤镜…", MenuCommands.VideoFilters,
                    enabled: true),
            });

    private static MenuGroupDefinition AudioMenu(MenuState state) =>
        new(
            "audio",
            "音频",
            new[]
            {
                Item("audio.panel", "显示音频面板", MenuCommands.AudioPanel),
                Submenu("audio.tracks", "音频轨道", MenuCommands.AudioTracks,
                    enabled: state.HasAudioTrack),
                Separator(),
                Item("audio.volume-up-5", "音量 +5%", MenuCommands.VolumeUp5, shortcut: "↑",
                    enabled: state.HasMedia),
                Item("audio.volume-down-5", "音量 -5%", MenuCommands.VolumeDown5, shortcut: "↓",
                    enabled: state.HasMedia),
                Item("audio.volume-up-1", "音量 +1%", MenuCommands.VolumeUp1, shortcut: "Ctrl+↑",
                    enabled: state.HasMedia),
                Item("audio.volume-down-1", "音量 -1%", MenuCommands.VolumeDown1, shortcut: "Ctrl+↓",
                    enabled: state.HasMedia),
                Item("audio.mute", "静音", MenuCommands.Mute, shortcut: "M",
                    enabled: state.HasMedia, isChecked: state.Muted),
                Separator(),
                Item("audio.delay-down", "音频延迟 -0.1s", MenuCommands.AudioDelayDown,
                    enabled: state.HasMedia),
                Item("audio.delay-up", "音频延迟 +0.1s", MenuCommands.AudioDelayUp,
                    enabled: state.HasMedia),
                Item("audio.delay-reset", "重置音频延迟", MenuCommands.AudioDelayReset,
                    enabled: state.HasMedia),
                Submenu("audio.device", "音频设备", MenuCommands.AudioDevice,
                    enabled: state.HasAudioDevice && state.HasMedia),
                Separator(),
                Item("audio.filters", "音频滤镜…", MenuCommands.AudioFilters,
                    enabled: true),
            });

    private static MenuGroupDefinition SubtitlesMenu(MenuState state) =>
        new(
            "subtitles",
            "字幕",
            new[]
            {
                Item("subtitles.panel", "显示字幕面板", MenuCommands.SubtitlesPanel),
                Submenu("subtitles.tracks", "字幕轨道", MenuCommands.SubtitleTracks,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.toggle", state.SubtitlesVisible ? "隐藏字幕" : "显示字幕",
                    MenuCommands.SubtitleToggle, shortcut: "V",
                    enabled: state.HasSubtitleTrack, isChecked: state.SubtitlesVisible),
                Submenu("subtitles.secondary", "次字幕", MenuCommands.SecondarySubtitles,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.load", "加载外挂字幕…", MenuCommands.LoadSubtitle,
                    enabled: state.HasMedia),
                Item("subtitles.online", "在线字幕…", MenuCommands.OnlineSubtitles,
                    enabled: false, disabledReason: "在线字幕（OpenSubtitles）在 Stage 10 提供。"),
                Separator(),
                Item("subtitles.delay-down", "延迟 -0.1s", MenuCommands.SubtitleDelayDown,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.delay-up", "延迟 +0.1s", MenuCommands.SubtitleDelayUp,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.delay-reset", "延迟归零", MenuCommands.SubtitleDelayReset,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.position-up", "位置上移", MenuCommands.SubtitlePositionUp,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.position-down", "位置下移", MenuCommands.SubtitlePositionDown,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.scale-up", "放大字幕", MenuCommands.SubtitleScaleUp,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.scale-down", "缩小字幕", MenuCommands.SubtitleScaleDown,
                    enabled: state.HasSubtitleTrack),
                Item("subtitles.scale-reset", "重置缩放", MenuCommands.SubtitleScaleReset,
                    enabled: state.HasSubtitleTrack),
                Separator(),
                Item("subtitles.font", "字幕字体…", MenuCommands.SubtitleFont),
                Item("subtitles.settings", "字幕设置…", MenuCommands.SubtitleSettings),
            });

    private static MenuGroupDefinition PluginMenu(MenuState state) =>
        new(
            "plugin",
            "插件",
            new[]
            {
                Item("plugin.manage", "插件管理…", MenuCommands.PluginManage,
                    enabled: state.PluginHostAvailable,
                    disabledReason: "插件系统在 Stage 9 提供。"),
                Item("plugin.install", "安装插件…", MenuCommands.PluginInstall,
                    enabled: state.PluginHostAvailable,
                    disabledReason: "插件安装、权限与 Info.json 在 Stage 9 提供。"),
                Item("plugin.folder", "打开插件文件夹", MenuCommands.PluginFolder),
            });

    private static MenuGroupDefinition WindowMenu(MenuState state) =>
        new(
            "window",
            "窗口",
            new[]
            {
                Item("window.new", "新建窗口", MenuCommands.NewWindow, shortcut: "Ctrl+N"),
                Item("window.open-in-new", "在新窗口打开…", MenuCommands.OpenInNew,
                    shortcut: "Ctrl+Shift+N"),
                Separator(),
                Item("window.minimize", "最小化", MenuCommands.Minimize),
                Item("window.maximize", "最大化 / 还原", MenuCommands.Maximize),
                Item("window.half", "0.5× 窗口", MenuCommands.VideoHalf),
                Item("window.normal", "1× 窗口", MenuCommands.VideoNormal),
                Item("window.double", "2× 窗口", MenuCommands.VideoDouble),
                Item("window.fit", "适合屏幕", MenuCommands.VideoFit),
                Separator(),
                Item("window.inspector", "检查器…", MenuCommands.Inspector),
                Item("window.log", "日志查看器…", MenuCommands.LogViewer),
                Separator(),
                Item("window.settings", "设置…", MenuCommands.Settings, shortcut: "Ctrl+,"),
                Item("window.close", "关闭窗口", MenuCommands.CloseWindow, shortcut: "Ctrl+W"),
            });

    private static MenuGroupDefinition HelpMenu(MenuState state) =>
        new(
            "help",
            "帮助",
            new[]
            {
                Item("help.welcome", "欢迎窗口", MenuCommands.Welcome),
                Item("help.about", "关于 Wiina", MenuCommands.About),
                Item("help.shortcuts", "键盘快捷键…", MenuCommands.Shortcuts),
                Separator(),
                Item("help.log-folder", "打开日志文件夹", MenuCommands.LogFolder),
                Item("help.export-debug", "导出调试信息", MenuCommands.ExportDebug),
                Separator(),
                Item("help.project", "GitHub 项目主页", MenuCommands.ProjectPage),
            });

    private static IEnumerable<MenuItemDefinition> AspectItems(MenuState state) =>
        new (string Label, string Value)[]
        {
            ("自动", "no"),
            ("16:9", "16:9"),
            ("4:3", "4:3"),
            ("2.35:1", "2.35:1"),
            ("1:1", "1:1"),
            ("5:4", "5:4"),
        }.Select(pair => Item(
            "video.aspect." + pair.Value,
            pair.Label,
            MenuCommands.Aspect,
            argument: pair.Value,
            enabled: state.HasMedia,
            isChecked: string.Equals(state.Aspect, pair.Value, StringComparison.Ordinal)));

    private static IEnumerable<MenuItemDefinition> CropItems(MenuState state) =>
        new (string Label, double Value)[]
        {
            ("无", 0),
            ("16:9", 16.0 / 9.0),
            ("4:3", 4.0 / 3.0),
            ("2.35:1", 2.35),
            ("1:1", 1),
            ("5:4", 1.25),
        }.Select(pair => Item(
            "video.crop." + pair.Label,
            pair.Label,
            MenuCommands.Crop,
            argument: pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            enabled: state.HasMedia,
            isChecked: Math.Abs(state.CropAspect - pair.Value) < 0.001));

    private static MenuItemDefinition RotationItem(MenuState state, int degrees, string label) =>
        Item(
            $"video.rotate.{degrees}",
            label,
            MenuCommands.Rotate,
            argument: degrees.ToString(System.Globalization.CultureInfo.InvariantCulture),
            enabled: state.HasMedia,
            isChecked: state.Rotation == degrees);

    private static MenuItemDefinition SpeedItem(MenuState state, double speed) =>
        Item(
            $"playback.speed.{speed:0.##}",
            $"{speed:0.##}×",
            MenuCommands.Speed,
            argument: speed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            enabled: state.HasMedia,
            isChecked: Math.Abs(state.Speed - speed) < 0.01);

    private static MenuItemDefinition Item(
        string id,
        string label,
        string command,
        string? argument = null,
        string? shortcut = null,
        bool enabled = true,
        bool isChecked = false,
        string? disabledReason = null) =>
        new()
        {
            Id = id,
            Label = label,
            Command = command,
            Argument = argument,
            Shortcut = shortcut,
            Enabled = enabled,
            IsChecked = isChecked,
            DisabledReason = disabledReason,
            Kind = MenuItemKind.Item,
        };

    private static MenuItemDefinition Submenu(
        string id,
        string label,
        string command,
        bool enabled = true,
        IEnumerable<MenuItemDefinition>? children = null) =>
        new()
        {
            Id = id,
            Label = label,
            Command = command,
            Enabled = enabled,
            Kind = MenuItemKind.Submenu,
            Children = children?.ToArray() ?? Array.Empty<MenuItemDefinition>(),
        };

    private static MenuItemDefinition Separator() =>
        new()
        {
            Id = "separator",
            Label = string.Empty,
            Kind = MenuItemKind.Separator,
        };

    private static MenuItemDefinition Sep() => Separator();
}
