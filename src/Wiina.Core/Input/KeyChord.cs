using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Wiina.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Windows = 1 << 3,
}

/// <summary>
/// WinUI-independent key vocabulary. Only keys that Wiina binds are listed;
/// the App maps Windows.System.VirtualKey onto this enum.
/// </summary>
public enum AppKey
{
    Unknown = 0,
    Space,
    Tab,
    Enter,
    Escape,
    Backspace,
    Delete,
    Insert,
    Home,
    End,
    PageUp,
    PageDown,
    Left,
    Right,
    Up,
    Down,
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Comma,
    Period,
    Minus,
    Plus,
    OpenBracket,
    CloseBracket,
    Semicolon,
    Quote,
    Slash,
    Backslash,
    Tilde,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    MediaStop,
    MediaVolumeUp,
    MediaVolumeDown,
    MediaMute,
    MediaForward,
    MediaRewind,
    MediaPower,
    MediaClose,
}

/// <summary>
/// A normalized key combination such as <c>Ctrl+Shift+O</c>. Parsing and
/// formatting live in Core so the key table can be tested without WinUI.
/// </summary>
public readonly record struct KeyChord(AppKey Key, KeyModifiers Modifiers)
{
    public bool IsEmpty => Key == AppKey.Unknown;

    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out KeyChord chord) =>
        TryParseCore(text, mpvStyle: false, out chord);

    /// <summary>
    /// Parses an mpv/input.conf style chord. In mpv syntax a bare uppercase
    /// letter implies Shift ("P" means Shift+p) and punctuation such as
    /// <c>&gt;</c>, <c>(</c> or <c>_</c> implies the Shift key needed to type it.
    /// </summary>
    public static bool TryParseMpv(
        string? text,
        [NotNullWhen(true)] out KeyChord chord) =>
        TryParseCore(text, mpvStyle: true, out chord);

    private static bool TryParseCore(
        string? text,
        bool mpvStyle,
        [NotNullWhen(true)] out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        KeyModifiers modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt":
                case "option":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= KeyModifiers.Windows;
                    break;
                default:
                    return false;
            }
        }

        string keyToken = parts[^1];
        if (mpvStyle && keyToken.Length == 1 && keyToken[0] is >= 'A' and <= 'Z')
        {
            modifiers |= KeyModifiers.Shift;
            keyToken = keyToken.ToLowerInvariant();
        }

        if (!TryParseKey(keyToken, out AppKey key, out KeyModifiers impliedModifiers))
        {
            return false;
        }

        chord = new KeyChord(key, modifiers | impliedModifiers);
        return true;
    }

    public static KeyChord Parse(string text)
    {
        if (!TryParse(text, out KeyChord chord))
        {
            throw new FormatException($"Unknown key chord: {text}");
        }

        return chord;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control))
        {
            builder.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            builder.Append("Alt+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            builder.Append("Shift+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Windows))
        {
            builder.Append("Win+");
        }

        builder.Append(FormatKey(Key));
        return builder.ToString();
    }

    private static bool TryParseKey(
        string token,
        out AppKey key,
        out KeyModifiers impliedModifiers)
    {
        key = AppKey.Unknown;
        impliedModifiers = KeyModifiers.None;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // Shifted punctuation typed on a US layout. The chord names the resulting
        // character, so record the Shift modifier that produces it.
        switch (token)
        {
            case ">":
                key = AppKey.Period;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "<":
                key = AppKey.Comma;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "(":
                key = AppKey.D9;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case ")":
                key = AppKey.D0;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "_":
                key = AppKey.Minus;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "{":
                key = AppKey.OpenBracket;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "}":
                key = AppKey.CloseBracket;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case ":":
                key = AppKey.Semicolon;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "\"":
                key = AppKey.Quote;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "?":
                key = AppKey.Slash;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "|":
                key = AppKey.Backslash;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "!":
                key = AppKey.D1;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "@":
                key = AppKey.D2;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "#":
                key = AppKey.D3;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "$":
                key = AppKey.D4;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "%":
                key = AppKey.D5;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "^":
                key = AppKey.D6;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "&":
                key = AppKey.D7;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "*":
                key = AppKey.D8;
                impliedModifiers = KeyModifiers.Shift;
                return true;
            case "~":
                key = AppKey.Tilde;
                impliedModifiers = KeyModifiers.Shift;
                return true;
        }

        string lowered = token.ToLowerInvariant();
        key = lowered switch
        {
            "space" => AppKey.Space,
            "tab" => AppKey.Tab,
            "enter" or "return" => AppKey.Enter,
            "esc" or "escape" => AppKey.Escape,
            "backspace" or "back" => AppKey.Backspace,
            "delete" or "del" => AppKey.Delete,
            "insert" or "ins" => AppKey.Insert,
            "home" => AppKey.Home,
            "end" => AppKey.End,
            "pageup" or "pgup" => AppKey.PageUp,
            "pagedown" or "pgdn" or "pgdwn" => AppKey.PageDown,
            "left" or "arrowleft" => AppKey.Left,
            "right" or "arrowright" => AppKey.Right,
            "up" or "arrowup" => AppKey.Up,
            "down" or "arrowdown" => AppKey.Down,
            "," => AppKey.Comma,
            "comma" => AppKey.Comma,
            "." => AppKey.Period,
            "period" => AppKey.Period,
            "-" => AppKey.Minus,
            "minus" => AppKey.Minus,
            "+" or "=" => AppKey.Plus,
            "plus" => AppKey.Plus,
            "[" => AppKey.OpenBracket,
            "openbracket" => AppKey.OpenBracket,
            "]" => AppKey.CloseBracket,
            "closebracket" => AppKey.CloseBracket,
            ";" => AppKey.Semicolon,
            "semicolon" => AppKey.Semicolon,
            "'" => AppKey.Quote,
            "quote" => AppKey.Quote,
            "/" => AppKey.Slash,
            "slash" => AppKey.Slash,
            "\\" => AppKey.Backslash,
            "backslash" => AppKey.Backslash,
            "`" => AppKey.Tilde,
            "tilde" => AppKey.Tilde,
            "mediaplaypause" or "playpause" or "play" or "pause" or "playonly" or "pauseonly" =>
                AppKey.MediaPlayPause,
            "medianext" or "next" => AppKey.MediaNext,
            "mediaprevious" or "mediaprev" or "prev" => AppKey.MediaPrevious,
            "mediastop" or "stop" => AppKey.MediaStop,
            "forward" => AppKey.MediaForward,
            "rewind" => AppKey.MediaRewind,
            "volume_up" or "volumeup" or "volup" => AppKey.MediaVolumeUp,
            "volume_down" or "volumedown" or "voldown" => AppKey.MediaVolumeDown,
            "mute" => AppKey.MediaMute,
            "power" => AppKey.MediaPower,
            "close_win" or "closewin" => AppKey.MediaClose,
            _ => ParseLetterDigitOrFunction(lowered),
        };

        return key != AppKey.Unknown;
    }

    private static AppKey ParseLetterDigitOrFunction(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return (AppKey)((int)AppKey.A + (c - 'A'));
            }

            if (c is >= '0' and <= '9')
            {
                return (AppKey)((int)AppKey.D0 + (c - '0'));
            }
        }

        if (token.Length is 2 or 3 && token[0] == 'f' &&
            int.TryParse(token.AsSpan(1), out int number) &&
            number is >= 1 and <= 12)
        {
            return (AppKey)((int)AppKey.F1 + number - 1);
        }

        return Enum.TryParse(token, ignoreCase: true, out AppKey parsed)
            ? parsed
            : AppKey.Unknown;
    }

    private static string FormatKey(AppKey key) => key switch
    {
        AppKey.Space => "Space",
        AppKey.Escape => "Esc",
        AppKey.Backspace => "Backspace",
        AppKey.Enter => "Enter",
        AppKey.PageUp => "PageUp",
        AppKey.PageDown => "PageDown",
        AppKey.Comma => ",",
        AppKey.Period => ".",
        AppKey.Minus => "-",
        AppKey.Plus => "Plus",
        AppKey.OpenBracket => "[",
        AppKey.CloseBracket => "]",
        AppKey.Semicolon => ";",
        AppKey.Quote => "'",
        AppKey.Slash => "/",
        AppKey.Backslash => "\\",
        AppKey.Tilde => "`",
        >= AppKey.A and <= AppKey.Z =>
            ((char)('A' + ((int)key - (int)AppKey.A))).ToString(),
        >= AppKey.D0 and <= AppKey.D9 =>
            ((char)('0' + ((int)key - (int)AppKey.D0))).ToString(),
        >= AppKey.F1 and <= AppKey.F12 =>
            "F" + ((int)key - (int)AppKey.F1 + 1),
        _ => key.ToString(),
    };
}
