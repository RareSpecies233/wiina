using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wiina.Core.Audio;

/// <summary>An external audio sidecar that shares the video's base name.</summary>
public sealed record ExternalAudioCandidate(string Path, bool IsExactBase)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Discovers same-base external audio sidecars. Only files that belong to the
/// loaded video (exact base or a dotted suffix such as <c>Movie.jpn.mka</c>)
/// are considered, so unrelated audio in the folder is never auto-loaded.
/// </summary>
public static class ExternalAudioFinder
{
    /// <summary>Extensions offered by the manual picker.</summary>
    public static IReadOnlyList<string> PickerExtensions { get; } = new[]
    {
        ".mka",
        ".ac3",
        ".dts",
        ".aac",
        ".flac",
        ".opus",
        ".wav",
        ".mp3",
        ".m4a",
        ".ogg",
    };

    /// <summary>Auto-load is intentionally limited to Matroska audio sidecars.</summary>
    public static IReadOnlyList<string> AutoLoadExtensions { get; } = new[] { ".mka" };

    public static bool IsSupportedAudio(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return PickerExtensions.Any(
            candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>All matching .mka sidecars, exact base first.</summary>
    public static IReadOnlyList<ExternalAudioCandidate> Find(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return Array.Empty<ExternalAudioCandidate>();
        }

        string fullVideoPath;
        string directory;
        string videoBase;
        try
        {
            fullVideoPath = Path.GetFullPath(videoPath);
            directory = Path.GetDirectoryName(fullVideoPath) ?? string.Empty;
            videoBase = Path.GetFileNameWithoutExtension(fullVideoPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Array.Empty<ExternalAudioCandidate>();
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<ExternalAudioCandidate>();
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<ExternalAudioCandidate>();
        }

        var candidates = new List<ExternalAudioCandidate>();
        foreach (string file in files)
        {
            string extension = Path.GetExtension(file);
            if (!AutoLoadExtensions.Any(
                    candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file);
            bool? exact = SidecarState(videoBase, stem);
            if (exact is null)
            {
                continue;
            }

            candidates.Add(new ExternalAudioCandidate(file, exact.Value));
        }

        candidates.Sort((left, right) =>
        {
            int compare = right.IsExactBase.CompareTo(left.IsExactBase);
            if (compare != 0)
            {
                return compare;
            }

            compare = left.FileName.Length.CompareTo(right.FileName.Length);
            if (compare != 0)
            {
                return compare;
            }

            return string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
        });

        return candidates;
    }

    /// <summary>Best matching .mka sidecar, or null when none matches.</summary>
    public static ExternalAudioCandidate? FindBest(string? videoPath) =>
        Find(videoPath).FirstOrDefault();

    /// <summary>True when the stem belongs to the video (exact base or dotted suffix).</summary>
    public static bool IsSidecarOf(string? videoBaseName, string? audioStem) =>
        SidecarState(videoBaseName, audioStem) is not null;

    private static bool? SidecarState(string? videoBaseName, string? audioStem)
    {
        if (string.IsNullOrEmpty(videoBaseName) || string.IsNullOrEmpty(audioStem))
        {
            return null;
        }

        if (string.Equals(videoBaseName, audioStem, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (audioStem.Length > videoBaseName.Length + 1 &&
            audioStem.StartsWith(videoBaseName + ".", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }
}
