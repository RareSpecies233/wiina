using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wiina.Core.Input;

/// <summary>One persisted key binding (action id + normalized chord text).</summary>
public sealed class KeyBindingSetting
{
    public string Action { get; set; } = string.Empty;

    public string Chord { get; set; } = string.Empty;

    public KeyBindingSetting()
    {
    }

    public KeyBindingSetting(string action, string chord)
    {
        Action = action;
        Chord = chord;
    }
}

/// <summary>Display metadata for one bindable player action.</summary>
public sealed record PlayerActionInfo(string Action, string Label, string Category);

/// <summary>
/// The complete list of actions the key editor can bind, with the Chinese
/// labels shared by the editor, the menu and the legacy settings dialog.
/// </summary>
public static class PlayerActionCatalog
{
    private static readonly PlayerActionInfo[] Items =
    {
        new(PlayerActions.PlayPause, "播放 / 暂停", "播放"),
        new(PlayerActions.Stop, "停止", "播放"),
        new(PlayerActions.SeekBack5, "后退 5 秒", "跳转"),
        new(PlayerActions.SeekForward5, "前进 5 秒", "跳转"),
        new(PlayerActions.SeekBack1, "后退 1 秒", "跳转"),
        new(PlayerActions.SeekForward1, "前进 1 秒", "跳转"),
        new(PlayerActions.FramePrevious, "上一帧", "跳转"),
        new(PlayerActions.FrameNext, "下一帧", "跳转"),
        new(PlayerActions.ChapterPrevious, "上一章", "跳转"),
        new(PlayerActions.ChapterNext, "下一章", "跳转"),
        new(PlayerActions.PlaylistPrevious, "播放列表上一首", "播放"),
        new(PlayerActions.PlaylistNext, "播放列表下一首", "播放"),
        new(PlayerActions.VolumeUp5, "音量 +5", "音量"),
        new(PlayerActions.VolumeDown5, "音量 -5", "音量"),
        new(PlayerActions.VolumeUp1, "音量 +1", "音量"),
        new(PlayerActions.VolumeDown1, "音量 -1", "音量"),
        new(PlayerActions.Mute, "静音", "音量"),
        new(PlayerActions.SpeedHalf, "速度 ×0.5", "播放"),
        new(PlayerActions.SpeedDouble, "速度 ×2", "播放"),
        new(PlayerActions.SpeedReset, "速度复位", "播放"),
        new(PlayerActions.AbLoop, "A-B 循环", "播放"),
        new(PlayerActions.LoopMode, "循环模式", "播放"),
        new(PlayerActions.SubtitleVisibility, "字幕显隐", "字幕"),
        new(PlayerActions.SubtitleDelayDecrease, "字幕延迟 -0.1", "字幕"),
        new(PlayerActions.SubtitleDelayIncrease, "字幕延迟 +0.1", "字幕"),
        new(PlayerActions.SubtitleDelayReset, "字幕延迟归零", "字幕"),
        new(PlayerActions.SubtitleScaleIncrease, "字幕放大", "字幕"),
        new(PlayerActions.SubtitleScaleDecrease, "字幕缩小", "字幕"),
        new(PlayerActions.SubtitlePositionUp, "字幕位置上移", "字幕"),
        new(PlayerActions.SubtitlePositionDown, "字幕位置下移", "字幕"),
        new(PlayerActions.SubtitleSeekPrevious, "上一句字幕", "字幕"),
        new(PlayerActions.SubtitleSeekNext, "下一句字幕", "字幕"),
        new(PlayerActions.Open, "打开文件", "文件"),
        new(PlayerActions.OpenUrl, "打开 URL", "文件"),
        new(PlayerActions.Settings, "设置", "文件"),
        new(PlayerActions.Screenshot, "截图", "文件"),
        new(PlayerActions.NewWindow, "新建窗口", "窗口"),
        new(PlayerActions.OpenInNewWindow, "在新窗口打开", "窗口"),
        new(PlayerActions.CloseWindow, "关闭当前窗口", "窗口"),
        new(PlayerActions.Quit, "退出", "窗口"),
        new(PlayerActions.ToggleFullscreen, "全屏", "窗口"),
        new(PlayerActions.ExitFullscreen, "退出全屏", "窗口"),
        new(PlayerActions.PictureInPicture, "画中画", "窗口"),
        // 音乐模式/迷你播放器 has no bindable action: the mode is reachable only
        // from 设置 → 高级 → 模式, so it must not appear in the shortcut editor.
        new(PlayerActions.Sidebar, "侧边栏", "界面"),
        new(PlayerActions.SidebarSettings, "快速设置侧栏", "界面"),
        new(PlayerActions.Layout, "布局切换", "界面"),
        new(PlayerActions.Theme, "主题切换", "界面"),
        new(PlayerActions.OscPosition, "OSC 位置", "界面"),
        new(PlayerActions.WindowHalf, "窗口 0.5×", "窗口"),
        new(PlayerActions.WindowNormal, "窗口 1×", "窗口"),
        new(PlayerActions.WindowDouble, "窗口 2×", "窗口"),
        new(PlayerActions.WindowShrink, "缩小窗口", "窗口"),
        new(PlayerActions.WindowGrow, "放大窗口", "窗口"),
        new(PlayerActions.Pause, "暂停", "播放"),
        new(PlayerActions.Resume, "继续播放", "播放"),
        new(PlayerActions.SpeedSlower, "速度 ÷1.1", "播放"),
        new(PlayerActions.SpeedFaster, "速度 ×1.1", "播放"),
        new(PlayerActions.CycleVideo, "切换视频轨", "播放"),
        new(PlayerActions.CycleAudio, "切换音频轨", "播放"),
        new(PlayerActions.CycleSubtitle, "切换字幕轨", "播放"),
        new(PlayerActions.CycleEdition, "切换版本", "播放"),
        new(PlayerActions.SeekBack15, "后退 15 秒", "跳转"),
        new(PlayerActions.SeekForward15, "前进 15 秒", "跳转"),
        new(PlayerActions.SeekBack600, "后退 10 分钟", "跳转"),
        new(PlayerActions.SeekForward600, "前进 10 分钟", "跳转"),
        new(PlayerActions.VolumeUp2, "音量 +2", "音量"),
        new(PlayerActions.VolumeDown2, "音量 -2", "音量"),
        new(PlayerActions.AudioDelayIncrease05, "音频延迟 +0.5", "音频"),
        new(PlayerActions.AudioDelayDecrease05, "音频延迟 -0.5", "音频"),
        new(PlayerActions.AudioDelayIncrease01, "音频延迟 +0.1", "音频"),
        new(PlayerActions.AudioDelayDecrease01, "音频延迟 -0.1", "音频"),
        new(PlayerActions.AudioDelayReset, "音频延迟归零", "音频"),
        new(PlayerActions.SubtitleDelayDecrease05, "字幕延迟 -0.5", "字幕"),
        new(PlayerActions.SubtitleDelayIncrease05, "字幕延迟 +0.5", "字幕"),
        new(PlayerActions.SecondarySubtitleVisibility, "第二字幕显隐", "字幕"),
        new(PlayerActions.EnterFullscreen, "进入全屏", "窗口"),
        new(PlayerActions.ToggleTopmost, "窗口置顶", "窗口"),
        new(PlayerActions.FitToScreen, "适应画面", "窗口"),
        new(PlayerActions.ShowProgress, "显示进度", "界面"),
        new(PlayerActions.VideoPanel, "视频面板", "界面"),
        new(PlayerActions.AudioPanel, "音频面板", "界面"),
        new(PlayerActions.SubtitlesPanel, "字幕面板", "界面"),
        new(PlayerActions.PlaylistPanel, "播放列表面板", "界面"),
        new(PlayerActions.ChapterPanel, "章节面板", "界面"),
    };

