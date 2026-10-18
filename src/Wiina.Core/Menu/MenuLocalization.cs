using System.Globalization;
using System.Text.RegularExpressions;
using Wiina.Core.Localization;

namespace Wiina.Core.Menu;

/// <summary>
/// English overlay for the built menu tree. Menu labels are constructed
/// dynamically in <see cref="MenuCatalog"/> (check states, speed, loop mode),
/// so translation happens on the built tree by exact label plus a small set of
/// patterns. Ids stay stable and tests assert that an English build contains no
/// untranslated CJK labels for representative player states.
/// </summary>
public static class MenuLocalization
{
    private static readonly Dictionary<string, string> EnglishGroups = new(StringComparer.Ordinal)
    {
        ["file"] = "File",
        ["playback"] = "Playback",
        ["video"] = "Video",
        ["audio"] = "Audio",
        ["subtitles"] = "Subtitles",
        ["plugin"] = "Plugins",
        ["window"] = "Window",
        ["help"] = "Help",
    };

    private static readonly Dictionary<string, string> EnglishLabels = new(StringComparer.Ordinal)
    {
        ["打开文件…"] = "Open File…",
        ["打开文件夹…"] = "Open Folder…",
        ["打开 URL…"] = "Open URL…",
        ["在新窗口打开 URL…"] = "Open URL in New Window…",
        ["在新窗口打开…"] = "Open in New Window…",
        ["打开播放列表…"] = "Open Playlist…",
        ["打开最近"] = "Open Recent",
        ["保存播放列表…"] = "Save Playlist…",
        ["截图到文件夹…"] = "Screenshot to Folder…",
        ["关闭窗口"] = "Close Window",
        ["退出"] = "Quit",
        ["暂停"] = "Pause",
        ["继续播放"] = "Resume",
        ["停止"] = "Stop",
        ["停止并清空播放列表"] = "Stop and Clear Playlist",
        ["后退 5 秒"] = "Back 5 Seconds",
        ["前进 5 秒"] = "Forward 5 Seconds",
        ["上一帧"] = "Previous Frame",
        ["下一帧"] = "Next Frame",
        ["跳到开头"] = "Jump to Start",
        ["重置 1×"] = "Reset to 1×",
        ["截图"] = "Screenshot",
        ["A-B 循环"] = "A-B Loop",
        ["循环模式"] = "Loop Mode",
        ["关闭"] = "Off",
        ["列表循环"] = "Loop Playlist",
        ["单曲循环"] = "Loop One",
        ["随机播放"] = "Shuffle",
        ["上一首"] = "Previous",
        ["下一首"] = "Next",
        ["显示播放列表"] = "Show Playlist",
        ["显示章节"] = "Show Chapters",
        ["上一章"] = "Previous Chapter",
        ["下一章"] = "Next Chapter",
        ["显示视频面板"] = "Show Video Panel",
        ["视频轨道"] = "Video Tracks",
        ["0.5× 窗口"] = "0.5× Window",
        ["1× 窗口"] = "1× Window",
        ["2× 窗口"] = "2× Window",
        ["适合屏幕"] = "Fit to Screen",
        ["放大窗口"] = "Enlarge Window",
        ["缩小窗口"] = "Shrink Window",
        ["退出画中画"] = "Exit Picture-in-Picture",
        ["画中画"] = "Picture-in-Picture",
        ["退出全屏"] = "Exit Fullscreen",
        ["进入全屏"] = "Enter Fullscreen",
        ["窗口置顶"] = "Always on Top",
        ["锁定宽高比"] = "Lock Aspect Ratio",
        ["退出音乐模式"] = "Exit Music Mode",
        ["音乐模式"] = "Music Mode",
        ["宽高比"] = "Aspect Ratio",
        ["裁剪"] = "Crop",
        ["旋转"] = "Rotate",
        ["水平翻转"] = "Flip Horizontal",
        ["垂直翻转"] = "Flip Vertical",
        ["去隔行"] = "Deinterlace",
        ["视频滤镜…"] = "Video Filters…",
        ["显示音频面板"] = "Show Audio Panel",
        ["音频轨道"] = "Audio Tracks",
        ["音量 +5%"] = "Volume +5%",
        ["音量 -5%"] = "Volume -5%",
        ["音量 +1%"] = "Volume +1%",
        ["音量 -1%"] = "Volume -1%",
        ["静音"] = "Mute",
        ["音频延迟 -0.1s"] = "Audio Delay -0.1s",
        ["音频延迟 +0.1s"] = "Audio Delay +0.1s",
        ["重置音频延迟"] = "Reset Audio Delay",
        ["音频设备"] = "Audio Device",
        ["音频滤镜…"] = "Audio Filters…",
        ["显示字幕面板"] = "Show Subtitle Panel",
        ["字幕轨道"] = "Subtitle Tracks",
        ["隐藏字幕"] = "Hide Subtitles",
        ["显示字幕"] = "Show Subtitles",
        ["次字幕"] = "Secondary Subtitles",
        ["加载外挂字幕…"] = "Load Subtitle…",
        ["在线字幕…"] = "Online Subtitles…",
        ["延迟 -0.1s"] = "Delay -0.1s",
        ["延迟 +0.1s"] = "Delay +0.1s",
        ["延迟归零"] = "Reset Delay",
        ["位置上移"] = "Move Up",
        ["位置下移"] = "Move Down",
        ["放大字幕"] = "Increase Subtitle Size",
        ["缩小字幕"] = "Decrease Subtitle Size",
        ["重置缩放"] = "Reset Scale",
        ["字幕字体…"] = "Subtitle Font…",
        ["字幕设置…"] = "Subtitle Settings…",
        ["插件管理…"] = "Manage Plugins…",
        ["安装插件…"] = "Install Plugin…",
        ["打开插件文件夹"] = "Open Plugin Folder",
        ["新建窗口"] = "New Window",
        ["最小化"] = "Minimize",
        ["最大化 / 还原"] = "Maximize / Restore",
        ["检查器…"] = "Inspector…",
        ["日志查看器…"] = "Log Viewer…",
        ["设置…"] = "Settings…",
        ["欢迎窗口"] = "Welcome",
        ["关于 Wiina"] = "About Wiina",
        ["键盘快捷键…"] = "Keyboard Shortcuts…",
        ["打开日志文件夹"] = "Open Log Folder",
        ["导出调试信息"] = "Export Diagnostics",
        ["GitHub 项目主页"] = "GitHub Project Page",
        ["自动"] = "Auto",
        ["无"] = "None",
        ["在线字幕（OpenSubtitles）在 Stage 10 提供。"] =
            "Online subtitles (OpenSubtitles) arrive in Stage 10.",
        ["插件系统在 Stage 9 提供。"] = "The plugin system arrives in Stage 9.",
        ["插件安装、权限与 Info.json 在 Stage 9 提供。"] =
            "Plugin installation, permissions, and Info.json arrive in Stage 9.",
    };

