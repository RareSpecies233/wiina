using Windows.System;
using Wiina.Core.Input;

namespace Wiina.App.Services;

/// <summary>Maps WinUI <see cref="VirtualKey"/> values onto the Core key table.</summary>
internal static class KeyChordMapper
{
    public static bool TryMap(
        VirtualKey key,
        KeyModifiers modifiers,
        out KeyChord chord)
    {
        AppKey? mapped = MapKey(key);
        if (mapped is null)
        {
            chord = default;
            return false;
        }

        chord = new KeyChord(mapped.Value, modifiers);
        return true;
    }

    private static AppKey? MapKey(VirtualKey key)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return (AppKey)((int)AppKey.A + ((int)key - (int)VirtualKey.A));
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return (AppKey)((int)AppKey.D0 + ((int)key - (int)VirtualKey.Number0));
        }

        if (key is >= VirtualKey.F1 and <= VirtualKey.F12)
        {
            return (AppKey)((int)AppKey.F1 + ((int)key - (int)VirtualKey.F1));
        }

        // Windows.System.VirtualKey does not expose the OEM punctuation names,
        // so map the raw virtual-key codes (VK_OEM_*).
        return (int)key switch
        {
            173 => AppKey.MediaMute,
            174 => AppKey.MediaVolumeDown,
            175 => AppKey.MediaVolumeUp,
            176 => AppKey.MediaNext,
            177 => AppKey.MediaPrevious,
            178 => AppKey.MediaStop,
            179 => AppKey.MediaPlayPause,
            186 => AppKey.Semicolon,
            187 => AppKey.Plus,
            188 => AppKey.Comma,
            189 => AppKey.Minus,
            190 => AppKey.Period,
            191 => AppKey.Slash,
            192 => AppKey.Tilde,
            219 => AppKey.OpenBracket,
            220 => AppKey.Backslash,
            221 => AppKey.CloseBracket,
            222 => AppKey.Quote,
            226 => AppKey.Backslash,
            _ => MapNamedKey(key),
        };
    }

    private static AppKey? MapNamedKey(VirtualKey key)
    {
        return key switch
        {
            VirtualKey.Space => AppKey.Space,
            VirtualKey.Tab => AppKey.Tab,
            VirtualKey.Enter => AppKey.Enter,
            VirtualKey.Escape => AppKey.Escape,
            VirtualKey.Back => AppKey.Backspace,
            VirtualKey.Delete => AppKey.Delete,
            VirtualKey.Insert => AppKey.Insert,
            VirtualKey.Home => AppKey.Home,
            VirtualKey.End => AppKey.End,
            VirtualKey.PageUp => AppKey.PageUp,
            VirtualKey.PageDown => AppKey.PageDown,
            VirtualKey.Left => AppKey.Left,
            VirtualKey.Right => AppKey.Right,
            VirtualKey.Up => AppKey.Up,
            VirtualKey.Down => AppKey.Down,
            _ => null,
        };
    }
}