    private static readonly Dictionary<string, PlayerActionInfo> ByAction =
        Items.ToDictionary(item => item.Action, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PlayerActionInfo> All => Items;

    public static IReadOnlyList<string> Categories =>
        Items.Select(item => item.Category).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsKnown(string? action) =>
        !string.IsNullOrWhiteSpace(action) &&
        (ByAction.ContainsKey(action) || IsRawCommand(action));

    /// <summary>True when the action is a raw <c>mpv:&lt;command&gt;</c> binding.</summary>
    public static bool IsRawCommand(string? action) =>
        action is not null &&
        action.StartsWith("mpv:", StringComparison.Ordinal) &&
        action.Length > 4;

    /// <summary>Human-readable mpv command text for a raw action, or null.</summary>
    public static string? RawCommand(string? action) =>
        IsRawCommand(action) ? action![4..] : null;

    public static string Label(string? action)
    {
        if (RawCommand(action) is { } command)
        {
            return "mpv 命令：" + command;
        }

        return action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info.Label
            : "未知动作";
    }

    public static string CategoryOf(string? action)
    {
        if (IsRawCommand(action))
        {
            return "mpv 命令";
        }

        return action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info.Category
            : "其他";
    }

    public static PlayerActionInfo? Find(string? action) =>
        action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info
            : null;
}

/// <summary>Picker/editor profiles for the key map.</summary>
/// <summary>A named, user-editable key-binding profile.</summary>
public sealed class KeyBindingUserProfile
{
    public string Name { get; set; } = string.Empty;

