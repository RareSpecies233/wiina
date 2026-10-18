namespace Wiina.Core.Files;

public enum MediaKind
{
    Unknown,
    Video,
    Audio,
    Subtitle,
    Playlist,
}

/// <summary>
/// Extension-based media classification used by file/folder/playlist opening.
/// Playback itself is always delegated to mpv; this only decides how a path is
/// routed (open, append, subtitle, m3u).
/// </summary>
public static class MediaFileTypes
{
    private static readonly HashSet<string> VideoExtensions = new(
        new[]
        {
            ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv",
            ".ts", ".m2ts", ".mts", ".mpg", ".mpeg", ".ogv", ".3gp", ".divx",
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AudioExtensions = new(
        new[]
        {
            ".mp3", ".flac", ".aac", ".m4a", ".wav", ".ogg", ".opus", ".wma",
            ".alac", ".aiff", ".aif", ".ape", ".mka",
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SubtitleExtensions = new(
        new[]
        {
            ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi",
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PlaylistExtensions = new(
        new[] { ".m3u", ".m3u8", ".pls" },
        StringComparer.OrdinalIgnoreCase);

    public static MediaKind Classify(string? path)
    {
        string extension = GetExtension(path);
        if (extension.Length == 0)
        {
            return MediaKind.Unknown;
        }

        if (VideoExtensions.Contains(extension))
        {
            return MediaKind.Video;
        }

        if (AudioExtensions.Contains(extension))
        {
            return MediaKind.Audio;
        }

        if (SubtitleExtensions.Contains(extension))
        {
            return MediaKind.Subtitle;
        }

        return PlaylistExtensions.Contains(extension)
            ? MediaKind.Playlist
            : MediaKind.Unknown;
    }

    public static bool IsPlayableMedia(string? path) =>
        Classify(path) is MediaKind.Video or MediaKind.Audio;

    public static bool IsMediaFile(string? path) =>
        Classify(path) is MediaKind.Video or MediaKind.Audio or MediaKind.Subtitle;

    public static bool IsSubtitle(string? path) =>
        Classify(path) == MediaKind.Subtitle;

    public static bool IsPlaylist(string? path) =>
        Classify(path) == MediaKind.Playlist;

    public static string GetExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetExtension(path).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Expands a folder into playable files (and nested playlists). Inaccessible
    /// entries are skipped; the result is ordinal-ignore-case sorted.
    /// </summary>
    public static IReadOnlyList<string> ExpandFolder(
        string folder,
        bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            };
            return Directory
                .EnumerateFiles(folder, "*", options)
                .Where(file => IsPlayableMedia(file) || IsPlaylist(file))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Filters a mixed path list down to playable media (stable order).</summary>
    public static IReadOnlyList<string> FilterPlayable(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Where(IsPlayableMedia).ToArray();
    }
}
