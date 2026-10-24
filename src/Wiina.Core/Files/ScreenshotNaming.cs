namespace Wiina.Core.Files;

/// <summary>
/// Builds screenshot file names from the playing video and the capture
/// position, e.g. <c>xxx.mkv</c> at 1:28:39.980 becomes
/// <c>xxx-1-28-39-980.png</c>. Names are sanitised so the same scheme works on
/// every platform Wiina ships to, and collisions are numbered rather than
/// silently overwritten.
/// </summary>
public static class ScreenshotNaming
{
    /// <summary>Fallback prefix used when no video is loaded (picker path).</summary>
    public const string FallbackPrefix = "wiina";

    /// <summary>Hard cap for the media-name part of the generated name.</summary>
    public const int MaxMediaNameLength = 100;

    private const string Extension = ".png";

    /// <summary>
    /// Returns the screenshot file name for <paramref name="mediaPath"/> at
    /// <paramref name="positionSeconds"/>. Falls back to the legacy
    /// <c>wiina-yyyyMMdd-HHmmss.png</c> name when no media path is available.
    /// </summary>
    public static string BuildFileName(string? mediaPath, double positionSeconds, DateTimeOffset timestamp)
    {
        string stem = MediaStem(mediaPath);
        if (stem.Length == 0)
        {
            return $"{FallbackPrefix}-{timestamp:yyyyMMdd-HHmmss}{Extension}";
        }

        long milliseconds = PositionMilliseconds(positionSeconds);
        long hours = milliseconds / 3_600_000;
        long minutes = milliseconds / 60_000 % 60;
        long seconds = milliseconds / 1_000 % 60;
        long millis = milliseconds % 1_000;
        return $"{stem}-{hours}-{minutes:00}-{seconds:00}-{millis:000}{Extension}";
    }

    /// <summary>
    /// Same as <see cref="BuildFileName"/> but appends <c>-2</c>, <c>-3</c>, …
    /// until <paramref name="exists"/> reports the name is free.
    /// </summary>
    public static string BuildUniqueFileName(
        string? mediaPath,
        double positionSeconds,
        DateTimeOffset timestamp,
        Func<string, bool> exists,
        int maxAttempts = 1_000)
    {
        ArgumentNullException.ThrowIfNull(exists);

        string name = BuildFileName(mediaPath, positionSeconds, timestamp);
        if (!exists(name))
        {
            return name;
        }

        string stem = name[..^Extension.Length];
        for (int attempt = 2; attempt <= Math.Max(2, maxAttempts); attempt++)
        {
            string candidate = $"{stem}-{attempt}{Extension}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        // Give up on numbering after maxAttempts; overwriting is acceptable
        // there because a single position cannot produce that many captures.
        return $"{stem}-{maxAttempts}{Extension}";
    }

    /// <summary>
    /// Video file name without extension, sanitised for use in a file name.
    /// Returns an empty string when no usable media name exists.
    /// </summary>
    public static string MediaStem(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return string.Empty;
        }

        string name;
        try
        {
            name = Path.GetFileNameWithoutExtension(mediaPath);
        }
        catch (ArgumentException)
        {
            name = string.Empty;
        }

        string sanitised = Sanitise(name);
        if (sanitised.Length > MaxMediaNameLength)
        {
            sanitised = sanitised[..MaxMediaNameLength].TrimEnd(' ', '.', '-');
        }

        return sanitised;
    }

    /// <summary>
    /// Replaces every character that is not portable across Windows, macOS and
    /// Linux, then trims characters Windows rejects at the end of a name.
    /// </summary>
    public static string Sanitise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        Span<char> buffer = name.Length <= 256 ? stackalloc char[name.Length] : new char[name.Length];
        int length = 0;
        foreach (char ch in name)
        {
            bool invalid = char.IsControl(ch)
                || ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';
            buffer[length++] = invalid ? '-' : ch;
        }

        string result = new string(buffer[..length]).Trim();
        return result.TrimEnd(' ', '.');
    }

    private static long PositionMilliseconds(double positionSeconds)
    {
        if (!double.IsFinite(positionSeconds) || positionSeconds <= 0)
        {
            return 0;
        }

        // Round to the nearest millisecond so 39.9999 does not print as 999.
        double milliseconds = Math.Round(positionSeconds * 1_000, MidpointRounding.AwayFromZero);
        return milliseconds >= long.MaxValue ? long.MaxValue : (long)milliseconds;
    }
}
