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
        new(PlayerActions.MusicMode, "音乐模式", "模式"),
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
    };

    private static readonly Dictionary<string, PlayerActionInfo> ByAction =
        Items.ToDictionary(item => item.Action, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PlayerActionInfo> All => Items;

    public static IReadOnlyList<string> Categories =>
        Items.Select(item => item.Category).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsKnown(string? action) =>
        !string.IsNullOrWhiteSpace(action) && ByAction.ContainsKey(action);

    public static string Label(string? action) =>
        action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info.Label
            : "未知动作";

    public static string CategoryOf(string? action) =>
        action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info.Category
            : "其他";

    public static PlayerActionInfo? Find(string? action) =>
        action is not null && ByAction.TryGetValue(action, out PlayerActionInfo? info)
            ? info
            : null;
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

    /// <summary>Every documented default binding (one entry per chord).</summary>
    public static IReadOnlyList<KeyBindingSetting> CreateDefaults() =>
        new List<KeyBindingSetting>
        {
            new(PlayerActions.PlayPause, "Space"),
            new(PlayerActions.PlayPause, "P"),
            new(PlayerActions.Stop, "Ctrl+."),
            new(PlayerActions.SeekBack5, "Left"),
            new(PlayerActions.SeekForward5, "Right"),
            new(PlayerActions.SeekBack1, "Shift+Left"),
            new(PlayerActions.SeekForward1, "Shift+Right"),
            new(PlayerActions.VolumeUp5, "Up"),
            new(PlayerActions.VolumeDown5, "Down"),
            new(PlayerActions.VolumeUp1, "Ctrl+Up"),
            new(PlayerActions.VolumeDown1, "Ctrl+Down"),
            new(PlayerActions.Mute, "Ctrl+M"),
            new(PlayerActions.Mute, "M"),
            new(PlayerActions.FramePrevious, "Alt+Left"),
            new(PlayerActions.FrameNext, "Alt+Right"),
            new(PlayerActions.SubtitleSeekPrevious, "Ctrl+Shift+Left"),
            new(PlayerActions.SubtitleSeekNext, "Ctrl+Shift+Right"),
            new(PlayerActions.SpeedHalf, "["),
            new(PlayerActions.SpeedDouble, "]"),
            new(PlayerActions.SpeedReset, "Ctrl+Backspace"),
            new(PlayerActions.AbLoop, "Ctrl+L"),
            new(PlayerActions.ChapterPrevious, "Ctrl+PageUp"),
            new(PlayerActions.ChapterNext, "Ctrl+PageDown"),
            new(PlayerActions.LoopMode, "Ctrl+Shift+R"),
            new(PlayerActions.Open, "Ctrl+O"),
            new(PlayerActions.OpenUrl, "Ctrl+Shift+O"),
            new(PlayerActions.Screenshot, "Ctrl+S"),
            new(PlayerActions.Settings, "Ctrl+,"),
            new(PlayerActions.PlaylistPrevious, "Ctrl+Left"),
            new(PlayerActions.PlaylistNext, "Ctrl+Right"),
            new(PlayerActions.ToggleFullscreen, "F11"),
            new(PlayerActions.ExitFullscreen, "Esc"),
            new(PlayerActions.PictureInPicture, "Ctrl+Shift+P"),
            new(PlayerActions.PictureInPicture, "Ctrl+P"),
            new(PlayerActions.Sidebar, "Ctrl+Shift+B"),
            new(PlayerActions.SidebarSettings, "Ctrl+Shift+S"),
            new(PlayerActions.Layout, "Ctrl+Shift+L"),
            new(PlayerActions.MusicMode, "Ctrl+Shift+M"),
            new(PlayerActions.Theme, "Ctrl+Shift+T"),
            new(PlayerActions.OscPosition, "Ctrl+Alt+O"),
            new(PlayerActions.WindowHalf, "Ctrl+0"),
            new(PlayerActions.WindowNormal, "Ctrl+1"),
            new(PlayerActions.WindowDouble, "Ctrl+2"),
            new(PlayerActions.WindowShrink, "Ctrl+-"),
            new(PlayerActions.WindowGrow, "Ctrl+Plus"),
            new(PlayerActions.NewWindow, "Ctrl+N"),
            new(PlayerActions.OpenInNewWindow, "Ctrl+Shift+N"),
            new(PlayerActions.CloseWindow, "Ctrl+W"),
            new(PlayerActions.Quit, "Q"),
            new(PlayerActions.Quit, "Ctrl+Q"),
            new(PlayerActions.SubtitleVisibility, "V"),
            new(PlayerActions.SubtitleDelayDecrease, "Z"),
            new(PlayerActions.SubtitleDelayIncrease, "X"),
            new(PlayerActions.SubtitleDelayReset, "C"),
            new(PlayerActions.SubtitleScaleIncrease, "G"),
            new(PlayerActions.SubtitleScaleDecrease, "F"),
            new(PlayerActions.SubtitlePositionUp, "R"),
            new(PlayerActions.SubtitlePositionDown, "T"),
        };

    /// <summary>Defaults per action (used by the per-action reset button).</summary>
    public static IReadOnlyList<string> DefaultChords(string action) =>
        CreateDefaults()
            .Where(binding => string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.Chord)
            .ToArray();

    /// <summary>
    /// Expands stored bindings into a complete list. An empty/absent list means
    /// "documented defaults"; entries with unknown actions or invalid chords are
    /// dropped so a hand-edited settings.json cannot break input.
    /// </summary>
    public static IReadOnlyList<KeyBindingSetting> Resolve(
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
    public static KeyMap BuildKeyMap(IEnumerable<KeyBindingSetting>? stored)
    {
        var map = new KeyMap();
        foreach (KeyBindingSetting binding in Resolve(stored))
        {
            if (KeyChord.TryParse(binding.Chord, out KeyChord chord))
            {
                map.Bind(chord, binding.Action);
            }
        }

        return map;
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