    private static readonly Regex SpeedPattern = new(
        "^速度：(?<value>[0-9.,]+)×$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when an English alternative exists for a label/reason.</summary>
    public static bool HasLabel(string text) => EnglishLabels.ContainsKey(text);

    public static IReadOnlyList<MenuGroupDefinition> Translate(
        IReadOnlyList<MenuGroupDefinition> groups,
        string? language)
    {
        if (AppLanguage.NormalizeTag(language) != AppLanguage.English)
        {
            return groups;
        }

        var translated = new List<MenuGroupDefinition>(groups.Count);
        foreach (MenuGroupDefinition group in groups)
        {
            string title = EnglishGroups.TryGetValue(group.Id, out string? groupTitle)
                ? groupTitle
                : group.Title;
            translated.Add(new MenuGroupDefinition(
                group.Id,
                title,
                group.Items.Select(TranslateItem).ToArray()));
        }

        return translated;
    }

    private static MenuItemDefinition TranslateItem(MenuItemDefinition item)
    {
        string label = TranslateLabel(item.Label);
        string? reason = item.DisabledReason is null
            ? null
            : TranslateLabel(item.DisabledReason);
        return item with
        {
            Label = label,
            DisabledReason = reason,
            Children = item.Children.Select(TranslateItem).ToArray(),
        };
    }

    /// <summary>
    /// Translates one label. Unknown Chinese text is returned unchanged; the
    /// English catalog test lists any such labels so gaps are visible.
    /// </summary>
    public static string TranslateLabel(string label)
    {
        if (EnglishLabels.TryGetValue(label, out string? translated))
        {
            return translated;
        }

        Match speed = SpeedPattern.Match(label);
        if (speed.Success)
        {
            return $"Speed: {speed.Groups["value"].Value}×";
        }

        // Aspect/crop/rotation labels are numbers or language-neutral.
        if (label.All(ch => ch < 0x4E00 || ch > 0x9FFF))
        {
            return label;
        }

        return label;
    }
}
