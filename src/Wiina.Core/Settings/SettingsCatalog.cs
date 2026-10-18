using Wiina.Core.Filters;
using Wiina.Core.Input;
using Wiina.Core.Localization;
using Wiina.Core.Models;
using Wiina.Core.State;

namespace Wiina.Core.Settings;

/// <summary>
/// The complete Stage 7 settings catalog: eleven pages, their sections and every
/// row with a typed getter/setter over <see cref="AppSettings"/>. The WinUI
/// window renders this list; keeping it in Core makes coverage, search ranking
/// and round-trips testable without starting the app.
/// </summary>
public static class SettingsCatalog
{
    private static readonly SettingsPageInfo[] PageList =
    {
        new(SettingsPageId.General, "通用", "启动、外观与历史记录", "\uE713"),
        new(SettingsPageId.Ui, "界面", "窗口布局、OSC、OSD 与缩略图", "\uE790"),
        new(SettingsPageId.Video, "视频", "硬件解码、比例、裁剪与画面变换", "\uE714"),
        new(SettingsPageId.Audio, "音频", "音量上限、延迟与输出设备", "\uE767"),
        new(SettingsPageId.Subtitles, "字幕", "字体、颜色、位置与延迟", "\uE8D8"),
        new(SettingsPageId.Network, "网络", "缓存、代理与请求头", "\uE774"),
        new(SettingsPageId.Control, "控制", "鼠标、滚轮与键盘入口", "\uE765"),
        new(SettingsPageId.KeyBindings, "按键绑定", "录制、冲突检测与导入导出", "\uE765"),
        new(SettingsPageId.Advanced, "高级", "诊断、文件夹与维护", "\uE7EF"),
        new(SettingsPageId.Plugin, "插件", "插件管理（Stage 9）", "\uEA86"),
        new(SettingsPageId.Utilities, "工具", "字体、日志、检查器与关于", "\uE90F"),
    };

    private static readonly SettingsRowDefinition[] RowList = BuildRows();

    public static IReadOnlyList<SettingsPageInfo> Pages => PageList;

    public static IReadOnlyList<SettingsRowDefinition> Rows => RowList;

    public static SettingsPageInfo Page(SettingsPageId page) =>
        PageList.First(info => info.Id == page);

    /// <summary>Ordered section names for one page (spec §12.1 dots).</summary>
    public static IReadOnlyList<string> Sections(SettingsPageId page) =>
        RowList
            .Where(row => row.Page == page)
            .Select(row => row.Section)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<SettingsRowDefinition> RowsFor(SettingsPageId page) =>
        RowList.Where(row => row.Page == page).ToArray();