    public List<KeyBindingSetting> Bindings { get; set; } = new();
}

public static class KeyBindingProfiles
{
    /// <summary>The shipped default profile, generated from wiina-defaults.conf.</summary>
    public const string Wiina = "Wiina";

    /// <summary>Optional plain mpv profile.</summary>
    public const string Mpv = "mpv";

    /// <summary>Virtual name used by the editor for user-created profiles.</summary>
    public const string Custom = "Custom";

    /// <summary>
    /// The active profile when none is stored. Kept as <c>Default</c> so existing
    /// settings/JSON keep compiling; its value is the Wiina profile.
    /// </summary>
    public const string Default = Wiina;

    /// <summary>
    /// Built-in profiles. "Custom" is intentionally absent: custom profiles are
    /// named user records and can be renamed or deleted.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[] { Wiina, Mpv };

    /// <summary>Normalizes persisted/user values; legacy names map to the new ids.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        string trimmed = value.Trim();
        if (Matches(trimmed, Wiina) ||
            Matches(trimmed, "Default") ||
            Matches(trimmed, "默认") ||
            Matches(trimmed, "Wiina 默认"))
        {
            return Wiina;
        }

        if (Matches(trimmed, Mpv) || Matches(trimmed, "mpv 默认"))
        {
            return Mpv;
        }

        // The IINA profile was removed in 1.3.1. Persisted values from older
        // builds migrate to the Wiina profile so the label never reappears.
        if (Matches(trimmed, "IINA") || Matches(trimmed, "IINA 默认"))
        {
            return Wiina;
        }

        // Note: "自定义" is deliberately *not* aliased. Older builds created
        // real user profiles with that name, so treating it as a virtual id
        // would make them unselectable in the profile picker.
        return trimmed;
    }

    private static bool Matches(string value, string candidate) =>
        string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? value) => !string.IsNullOrWhiteSpace(value);

    public static string Label(string? value) =>
        Normalize(value) switch
        {
            Wiina => "Wiina 默认",
            Mpv => "mpv",
            Custom => "自定义",
            var custom => custom,
        };

    public static string LabelEnglish(string? value) =>
        Normalize(value) switch
        {
            Wiina => "Wiina Default",
            Mpv => "mpv",
            Custom => "Custom",
            var custom => custom,
        };
}

