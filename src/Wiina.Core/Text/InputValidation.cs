using System.Text;

namespace Wiina.Core.Text;

/// <summary>
/// URL normalization for the "Open URL" dialog and the command line. Kept in
/// Core so validation is unit-tested without WinUI.
/// </summary>
public static class UrlValidation
{
    private static readonly string[] AllowedSchemes =
    {
        "http", "https", "rtmp", "rtmps", "rtsp", "rtsps", "ftp", "sftp",
        "udp", "rtp", "tcp", "file", "mms", "mmsh", "ytdl",
    };

    public const int MaxLength = 2048;

    /// <summary>
    /// Validates a stream URL. Missing schemes get <c>https://</c>; schemes
    /// mpv cannot open are rejected instead of being stored.
    /// </summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        if (value.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        // Windows paths and UNC paths are files, not URLs; the file picker and
        // drag/drop handle those. A bare drive path is not a valid URL.
        if (value.Length >= 2 && value[1] == ':' && char.IsLetter(value[0]))
        {
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal) && LooksLikeScheme(value))
        {
            // "mailto:someone@example.com" and similar are not stream URLs;
            // only bare hosts may get the https:// default.
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            // Require a host-looking token before assuming https; "hello world"
            // must stay invalid rather than becoming https://hello world.
            if (!value.Contains('.', StringComparison.Ordinal) ||
                value.StartsWith('.') ||
                value.StartsWith('-') ||
                value.Any(char.IsWhiteSpace))
            {
                return false;
            }

            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = value;
        return true;
    }

    /// <summary>True for inputs the history should treat as network media.</summary>
    public static bool IsStreamUrl(string? text) =>
        TryNormalize(text, out string normalized) &&
        !normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeScheme(string value)
    {
        int colon = value.IndexOf(':');
        if (colon <= 0 || !char.IsLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < colon; index++)
        {
            char character = value[index];
            if (!char.IsLetterOrDigit(character) &&
                character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Validation/normalization for the subtitle font family name.</summary>
public static class FontNameValidation
{
    public const int MaxLength = 128;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Segoe UI";
        }

        var builder = new StringBuilder(Math.Min(value.Length, MaxLength));
        bool previousSpace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsControl(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousSpace = true;
                continue;
            }

            previousSpace = false;
            builder.Append(character);
            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        string normalized = builder.ToString().Trim();
        return normalized.Length == 0 ? "Segoe UI" : normalized;
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = Normalize(value);
        return !string.IsNullOrWhiteSpace(value) &&
            value.Trim().Length <= MaxLength;
    }
}