    public static SettingsRowDefinition? Find(string id) =>
        RowList.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.Ordinal));

    public static SettingsSearchIndex SearchIndex { get; } =
        new(PageList, RowList);

    private static SettingsRowDefinition[] BuildRows()
    {
        var rows = new List<SettingsRowDefinition>();

        // ------------------------------------------------------------- General
        rows.Add(ChoiceRow(
            "general.theme", SettingsPageId.General, "外观与启动",
            "主题", "深色、浅色或跟随系统；立即应用到所有播放窗口。",
            new[] { "跟随系统", "深色", "浅色" },
            settings => (int)settings.Theme,
            (settings, value) => settings.Theme = (ThemeMode)Math.Clamp(value, 0, 2),
            "theme appearance dark light 主题 深色 浅色"));
        rows.Add(BoolRow(
            "general.animations", SettingsPageId.General, "外观与启动",
            "界面动画", "关闭后 OSC、侧栏与 OSD 直接切换，不做淡入淡出。",
            settings => settings.AnimationsEnabled,
            (settings, value) => settings.AnimationsEnabled = value,
            "animation motion reduce 动画"));
        rows.Add(ChoiceRow(
            "general.language", SettingsPageId.General, "外观与启动",
            "界面语言", "选择界面语言；更改立即应用并记住，重启后保持。",
            new[] { "跟随系统", "简体中文", "English" },
            settings => AppLanguage.ToChoiceIndex(settings.Language),
            (settings, value) => settings.Language = AppLanguage.FromChoiceIndex(value),
            "language locale i18n 语言"));
        rows.Add(BoolRow(
            "general.welcome", SettingsPageId.General, "外观与启动",
            "启动时显示欢迎窗口", "没有通过命令行/最近文件打开媒体时显示欢迎页。",
            settings => settings.ShowWelcomeWindow,
            (settings, value) => settings.ShowWelcomeWindow = value,
            "welcome startup 欢迎 启动"));
        rows.Add(BoolRow(
            "general.resume", SettingsPageId.General, "播放与历史",
            "继续上次播放位置", "打开本地文件时从播放历史断点续播。",
            settings => settings.ResumePlayback,
            (settings, value) => settings.ResumePlayback = value,
            "resume history 续播 历史"));
        rows.Add(BoolRow(
            "general.restore-window", SettingsPageId.General, "播放与历史",
            "恢复上次窗口位置", "启动时还原窗口尺寸与位置；关闭后总是居中显示。",
            settings => settings.RestoreWindowPosition,
            (settings, value) => settings.RestoreWindowPosition = value,
            "window position restore 窗口 位置"));
        rows.Add(ActionRow(
            "general.screenshot-folder", SettingsPageId.General, "播放与历史",
            "截图文件夹",
            "Ctrl+S 保存截图的目录（默认 图片\\Wiina Screenshots）。",
            "choose-screenshot-folder",
            "screenshot folder 截图 文件夹",
            settings => GetScreenshotFolder(settings)));
        rows.Add(ActionRow(
            "general.data-folder", SettingsPageId.General, "播放与历史",
            "打开数据文件夹", "settings.json、history.json 与 mpv 配置所在的目录。",
            "open-data-folder",
            "data folder config 数据 配置"));

        // ------------------------------------------------------------------ UI
        rows.Add(ChoiceRow(
            "ui.layout", SettingsPageId.Ui, "窗口与 OSC",
            "布局", "Edge-to-edge 让视频铺满；Docked 让标题栏和底部 OSC 占位。",
            new[] { "Edge-to-edge（覆盖视频）", "Docked（视频让路）" },
            settings => settings.Layout == WindowLayoutMode.Docked ? 1 : 0,
            (settings, value) => settings.Layout = value >= 1
                ? WindowLayoutMode.Docked
                : WindowLayoutMode.EdgeToEdge,
            "layout docked edge 布局"));
        rows.Add(BoolRow(
            "ui.always-on-top", SettingsPageId.Ui, "窗口与 OSC",
            "窗口置顶", "播放窗口始终位于其他窗口之上。",
            settings => settings.AlwaysOnTop,
            (settings, value) => settings.AlwaysOnTop = value,
            "topmost pin always on top 置顶"));
        rows.Add(BoolRow(
            "ui.lock-aspect", SettingsPageId.Ui, "窗口与 OSC",
            "锁定窗口宽高比", "拖动窗口边缘时保持当前视频比例。",
            settings => settings.LockAspectRatio,
            (settings, value) => settings.LockAspectRatio = value,
            "aspect ratio lock 宽高比"));
        rows.Add(ChoiceRow(
            "ui.sidebar-side", SettingsPageId.Ui, "窗口与 OSC",
            "侧边栏位置", "快速设置、播放列表和章节从窗口哪一侧滑入。",
            new[] { "左侧", "右侧" },
            settings => settings.SidebarSide == SidebarSide.Trailing ? 1 : 0,
            (settings, value) => settings.SidebarSide = value >= 1
                ? SidebarSide.Trailing
                : SidebarSide.Leading,
            "sidebar side 侧边栏 位置"));
        rows.Add(NumberRow(
            "ui.sidebar-width", SettingsPageId.Ui, "窗口与 OSC",
            "侧边栏宽度", "300–500 DIP；窗口过窄时会自动收起侧边栏。",
            300, 500, 10, " DIP",
            settings => settings.SidebarWidth,
            (settings, value) => settings.SidebarWidth = Math.Clamp(value, 300, 500),
            "sidebar width 侧边栏 宽度"));
        rows.Add(ChoiceRow(
            "ui.osc-position", SettingsPageId.Ui, "窗口与 OSC",
            "OSC 位置", "浮动、贴底或顶部标题栏内的播放控制条。",
            new[] { "浮动", "底部", "顶部" },
            settings => (int)settings.OscPosition,
            (settings, value) => settings.OscPosition = (OscPosition)Math.Clamp(value, 0, 2),
            "osc controller position 控制条 位置"));
        rows.Add(BoolRow(
            "ui.osc-autohide", SettingsPageId.Ui, "窗口与 OSC",
            "OSC 自动隐藏", "2.5 秒无操作后淡出；Docked 布局下不隐藏。",
            settings => settings.OscAutoHide,
            (settings, value) => settings.OscAutoHide = value,
            "osc autohide 自动隐藏"));
        rows.Add(BoolRow(
            "ui.osd-enabled", SettingsPageId.Ui, "OSD",
            "显示 OSD 消息", "音量、跳转、轨道切换等状态提示。",
            settings => settings.OsdEnabled,
            (settings, value) => settings.OsdEnabled = value,
            "osd overlay message OSD 状态"));
        rows.Add(ChoiceRow(
            "ui.osd-position", SettingsPageId.Ui, "OSD",
            "OSD 位置", "六个位置：左右下角、左右上角、顶部居中或底部居中。",
            new[] { "右下", "左下", "右上", "左上", "顶部居中", "底部居中" },
            settings => OsdPositions.ToIndex(settings.OsdPosition),
            (settings, value) => settings.OsdPosition = OsdPositions.FromIndex(value),
            "osd position OSD 位置"));
        rows.Add(NumberRow(
            "ui.osd-size", SettingsPageId.Ui, "OSD",
            "OSD 字号", "按百分比缩放主文本；1–500%。",
            1, 500, 5, " %",
            settings => settings.OsdSizePercent,
            (settings, value) => settings.OsdSizePercent = value,
            "osd size font OSD 字号"));
        rows.Add(NumberRow(
            "ui.osd-duration", SettingsPageId.Ui, "OSD",
            "OSD 停留时间", "自动隐藏前的停留秒数；隐藏动画固定 0.5 秒。",
            0.2, 5, 0.1, " 秒",
            settings => settings.OsdDurationSeconds,
            (settings, value) => settings.OsdDurationSeconds = Math.Clamp(value, 0.2, 5),
            "osd duration timeout OSD 时间"));
        rows.Add(BoolRow(
            "ui.osd-persistent", SettingsPageId.Ui, "OSD",
            "启用持久 OSD", "切换后显示可交互的技术信息面板；Esc 关闭。",
            settings => settings.OsdPersistent,
            (settings, value) => settings.OsdPersistent = value,
            "osd persistent 持久 OSD"));
        rows.Add(BoolRow(
            "ui.fullscreen-clock", SettingsPageId.Ui, "全屏状态栏",
            "全屏时钟", "全屏时在左下角显示时间。",
            settings => settings.FullscreenClockEnabled,
            (settings, value) => settings.FullscreenClockEnabled = value,
            "fullscreen clock 全屏 时钟"));
        rows.Add(BoolRow(
            "ui.fullscreen-battery", SettingsPageId.Ui, "全屏状态栏",
            "全屏电量", "全屏时在左下角显示电池与充电状态。",
            settings => settings.FullscreenBatteryEnabled,
            (settings, value) => settings.FullscreenBatteryEnabled = value,
            "fullscreen battery 全屏 电量"));
        rows.Add(BoolRow(
            "ui.fullscreen-title", SettingsPageId.Ui, "全屏状态栏",
            "全屏显示标题", "在左下角状态行中包含当前媒体标题。",
            settings => settings.FullscreenTitleEnabled,
            (settings, value) => settings.FullscreenTitleEnabled = value,
            "fullscreen title 全屏 标题"));
        rows.Add(BoolRow(
            "ui.thumbnail-preview", SettingsPageId.Ui, "缩略图",
            "进度条缩略图", "悬停进度条时显示时间预览与视频帧。",
            settings => settings.ThumbnailPreviewEnabled,
            (settings, value) => settings.ThumbnailPreviewEnabled = value,
            "thumbnail preview 缩略图"));
        rows.Add(NumberRow(
            "ui.thumbnail-width", SettingsPageId.Ui, "缩略图",
            "缩略图宽度", "80–320 DIP；高度按视频比例计算。",
            80, 320, 10, " DIP",
            settings => settings.ThumbnailWidth,
            (settings, value) => settings.ThumbnailWidth = (int)Math.Round(Math.Clamp(value, 80, 320)),
            "thumbnail width 缩略图 宽度"));
        rows.Add(NumberRow(
            "ui.thumbnail-bucket", SettingsPageId.Ui, "缩略图",
            "缩略图间隔", "按秒分桶缓存，越大越省内存、精度越低。",
            1, 30, 1, " 秒",
            settings => settings.ThumbnailBucketSeconds,
            (settings, value) => settings.ThumbnailBucketSeconds = Math.Clamp(value, 1, 30),
            "thumbnail bucket interval 缩略图 间隔"));
        rows.Add(NumberRow(
            "ui.thumbnail-cache", SettingsPageId.Ui, "缩略图",
            "缩略图缓存数量", "内存中保留的缩略图张数。",
            10, 240, 10, " 张",
            settings => settings.ThumbnailCacheCount,
            (settings, value) => settings.ThumbnailCacheCount = (int)Math.Round(Math.Clamp(value, 10, 240)),
            "thumbnail cache 缩略图 缓存"));

        // --------------------------------------------------------------- Video
        rows.Add(BoolRow(
            "video.hwdec", SettingsPageId.Video, "解码",
            "硬件解码", "使用 auto-safe；显卡/驱动不支持时自动回退软解。",
            settings => settings.VideoHardwareDecode,
            (settings, value) => settings.VideoHardwareDecode = value,
            "hardware decode hwdec 硬解 解码"));
        rows.Add(ChoiceRow(
            "video.hwdec-mode", SettingsPageId.Video, "解码",
            "硬件解码模式", "开启硬件解码时使用的 mpv hwdec 模式；auto-safe 最稳妥。",
            new[] { "auto-safe", "auto", "d3d11va", "dxva2", "no" },
            settings => IndexOf(settings.VideoHwdecMode,
                new[] { "auto-safe", "auto", "d3d11va", "dxva2", "no" }, 0),
            (settings, value) => settings.VideoHwdecMode =
                new[] { "auto-safe", "auto", "d3d11va", "dxva2", "no" }[
                    Math.Clamp(value, 0, 4)],
            "hwdec hardware decode d3d11va dxva2 硬解 模式"));
        rows.Add(BoolRow(
            "video.deinterlace", SettingsPageId.Video, "解码",
            "去隔行", "对隔行扫描素材启用 mpv 去隔行。",
            settings => settings.VideoDeinterlace,
            (settings, value) => settings.VideoDeinterlace = value,
            "deinterlace 去隔行"));
        rows.Add(ChoiceRow(
            "video.tone-mapping", SettingsPageId.Video, "HDR 与色彩",
            "HDR 色调映射", "SDR 屏幕上显示 HDR 内容时使用的 mpv tone-mapping 算法。",
            new[]
            {
                "自动", "BT.2390", "ST2094-40", "ST2094-10", "Spline",
                "Hable", "Clip", "Gamma", "Reinhard", "Mobius",
            },
            settings => IndexOf(settings.VideoToneMapping,
                new[]
                {
                    "auto", "bt.2390", "st2094-40", "st2094-10", "spline",
                    "hable", "clip", "gamma", "reinhard", "mobius",
                }, 0),
            (settings, value) => settings.VideoToneMapping = new[]
            {
                "auto", "bt.2390", "st2094-40", "st2094-10", "spline",
                "hable", "clip", "gamma", "reinhard", "mobius",
            }[Math.Clamp(value, 0, 9)],
            "hdr tone mapping tonemap 色调映射 HDR"));
        rows.Add(ChoiceRow(
            "video.hdr-compute-peak", SettingsPageId.Video, "HDR 与色彩",
            "HDR 峰值计算", "自动让 libmpv 根据内容计算峰值亮度；关闭可省一点性能。",
            new[] { "自动", "开启", "关闭" },
            settings => settings.VideoHdrComputePeak switch
            {
                "yes" => 1,
                "no" => 2,
                _ => 0,
            },
            (settings, value) => settings.VideoHdrComputePeak = value switch
            {
                1 => "yes",
                2 => "no",
                _ => "auto",
            },
            "hdr compute peak 峰值 HDR"));
        rows.Add(NumberRow(
            "video.target-peak", SettingsPageId.Video, "HDR 与色彩",
            "目标峰值亮度", "色调映射的目标亮度（nits）；0 表示自动。",
            0, 4000, 50, " nit（0=自动）",
            settings => settings.VideoTargetPeak,
            (settings, value) => settings.VideoTargetPeak = Math.Round(
                Math.Clamp(value, 0, 4000)),
            "target peak nits brightness HDR 亮度"));
        rows.Add(ChoiceRow(
            "video.output-levels", SettingsPageId.Video, "HDR 与色彩",
            "输出范围", "auto/limited/full；全范围灰阶发灰时可尝试 full。",
            new[] { "自动", "Limited（16–235）", "Full（0–255）" },
            settings => IndexOf(settings.VideoOutputLevels,
                new[] { "auto", "limited", "full" }, 0),
            (settings, value) => settings.VideoOutputLevels =
                new[] { "auto", "limited", "full" }[Math.Clamp(value, 0, 2)],
            "color range levels output 范围 灰阶"));
        rows.Add(ActionRow(
            "video.filters", SettingsPageId.Video, "滤镜",
            "打开视频滤镜…", "添加/删除/排序/编辑滤镜，保存命名预设并即时应用到 mpv vf。",
            "open-video-filters",
            "video filter vf 滤镜"));
        rows.Add(BoolRow(
            "video.delogo-enabled", SettingsPageId.Video, "去 Logo",
            "启用去 Logo", "用周围像素覆盖固定位置的台标；与滤镜列表里的 delogo 互斥。",
            settings => settings.VideoDelogoEnabled,
            (settings, value) => settings.VideoDelogoEnabled = value,
            "delogo logo 台标 去logo"));
        rows.Add(NumberRow(
            "video.delogo-x", SettingsPageId.Video, "去 Logo",
            "去 Logo X", "矩形左上角 X（像素）。",
            0, 16384, 1, " px",
            settings => settings.VideoDelogoX,
            (settings, value) => settings.VideoDelogoX = (int)Math.Round(
                Math.Clamp(value, 0, 16384)),
            "delogo x logo 台标"));
        rows.Add(NumberRow(
            "video.delogo-y", SettingsPageId.Video, "去 Logo",
            "去 Logo Y", "矩形左上角 Y（像素）。",
            0, 16384, 1, " px",
            settings => settings.VideoDelogoY,
            (settings, value) => settings.VideoDelogoY = (int)Math.Round(
                Math.Clamp(value, 0, 16384)),
            "delogo y logo 台标"));
        rows.Add(NumberRow(
            "video.delogo-width", SettingsPageId.Video, "去 Logo",
            "去 Logo 宽度", "矩形宽度（像素，至少 2）。",
            2, 16384, 2, " px",
            settings => settings.VideoDelogoWidth,
            (settings, value) => settings.VideoDelogoWidth = (int)Math.Round(
                Math.Clamp(value, 2, 16384)),
            "delogo width 宽度 台标"));
        rows.Add(NumberRow(
            "video.delogo-height", SettingsPageId.Video, "去 Logo",
            "去 Logo 高度", "矩形高度（像素，至少 2）。",
            2, 16384, 2, " px",
            settings => settings.VideoDelogoHeight,
            (settings, value) => settings.VideoDelogoHeight = (int)Math.Round(
                Math.Clamp(value, 2, 16384)),
            "delogo height 高度 台标"));
        rows.Add(ChoiceRow(
            "video.aspect", SettingsPageId.Video, "画面",
            "宽高比", "覆盖视频显示比例；默认跟随容器。",
            QuickSettings.AspectRatios.Select(option => option.Label).ToArray(),
            settings =>
            {
                string value = QuickSettings.NormalizeAspect(settings.VideoAspect);
                int index = QuickSettings.AspectRatios
                    .ToList()
                    .FindIndex(option => string.Equals(option.Value, value, StringComparison.Ordinal));
                return index < 0 ? 0 : index;
            },
            (settings, value) => settings.VideoAspect = QuickSettings
                .AspectRatios[Math.Clamp(value, 0, QuickSettings.AspectRatios.Count - 1)].Value,
            "aspect ratio 宽高比"));
        rows.Add(ChoiceRow(
            "video.crop", SettingsPageId.Video, "画面",
            "裁剪", "居中裁剪到指定比例；下次加载视频时重新计算。",
            QuickSettings.CropOptions.Select(option => option.Label).ToArray(),
            settings =>
            {
                int index = QuickSettings.CropOptions
                    .ToList()
                    .FindIndex(option => Math.Abs(option.Aspect - settings.VideoCropAspect) < 0.001);
                return index < 0 ? 0 : index;
            },
            (settings, value) => settings.VideoCropAspect = QuickSettings
                .CropOptions[Math.Clamp(value, 0, QuickSettings.CropOptions.Count - 1)].Aspect,
            "crop 裁剪"));
        rows.Add(ChoiceRow(
            "video.rotation", SettingsPageId.Video, "画面",
            "旋转", "顺时针旋转 90° 的整数倍。",
            new[] { "0°", "90°", "180°", "270°" },
            settings => QuickSettings.NormalizeRotation(settings.VideoRotation) / 90,
            (settings, value) => settings.VideoRotation = Math.Clamp(value, 0, 3) * 90,
            "rotation rotate 旋转"));
        rows.Add(BoolRow(
            "video.flip-h", SettingsPageId.Video, "画面",
            "水平翻转", "左右镜像画面。",
            settings => settings.VideoFlipHorizontal,
            (settings, value) => settings.VideoFlipHorizontal = value,
            "flip horizontal mirror 水平 翻转"));
        rows.Add(BoolRow(
            "video.flip-v", SettingsPageId.Video, "画面",
            "垂直翻转", "上下镜像画面。",
            settings => settings.VideoFlipVertical,
            (settings, value) => settings.VideoFlipVertical = value,
            "flip vertical mirror 垂直 翻转"));
        rows.Add(NumberRow(
            "video.zoom", SettingsPageId.Video, "画面",
            "缩放", "−1（缩小）到 1（放大）；0 为原始大小。",
            -1, 1, 0.05, string.Empty,
            settings => settings.VideoZoom,
            (settings, value) => settings.VideoZoom = QuickSettings.ClampZoom(value),
            "zoom 缩放"));
        rows.Add(NumberRow(
            "video.pan-x", SettingsPageId.Video, "画面",
            "水平平移", "−1 到 1，按画面宽度比例。",
            -1, 1, 0.05, string.Empty,
            settings => settings.VideoPanX,
            (settings, value) => settings.VideoPanX = QuickSettings.ClampPan(value),
            "pan horizontal 平移"));
        rows.Add(NumberRow(
            "video.pan-y", SettingsPageId.Video, "画面",
            "垂直平移", "−1 到 1，按画面高度比例。",
            -1, 1, 0.05, string.Empty,
            settings => settings.VideoPanY,
            (settings, value) => settings.VideoPanY = QuickSettings.ClampPan(value),
            "pan vertical 平移"));
        rows.Add(ActionRow(
            "video.reset", SettingsPageId.Video, "重置",
            "还原画面设置", "比例、裁剪、旋转、翻转、缩放与平移恢复默认。",
            "reset-video",
            "reset video 还原 重置"));

        // --------------------------------------------------------------- Audio
        rows.Add(NumberRow(
            "audio.volume", SettingsPageId.Audio, "音量与输出",
            "音量", "0–200%；超过 100% 时 OSC 会画出增益缺口。",
            0, 200, 1, " %",
            settings => settings.Volume,
            (settings, value) => settings.Volume = QuickSettings.ClampVolume(value, settings.MaxVolume),
            "volume 音量"));
        rows.Add(NumberRow(
            "audio.max-volume", SettingsPageId.Audio, "音量与输出",
            "音量上限", "限制音量滑条与滚轮能到达的最大值。",
            100, 200, 5, " %",
            settings => settings.MaxVolume,
            (settings, value) => settings.MaxVolume = QuickSettings.ClampMaxVolume(value),
            "max volume 上限"));
        rows.Add(BoolRow(
            "audio.mute", SettingsPageId.Audio, "音量与输出",
            "静音", "保持音量值不变，仅关闭输出。",
            settings => settings.Muted,
            (settings, value) => settings.Muted = value,
            "mute 静音"));
        rows.Add(NumberRow(
            "audio.delay", SettingsPageId.Audio, "延迟",
            "音频延迟", "正值延后音频，负值提前；−10 到 10 秒。",
            -10, 10, 0.1, " 秒",
            settings => settings.AudioDelay,
            (settings, value) => settings.AudioDelay = QuickSettings.ClampAudioDelay(value),
            "audio delay sync 延迟 音画同步"));
        rows.Add(TextRow(
            "audio.device", SettingsPageId.Audio, "音量与输出",
            "音频输出设备", "mpv 设备名；留空或 auto 表示跟随系统默认。",
            settings => settings.AudioDevice,
            (settings, value) => settings.AudioDevice = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim(),
            "audio device output 输出 设备"));
        rows.Add(BoolRow(
            "audio.auto-external", SettingsPageId.Audio, "音量与输出",
            "自动加载外挂音轨", "自动加载与视频同名的 .mka 音轨；不会加载无关音频文件。",
            settings => settings.AutoLoadExternalAudio,
            (settings, value) => settings.AutoLoadExternalAudio = value,
            "external audio mka 外挂音轨 自动"));
        rows.Add(BoolRow(
            "audio.equalizer", SettingsPageId.Audio, "均衡器",
            "启用 10 段均衡器", "31 Hz–16 kHz，每段 −12…+12 dB；实时写入 mpv af。",
            settings => settings.EqualizerEnabled,
            (settings, value) => settings.EqualizerEnabled = value,
            "equalizer eq 均衡器 10段"));
        rows.Add(ChoiceRow(
            "audio.equalizer-preset", SettingsPageId.Audio, "均衡器",
            "均衡器预设", "选择预设会立即覆盖十段增益；手动调整后变为“自定义”。",
            EqualizerBands.PresetNames.Concat(new[] { EqualizerBands.CustomPreset }).ToArray(),
            settings => IndexOf(settings.EqualizerPreset,
                EqualizerBands.PresetNames.Concat(new[] { EqualizerBands.CustomPreset }).ToArray(),
                EqualizerBands.PresetNames.Count),
            (settings, value) => ApplyEqualizerPreset(settings, value),
            "equalizer preset rock jazz classical pop bass treble vocal 均衡器 预设"));
        rows.Add(ActionRow(
            "audio.equalizer-reset", SettingsPageId.Audio, "均衡器",
            "重置均衡器", "十段增益归零并选择 Flat；启用状态保持不变。",
            "reset-equalizer",
            "equalizer reset flat 均衡器 重置"));
        rows.Add(ActionRow(
            "audio.filters", SettingsPageId.Audio, "滤镜",
            "打开音频滤镜…", "响度标准化、动态音量、均衡器与命名预设；不支持变调，原因见窗口。",
            "open-audio-filters",
            "audio filter af loudnorm dynaudnorm 音频 滤镜"));
        rows.Add(ActionRow(
            "audio.reset", SettingsPageId.Audio, "重置",
            "还原音频设置", "音量、静音、延迟与设备恢复默认。",
            "reset-audio",
            "reset audio 还原 重置"));

        // ----------------------------------------------------------- Subtitles
        rows.Add(BoolRow(
            "subtitles.visible", SettingsPageId.Subtitles, "外观",
            "显示字幕", "关闭后仍保留字幕轨道，只是不渲染。",
            settings => settings.SubtitleVisible,
            (settings, value) => settings.SubtitleVisible = value,
            "subtitle visibility 字幕 显示"));
        rows.Add(NumberRow(
            "subtitles.delay", SettingsPageId.Subtitles, "时间与位置",
            "字幕延迟", "正值延后字幕，负值提前；−30 到 30 秒。",
            -30, 30, 0.1, " 秒",
            settings => settings.SubtitleDelay,
            (settings, value) => settings.SubtitleDelay = QuickSettings.ClampSubtitleDelay(value),
            "subtitle delay sync 字幕 延迟"));
        rows.Add(NumberRow(
            "subtitles.position", SettingsPageId.Subtitles, "时间与位置",
            "字幕位置", "0（底部）到 150（画面上方）。",
            0, 150, 1, string.Empty,
            settings => settings.SubtitlePosition,
            (settings, value) => settings.SubtitlePosition = QuickSettings.ClampSubtitlePosition(value),
            "subtitle position 字幕 位置"));
        rows.Add(NumberRow(
            "subtitles.scale", SettingsPageId.Subtitles, "外观",
            "字幕缩放", "0.5–2.0 倍。",
            0.5, 2, 0.05, " ×",
            settings => settings.SubtitleScale,
            (settings, value) => settings.SubtitleScale = QuickSettings.ClampSubtitleScale(value),
            "subtitle scale 字幕 缩放"));
        rows.Add(NumberRow(
            "subtitles.font-size", SettingsPageId.Subtitles, "外观",
            "字幕字号", "8–120，对应 mpv sub-font-size。",
            8, 120, 1, string.Empty,
            settings => settings.SubtitleFontSize,
            (settings, value) => settings.SubtitleFontSize = QuickSettings.ClampSubtitleFontSize(value),
            "subtitle font size 字幕 字号"));
        rows.Add(FontRow(
            "subtitles.font", SettingsPageId.Subtitles, "外观",
            "字幕字体", "从系统已安装字体中选择；应用于 mpv sub-font。",
            settings => settings.SubtitleFont,
            (settings, value) => settings.SubtitleFont = Text.FontNameValidation.Normalize(value),
            "subtitle font family 字幕 字体"));
        rows.Add(ChoiceRow(
            "subtitles.color", SettingsPageId.Subtitles, "外观",
            "字幕颜色", "常用颜色；ASS 字幕的内嵌样式优先。",
            QuickSettings.SubtitleColors.Select(option => option.Label).ToArray(),
            settings =>
            {
                string value = QuickSettings.NormalizeColor(settings.SubtitleColor);
                int index = QuickSettings.SubtitleColors
                    .ToList()
                    .FindIndex(option => string.Equals(option.Value, value, StringComparison.Ordinal));
                return index < 0 ? 0 : index;
            },
            (settings, value) => settings.SubtitleColor = QuickSettings
                .SubtitleColors[Math.Clamp(value, 0, QuickSettings.SubtitleColors.Count - 1)].Value,
            "subtitle color 字幕 颜色"));
        rows.Add(NumberRow(
            "subtitles.border", SettingsPageId.Subtitles, "外观",
            "字幕边框", "0–10，对应 mpv sub-border-size。",
            0, 10, 0.5, string.Empty,
            settings => settings.SubtitleBorderSize,
            (settings, value) => settings.SubtitleBorderSize = QuickSettings.ClampSubtitleBorder(value),
            "subtitle border outline 字幕 边框"));
        rows.Add(ChoiceRow(
            "subtitles.ass-override", SettingsPageId.Subtitles, "ASS 字幕",
            "ASS 样式覆盖", "no 使用字幕自带样式；yes/scale/strip/force 强制应用 Wiina 样式。",
            new[] { "使用字幕样式（no）", "覆盖样式（yes）", "缩放（scale）", "剥离（strip）", "强制（force）" },
            settings => IndexOf(settings.SubtitleAssOverride,
                new[] { "no", "yes", "scale", "strip", "force" }, 2),
            (settings, value) => settings.SubtitleAssOverride =
                new[] { "no", "yes", "scale", "strip", "force" }[Math.Clamp(value, 0, 4)],
            "ass ssa override subtitle style 字幕 样式 覆盖"));
        rows.Add(BoolRow(
            "subtitles.bold", SettingsPageId.Subtitles, "ASS 字幕",
            "粗体", "纯文本与覆盖样式时使用粗体；ASS 内嵌样式仍可能覆盖。",
            settings => settings.SubtitleBold,
            (settings, value) => settings.SubtitleBold = value,
            "subtitle bold 字幕 粗体"));
        rows.Add(BoolRow(
            "subtitles.italic", SettingsPageId.Subtitles, "ASS 字幕",
            "斜体", "纯文本与覆盖样式时使用斜体；ASS 内嵌样式仍可能覆盖。",
            settings => settings.SubtitleItalic,
            (settings, value) => settings.SubtitleItalic = value,
            "subtitle italic 字幕 斜体"));
        rows.Add(InfoRow(
            "subtitles.image-warning", SettingsPageId.Subtitles, "ASS 字幕",
            "图像字幕提示", "PGS/VobSub 等位图字幕无法调整字体、颜色与边框；检测到图像字幕时快速设置会显示警告。",
            "image subtitle pgs vobsub warning 图像字幕 警告",
            settings => "PGS/VobSub：样式不可调；ASS/SSA：部分样式由字幕决定"));
        rows.Add(BoolRow(
            "subtitles.smart-autoload", SettingsPageId.Subtitles, "智能加载",
            "智能字幕", "自动查找视频旁边的字幕文件，并按首选语言选择。",
            settings => settings.AutoLoadSmartSubtitles,
            (settings, value) => settings.AutoLoadSmartSubtitles = value,
            "smart subtitle sidecar 自动 字幕 外挂"));
        string[] subtitleLanguages = { "auto", "zh-Hans", "zh-Hant", "en", "ja" };
        rows.Add(ChoiceRow(
            "subtitles.preferred-language", SettingsPageId.Subtitles, "智能加载",
            "首选字幕语言", "自动时优先简体中文，其次繁体、英语、日语。",
            new[] { "自动", "简体中文", "繁体中文", "English", "日本語" },
            settings => IndexOf(settings.PreferredSubtitleLanguage,
                subtitleLanguages,
                0),
            (settings, value) => settings.PreferredSubtitleLanguage =
                subtitleLanguages[Math.Clamp(value, 0, subtitleLanguages.Length - 1)],
            "subtitle language preference 字幕 语言 简繁"));
        rows.Add(ActionRow(
            "subtitles.reset", SettingsPageId.Subtitles, "重置",
            "还原字幕设置", "延迟、位置、缩放、字号、字体、颜色与边框恢复默认。",
            "reset-subtitles",
            "reset subtitle 还原 重置"));

        // ------------------------------------------------------------- Network
        rows.Add(NumberRow(
            "network.cache", SettingsPageId.Network, "缓存",
            "前向缓存", "demuxer-max-bytes；网络抖动时更大的缓存更耐卡顿。",
            10, 2000, 10, " MiB",
            settings => settings.CacheSizeMb,
            (settings, value) => settings.CacheSizeMb = (int)Math.Round(Math.Clamp(value, 10, 2000)),
            "cache buffer network 缓存"));
        rows.Add(NumberRow(
            "network.cache-back", SettingsPageId.Network, "缓存",
            "回退缓存", "demuxer-max-back-bytes；用于往回跳转。",
            0, 2000, 10, " MiB",
            settings => settings.CacheBackMb,
            (settings, value) => settings.CacheBackMb = (int)Math.Round(Math.Clamp(value, 0, 2000)),
            "cache back seek 回退 缓存"));
        rows.Add(TextRow(
            "network.proxy", SettingsPageId.Network, "网络",
            "HTTP 代理", "如 http://127.0.0.1:7890；留空表示直连。",
            settings => settings.Proxy,
            (settings, value) => settings.Proxy = value.Trim(),
            "proxy http 代理"));
        rows.Add(TextRow(
            "network.user-agent", SettingsPageId.Network, "网络",
            "User-Agent", "网络流请求头；空值使用 mpv 默认。",
            settings => settings.UserAgent,
            (settings, value) => settings.UserAgent = value.Trim(),
            "user agent http header 请求头"));
        rows.Add(NumberRow(
            "network.timeout", SettingsPageId.Network, "网络",
            "网络超时", "打开/读取流的最长等待秒数。",
            5, 120, 5, " 秒",
            settings => settings.NetworkTimeoutSeconds,
            (settings, value) => settings.NetworkTimeoutSeconds = Math.Clamp(value, 5, 120),
            "network timeout 超时"));
        rows.Add(TextRow(
            "network.ytdlp", SettingsPageId.Network, "网络",
            "yt-dlp 路径", "仅使用系统 PATH 或显式路径中的 yt-dlp，不自动下载。",
            settings => settings.YtDlpPath,
            (settings, value) => settings.YtDlpPath = value.Trim(),
            "ytdlp youtube 下载",
            unavailable: "yt-dlp 解析在 Stage 10 接入。"));

        // ------------------------------------------------------------- Control
        rows.Add(ChoiceRow(
            "control.single-click", SettingsPageId.Control, "鼠标与滚轮",
            "视频单击动作", "单击视频区域时执行的动作；双击仍切换全屏。",
            new[] { "隐藏 OSC", "播放 / 暂停", "全屏", "无" },
            settings => (int)settings.SingleClickAction,
            (settings, value) => settings.SingleClickAction =
                (SingleClickAction)Math.Clamp(value, 0, 3),
            "click mouse single 单击 鼠标"));
        rows.Add(NumberRow(
            "control.wheel-volume", SettingsPageId.Control, "鼠标与滚轮",
            "滚轮音量步长", "视频区域每格滚轮改变的音量。",
            1, 20, 1, " %",
            settings => settings.WheelVolumeStep,
            (settings, value) => settings.WheelVolumeStep = Math.Clamp(value, 1, 20),
            "wheel volume 滚轮 音量"));
        rows.Add(NumberRow(
            "control.wheel-seek", SettingsPageId.Control, "鼠标与滚轮",
            "滚轮跳转步长", "水平滚动每格跳转的秒数。",
            1, 60, 1, " 秒",
            settings => settings.WheelSeekSeconds,
            (settings, value) => settings.WheelSeekSeconds = Math.Clamp(value, 1, 60),
            "wheel seek 滚轮 跳转"));
        rows.Add(BoolRow(
            "control.invert-wheel", SettingsPageId.Control, "鼠标与滚轮",
            "反转滚轮方向", "向上滚降低音量/后退，符合“自然滚动”。",
            settings => settings.InvertWheelDirection,
            (settings, value) => settings.InvertWheelDirection = value,
            "invert wheel natural 滚轮 反向"));
        rows.Add(ActionRow(
            "control.keybindings", SettingsPageId.Control, "键盘",
            "编辑键盘快捷键", "打开“按键绑定”页录制、清除或导入导出快捷键。",
            "open-keybindings",
            "keyboard shortcut 快捷键 键盘"));
        rows.Add(InfoRow(
            "control.media-keys", SettingsPageId.Control, "键盘",
            "系统媒体键", "已启用：播放/暂停、停止、上一首、下一首与进度拖动。",
            "media keys smtc 媒体键",
            settings => "已启用（SystemMediaTransportControls）"));
        string[] keyProfiles = KeyBindingProfiles.All.ToArray();
        rows.Add(ChoiceRow(
            "control.key-profile", SettingsPageId.Control, "键盘",
            "键盘方案", "默认、IINA 或自定义；IINA 使用常见 IINA/mpv 快捷键。",
            keyProfiles,
            settings => IndexOf(settings.KeyBindingProfile, keyProfiles, 0),
            (settings, value) => settings.KeyBindingProfile =
                keyProfiles[Math.Clamp(value, 0, keyProfiles.Length - 1)],
            "keyboard profile iina 键盘方案 快捷键"));
        rows.Add(ActionRow(
            "control.reset", SettingsPageId.Control, "重置",
            "还原控制设置", "鼠标动作、滚轮步长与方向恢复默认。",
            "reset-control",
            "reset control mouse 还原 重置"));

        // -------------------------------------------------------- Key bindings
        rows.Add(new SettingsRowDefinition
        {
            Id = "keybindings.editor",
            Page = SettingsPageId.KeyBindings,
            Section = "快捷键",
            Title = "按键绑定",
            Description = "录制新的组合键、处理冲突、导入/导出 JSON；更改立即生效并持久化。",
            Keywords = "keybindings shortcuts record conflict import export 快捷键 冲突",
            Kind = SettingsRowKind.KeyBindings,
        });

        // ------------------------------------------------------------ Advanced
        rows.Add(ChoiceRow(
            "advanced.mpv-log", SettingsPageId.Advanced, "诊断",
            "mpv 日志级别", "libmpv 转发到 Wiina 日志的最低级别。",
            new[] { "关闭（no）", "信息（info）", "详细（v）", "调试（verbose）" },
            settings => settings.MpvLogLevel switch
            {
                "no" => 0,
                "v" => 2,
                "verbose" => 3,
                _ => 1,
            },
            (settings, value) => settings.MpvLogLevel = value switch
            {
                0 => "no",
                2 => "v",
                3 => "verbose",
                _ => "info",
            },
            "mpv log level 日志"));
        rows.Add(InfoRow(
            "advanced.renderer", SettingsPageId.Advanced, "诊断",
            "渲染器", "当前窗口的 gpu-api / gpu-context / hwdec-current。",
            "renderer gpu video 渲染器",
            settings => "由当前播放窗口提供"));
        rows.Add(InfoRow(
            "advanced.libmpv", SettingsPageId.Advanced, "诊断",
            "libmpv 版本", "已加载的 libmpv 客户端 API 版本。",
            "libmpv version mpv 版本",
            settings => "由播放引擎提供"));
        rows.Add(ActionRow(
            "advanced.config-folder", SettingsPageId.Advanced, "文件夹",
            "打开配置文件夹", "%LOCALAPPDATA%\\Wiina（settings.json / mpv 子目录）。",
            "open-config-folder",
            "config folder 配置 文件夹"));
        rows.Add(ActionRow(
            "advanced.log-folder", SettingsPageId.Advanced, "文件夹",
            "打开日志文件夹", "%LOCALAPPDATA%\\Wiina\\logs 与崩溃日志。",
            "open-log-folder",
            "log folder 日志 文件夹"));
        rows.Add(ActionRow(
            "advanced.export-diagnostics", SettingsPageId.Advanced, "诊断",
            "导出诊断信息", "把版本、渲染器、关键设置为文本写入日志文件夹。",
            "export-diagnostics",
            "diagnostics export 诊断 导出"));
        rows.Add(ActionRow(
            "advanced.clear-history", SettingsPageId.Advanced, "维护",
            "清空播放历史", "删除最近文件与断点续播记录。",
            "clear-history",
            "clear history 清空 历史"));
        rows.Add(ActionRow(
            "advanced.clear-thumbnails", SettingsPageId.Advanced, "维护",
            "清空缩略图缓存", "释放内存中的时间轴缩略图。",
            "clear-thumbnails",
            "clear thumbnails cache 缩略图 清空"));
        rows.Add(ActionRow(
            "advanced.reset-all", SettingsPageId.Advanced, "维护",
            "重置所有设置", "恢复全部默认值（按键绑定、窗口位置与历史不受影响）。",
            "reset-all-settings",
            "reset all settings 重置 全部"));

        // -------------------------------------------------------------- Plugin
        rows.Add(InfoRow(
            "plugin.status", SettingsPageId.Plugin, "插件",
            "插件系统状态", "WebView2 宿主、Info.json 与权限模型在 Stage 9 实装。",
            "plugin status 插件 状态",
            settings => "未启用（Stage 9）"));
        rows.Add(ActionRow(
            "plugin.install", SettingsPageId.Plugin, "插件",
            "安装插件…", "从文件夹或 zip 安装，并展示所需权限。",
            "install-plugin",
            "plugin install 安装 插件",
            unavailable: "插件加载、权限与 Info.json 解析尚未实现（Stage 9）。"));
        rows.Add(ActionRow(
            "plugin.folder", SettingsPageId.Plugin, "插件",
            "打开插件文件夹", "%LOCALAPPDATA%\\Wiina\\plugins（当前会创建空目录）。",
            "open-plugin-folder",
            "plugin folder 插件 文件夹"));
        rows.Add(new SettingsRowDefinition
        {
            Id = "plugin.list",
            Page = SettingsPageId.Plugin,
            Section = "已安装插件",
            Title = "已安装插件",
            Description = "Stage 9 起显示插件名称、版本、作者与权限。",
            Keywords = "plugin list installed 插件 列表",
            Kind = SettingsRowKind.PluginList,
        });

        // ----------------------------------------------------------- Utilities
        rows.Add(FontRow(
            "utilities.font", SettingsPageId.Utilities, "工具",
            "字幕字体", "与“字幕”页共用同一设置，这里提供快速入口。",
            settings => settings.SubtitleFont,
            (settings, value) => settings.SubtitleFont = Text.FontNameValidation.Normalize(value),
            "font picker 字体"));
        rows.Add(ActionRow(
            "utilities.open-url", SettingsPageId.Utilities, "工具",
            "打开 URL…", "校验地址后交给当前播放窗口打开。",
            "open-url-dialog",
            "url open 地址"));
        rows.Add(ActionRow(
            "utilities.log-viewer", SettingsPageId.Utilities, "工具",
            "日志查看器", "查看 %TEMP% 跟踪日志与崩溃日志，可刷新、过滤、打开文件夹。",
            "open-log-viewer",
            "log viewer 日志"));
        rows.Add(ActionRow(
            "utilities.inspector", SettingsPageId.Utilities, "工具",
            "检查器", "显示当前媒体的文件/视频/音频/字幕技术属性。",
            "open-inspector",
            "inspector technical info 检查器 技术信息"));
        rows.Add(ActionRow(
            "utilities.about", SettingsPageId.Utilities, "工具",
            "关于 Wiina", "版本、许可证、mpv 与 WinUI 信息。",
            "open-about",
            "about version license 关于 版本"));
        rows.Add(ActionRow(
            "utilities.technical-osd", SettingsPageId.Utilities, "工具",
            "技术信息 OSD", "在当前播放窗口切换持久技术信息面板。",
            "toggle-technical-osd",
            "osd technical 技术信息"));

        return rows.ToArray();
    }

    // ------------------------------------------------------------ row builders

    private static SettingsRowDefinition BoolRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        Func<AppSettings, bool> get,
        Action<AppSettings, bool> set,
        string keywords = "",
        string? unavailable = null) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Toggle,
            UnavailableReason = unavailable,
            Get = settings => get(settings),
            Set = (settings, value) => set(settings, value is bool flag && flag),
        };

    private static SettingsRowDefinition ChoiceRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        IReadOnlyList<string> choices,
        Func<AppSettings, int> get,
        Action<AppSettings, int> set,
        string keywords = "",
        string? unavailable = null) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Choice,
            Choices = choices,
            UnavailableReason = unavailable,
            Get = settings => get(settings),
            Set = (settings, value) => set(
                settings,
                Math.Clamp(ToInt(value), 0, Math.Max(0, choices.Count - 1))),
        };

    private static SettingsRowDefinition NumberRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        double minimum,
        double maximum,
        double step,
        string suffix,
        Func<AppSettings, double> get,
        Action<AppSettings, double> set,
        string keywords = "",
        string? unavailable = null) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Number,
            Minimum = minimum,
            Maximum = maximum,
            Step = step,
            Suffix = suffix,
            UnavailableReason = unavailable,
            Get = settings => get(settings),
            Set = (settings, value) => set(
                settings,
                Math.Clamp(ToDouble(value), minimum, maximum)),
        };

    private static SettingsRowDefinition TextRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        Func<AppSettings, string> get,
        Action<AppSettings, string> set,
        string keywords = "",
        string? unavailable = null) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Text,
            UnavailableReason = unavailable,
            Get = settings => get(settings),
            Set = (settings, value) => set(settings, value as string ?? string.Empty),
        };

    private static SettingsRowDefinition ActionRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        string actionId,
        string keywords = "",
        Func<AppSettings, string>? value = null,
        string? unavailable = null) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Action,
            ActionId = actionId,
            UnavailableReason = unavailable,
            Get = value is null ? null : settings => value(settings),
        };

    private static SettingsRowDefinition InfoRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        string keywords,
        Func<AppSettings, string> value) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Info,
            Get = settings => value(settings),
        };

    private static SettingsRowDefinition FontRow(
        string id,
        SettingsPageId page,
        string section,
        string title,
        string description,
        Func<AppSettings, string> get,
        Action<AppSettings, string> set,
        string keywords) =>
        new()
        {
            Id = id,
            Page = page,
            Section = section,
            Title = title,
            Description = description,
            Keywords = keywords,
            Kind = SettingsRowKind.Font,
            Get = settings => get(settings),
            Set = (settings, value) => set(settings, value as string ?? string.Empty),
        };

    private static int ToInt(object? value) => value switch
    {
        int number => number,
        double number => (int)Math.Round(number),
        string text when int.TryParse(text, out int parsed) => parsed,
        _ => 0,
    };

    private static int IndexOf(
        string? value,
        IReadOnlyList<string> choices,
        int fallback)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Math.Clamp(fallback, 0, Math.Max(0, choices.Count - 1));
    }

    private static void ApplyEqualizerPreset(AppSettings settings, int value)
    {
        int presetCount = EqualizerBands.PresetNames.Count;
        if (value < 0 || value >= presetCount)
        {
            settings.EqualizerPreset = EqualizerBands.CustomPreset;
            return;
        }

        string name = EqualizerBands.PresetNames[value];
        settings.EqualizerPreset = name;
        settings.EqualizerGains = EqualizerBands.CreatePreset(name).ToList();
    }

    private static double ToDouble(object? value) => value switch
    {
        double number => number,
        int number => number,
        float number => number,
        string text when double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed) => parsed,
        _ => 0,
    };

    private static string GetScreenshotFolder(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ScreenshotFolder))
        {
            return settings.ScreenshotFolder;
        }

        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return System.IO.Path.Combine(pictures, "Wiina Screenshots");
    }
}