/// <summary>
/// Pure key-binding model: defaults, conflict detection, JSON import/export and
/// conversion to the runtime <see cref="KeyMap"/>. The editor and tests use
/// this class instead of touching <see cref="KeyMap"/> directly.
/// </summary>
public static class KeyBindings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The "Wiina 默认" profile: the shipped wiina-defaults.conf, so the desktop
    /// player starts with the same bindings as the author's mpv setup.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> CreateDefaults() =>
        WiinaInputDefaults.Create();

    /// <summary>
    /// Plain mpv profile: mpv's stock input.conf conventions, kept as an optional
    /// alternative to the Wiina profile.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> CreateMpvDefaults() =>
        new List<KeyBindingSetting>
        {
            new(PlayerActions.PlayPause, "Space"),
            new(PlayerActions.PlayPause, "P"),
            new(PlayerActions.SeekBack5, "Left"),
            new(PlayerActions.SeekForward5, "Right"),
            new(PlayerActions.SeekBack1, "Shift+Left"),
            new(PlayerActions.SeekForward1, "Shift+Right"),
            new(PlayerActions.SeekForward600, "Up"),
            new(PlayerActions.SeekBack600, "Down"),
            new(PlayerActions.VolumeDown2, "9"),
            new(PlayerActions.VolumeUp2, "0"),
            new(PlayerActions.Mute, "M"),
            new(PlayerActions.Quit, "Q"),
            new(PlayerActions.ToggleFullscreen, "F"),
            new(PlayerActions.SubtitleVisibility, "V"),
            new(PlayerActions.ShowProgress, "O"),
            new(PlayerActions.Screenshot, "S"),
            new(PlayerActions.FramePrevious, ","),
            new(PlayerActions.FrameNext, "."),
        };

    /// <summary>Defaults for a profile.</summary>
    public static IReadOnlyList<KeyBindingSetting> CreateProfileDefaults(string? profile) =>
        KeyBindingProfiles.Normalize(profile) switch
        {
            KeyBindingProfiles.Mpv => CreateMpvDefaults(),
            _ => CreateDefaults(),
        };

    /// <summary>Defaults per action (used by the per-action reset button).</summary>
    public static IReadOnlyList<string> DefaultChords(string action) =>
        CreateDefaults()
            .Where(binding => string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.Chord)
            .ToArray();

    /// <summary>Defaults per action within the selected profile.</summary>
    public static IReadOnlyList<string> DefaultChords(string action, string? profile) =>
        CreateProfileDefaults(profile)
            .Where(binding => string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.Chord)
            .ToArray();

    /// <summary>
    /// Expands stored bindings into a complete list. An empty/absent list means
    /// "documented defaults"; entries with unknown actions or invalid chords are
    /// dropped so a hand-edited settings.json cannot break input.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> Resolve(
        IEnumerable<KeyBindingSetting>? stored) =>
        ResolveStored(stored);

    /// <summary>
    /// Resolves the active map for a profile. The built-in profiles are fixed,
    /// complete presets; custom profiles use the stored bindings (or the shipped
    /// defaults when nothing valid is stored).
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> Resolve(
        IEnumerable<KeyBindingSetting>? stored,
        string? profile)
    {
        string normalizedProfile = KeyBindingProfiles.Normalize(profile);
        if (normalizedProfile == KeyBindingProfiles.Wiina)
        {
            return CreateDefaults();
        }

        if (normalizedProfile == KeyBindingProfiles.Mpv)
        {
            return CreateMpvDefaults();
        }

        return ResolveStored(stored);
    }

    private static IReadOnlyList<KeyBindingSetting> ResolveStored(
        IEnumerable<KeyBindingSetting>? stored)
    {
        var resolved = new List<KeyBindingSetting>();
        bool any = false;
        if (stored is not null)
        {
            foreach (KeyBindingSetting item in stored)
            {
                if (!PlayerActionCatalog.IsKnown(item.Action) ||
                    NormalizeChord(item.Chord) is not { } chord)
                {
                    continue;
                }

                any = true;
                resolved.Add(new KeyBindingSetting(item.Action, chord));
            }
        }

        return any ? resolved : CreateDefaults();
    }

    /// <summary>Builds a lookup map; later entries win for duplicate chords.</summary>
    public static KeyMap BuildKeyMap(IEnumerable<KeyBindingSetting>? stored) =>
        BuildKeyMapFrom(ResolveStored(stored));

    /// <summary>Builds a lookup map for the selected profile.</summary>
    public static KeyMap BuildKeyMap(IEnumerable<KeyBindingSetting>? stored, string? profile) =>
        BuildKeyMapFrom(Resolve(stored, profile));

    private static KeyMap BuildKeyMapFrom(IEnumerable<KeyBindingSetting> bindings)
    {
        var map = new KeyMap();
        foreach (KeyBindingSetting binding in bindings)
        {
            if (KeyChord.TryParse(binding.Chord, out KeyChord chord))
            {
                map.Bind(chord, binding.Action);
            }
        }

        return map;
    }

    /// <summary>
    /// Returns the first chord bound to each action for the active profile.
    /// The menu uses this so accelerator text follows the effective custom
    /// bindings instead of the static catalog text.
    /// </summary>
    public static IReadOnlyDictionary<string, KeyChord> PrimaryChords(
        IEnumerable<KeyBindingSetting>? stored,
        string? profile)
    {
        var result = new Dictionary<string, KeyChord>(StringComparer.Ordinal);
        foreach (KeyBindingSetting binding in Resolve(stored, profile))
        {
            if (result.ContainsKey(binding.Action) ||
                !KeyChord.TryParse(binding.Chord, out KeyChord chord) ||
                chord.IsEmpty)
            {
                continue;
            }

            result[binding.Action] = chord;
        }

        return result;
    }

    /// <summary>
    /// Returns the other action already using <paramref name="chord"/> or null.
    /// Passing <paramref name="exceptAction"/> ignores that action's own rows.
    /// </summary>
    public static string? FindConflict(
        IEnumerable<KeyBindingSetting> bindings,
        string? chord,
        string? exceptAction = null)
    {
        if (NormalizeChord(chord) is not { } normalized)
        {
            return null;
        }

        foreach (KeyBindingSetting binding in bindings)
        {
            if (exceptAction is not null &&
                string.Equals(binding.Action, exceptAction, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(NormalizeChord(binding.Chord), normalized, StringComparison.Ordinal))
            {
                return binding.Action;
            }
        }

        return null;
    }

    /// <summary>
    /// Binds <paramref name="chord"/> to <paramref name="action"/>, removing the
    /// chord from any other action and dropping the action's previous chords.
    /// A null/empty chord clears the action instead.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> SetBinding(
        IEnumerable<KeyBindingSetting> bindings,
        string action,
        string? chord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var result = bindings
            .Where(binding => !string.Equals(
                binding.Action,
                action,
                StringComparison.OrdinalIgnoreCase))
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();

        if (NormalizeChord(chord) is not { } normalized)
        {
            return result;
        }

        result.RemoveAll(binding => string.Equals(
            NormalizeChord(binding.Chord),
            normalized,
            StringComparison.Ordinal));
        result.Add(new KeyBindingSetting(action, normalized));
        return result;
    }

    public static IReadOnlyList<KeyBindingSetting> ClearAction(
        IEnumerable<KeyBindingSetting> bindings,
        string action) =>
        bindings
            .Where(binding => !string.Equals(
                binding.Action,
                action,
                StringComparison.OrdinalIgnoreCase))
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();

    /// <summary>Removes the action's rows and restores its documented defaults.</summary>
    public static IReadOnlyList<KeyBindingSetting> ResetAction(
        IEnumerable<KeyBindingSetting> bindings,
        string action)
    {
        var result = ClearAction(bindings, action).ToList();
        foreach (string chord in DefaultChords(action))
        {
            if (FindConflict(result, chord, action) is null)
            {
                result.Add(new KeyBindingSetting(action, chord));
            }
        }

        return result;
    }

    /// <summary>Resolves duplicates (first wins) and returns the editable list.</summary>
    public static IReadOnlyList<KeyBindingSetting> Normalize(
        IEnumerable<KeyBindingSetting> bindings)
    {
        var result = new List<KeyBindingSetting>();
        foreach (KeyBindingSetting binding in bindings)
        {
            if (!PlayerActionCatalog.IsKnown(binding.Action) ||
                NormalizeChord(binding.Chord) is not { } chord ||
                FindConflict(result, chord) is not null)
            {
                continue;
            }

            result.Add(new KeyBindingSetting(binding.Action, chord));
        }

        return result;
    }

    /// <summary>Parses and canonicalizes a chord string ("ctrl+shift+p" → "Ctrl+Shift+P").</summary>
    public static string? NormalizeChord(string? chord) =>
        KeyChord.TryParse(chord, out KeyChord parsed) && !parsed.IsEmpty
            ? parsed.ToString()
            : null;

    public static string ExportJson(IEnumerable<KeyBindingSetting> bindings) =>
        JsonSerializer.Serialize(
            new KeyBindingFile
            {
                Version = 1,
                Bindings = Normalize(bindings)
                    .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
                    .ToList(),
            },
            JsonOptions);

    /// <summary>
    /// Imports an export file. Unknown actions are skipped (forward compatible);
    /// malformed JSON, an empty result or duplicate chord all fail with a reason.
    /// </summary>
    public static bool TryImportJson(
        string? json,
        out IReadOnlyList<KeyBindingSetting> bindings,
        out string error)
    {
        bindings = Array.Empty<KeyBindingSetting>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "文件为空。";
            return false;
        }

        KeyBindingFile? file;
        try
        {
            file = JsonSerializer.Deserialize<KeyBindingFile>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = "JSON 解析失败：" + ex.Message;
            return false;
        }

        if (file?.Bindings is null || file.Bindings.Count == 0)
        {
            error = "文件中没有绑定项。";
            return false;
        }

        var result = new List<KeyBindingSetting>();
        int skipped = 0;
        foreach (KeyBindingSetting binding in file.Bindings)
        {
            if (!PlayerActionCatalog.IsKnown(binding.Action) ||
                NormalizeChord(binding.Chord) is not { } chord)
            {
                skipped++;
                continue;
            }

            if (FindConflict(result, chord) is not null)
            {
                skipped++;
                continue;
            }

            result.Add(new KeyBindingSetting(binding.Action, chord));
        }

        if (result.Count == 0)
        {
            error = "文件中的绑定项全部无效。";
            return false;
        }

        bindings = result;
        if (skipped > 0)
        {
            error = $"已跳过 {skipped} 条无效或重复的绑定。";
        }

        return true;
    }

    private sealed class KeyBindingFile
    {
        public int Version { get; set; } = 1;

        public List<KeyBindingSetting> Bindings { get; set; } = new();
    }
}
