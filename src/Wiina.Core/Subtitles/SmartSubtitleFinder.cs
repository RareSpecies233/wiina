using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wiina.Core.Subtitles;

/// <summary>Language family inferred from a sidecar subtitle file name.</summary>
public enum SubtitleLanguage
{
    Unknown = 0,
    SimplifiedChinese,
    TraditionalChinese,
    English,
    Japanese,
}

/// <summary>Values accepted by <c>AppSettings.PreferredSubtitleLanguage</c>.</summary>
public static class SubtitleLanguagePreferences
{
    public const string Auto = "auto";
    public const string SimplifiedChinese = "zh-Hans";
    public const string TraditionalChinese = "zh-Hant";
    public const string English = "en";
    public const string Japanese = "ja";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Auto,
        SimplifiedChinese,
        TraditionalChinese,
        English,
        Japanese,
    };

    /// <summary>Maps user input (including ISO/tag aliases) to a canonical value.</summary>
    public static string Normalize(string? value)
    {
        string token = (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return token switch
        {
            "zhhans" or "zhcn" or "chs" or "sc" or "chi" or "zho" or "zh" => SimplifiedChinese,
            "zhhant" or "zhtw" or "zhhk" or "tc" or "cht" => TraditionalChinese,
            "en" or "eng" => English,
            "ja" or "jp" or "jpn" => Japanese,
            _ => Auto,
        };
    }

    /// <summary>Canonical preference to the matching language family (auto = unknown).</summary>
    public static SubtitleLanguage ToLanguage(string? value) =>
        Normalize(value) switch
        {
            SimplifiedChinese => SubtitleLanguage.SimplifiedChinese,
            TraditionalChinese => SubtitleLanguage.TraditionalChinese,
            English => SubtitleLanguage.English,
            Japanese => SubtitleLanguage.Japanese,
            _ => SubtitleLanguage.Unknown,
        };
}

/// <summary>One sidecar subtitle that belongs to the loaded video.</summary>
public sealed record SubtitleCandidate(
    string Path,
    SubtitleLanguage Language,
    bool IsAssFonts,
    bool IsExactBase,
    int ExtensionRank)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Finds subtitle sidecars that share the video's base name, IINA style.
/// Pure filesystem logic with no UI or mpv dependency.
/// </summary>
public static class SmartSubtitleFinder
{
    /// <summary>Subtitle extensions understood by mpv, in tie-break preference order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[]
    {
        ".ass",
        ".ssa",
        ".srt",
        ".vtt",
        ".sub",
        ".idx",
    };

    /// <summary>All candidates next to <paramref name="videoPath"/>, best first.</summary>
    public static IReadOnlyList<SubtitleCandidate> Find(
        string? videoPath,
        string? preferredLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return Array.Empty<SubtitleCandidate>();
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
            return Array.Empty<SubtitleCandidate>();
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<SubtitleCandidate>();
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<SubtitleCandidate>();
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            names.Add(Path.GetFileName(file));
        }

        SubtitleLanguage preferred = SubtitleLanguagePreferences.ToLanguage(preferredLanguage);
        var candidates = new List<SubtitleCandidate>();
        foreach (string file in files)
        {
            string extension = Path.GetExtension(file);
            int extensionRank = ExtensionRank(extension);
            if (extensionRank < 0)
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file);
            string? suffix = SidecarSuffix(videoBase, stem);
            bool exactBase = suffix is not null;
            suffix ??= FuzzySuffix(videoBase, stem);
            if (suffix is null)
            {
                continue;
            }

            // .idx/.vobsub pairs need their .sub companion; .sub is exposed by
            // the .idx index file and would otherwise be loaded twice.
            if (string.Equals(extension, ".idx", StringComparison.OrdinalIgnoreCase))
            {
                if (!names.Contains(stem + ".sub"))
                {
                    continue;
                }
            }
            else if (string.Equals(extension, ".sub", StringComparison.OrdinalIgnoreCase) &&
                     names.Contains(stem + ".idx"))
            {
                continue;
            }

            string[] tokens = suffix.Length == 0
                ? Array.Empty<string>()
                : suffix.Split('.', StringSplitOptions.RemoveEmptyEntries);
            SubtitleLanguage language = ClassifyTokens(tokens);
            bool assFonts = tokens.Any(
                token => string.Equals(token, "assfonts", StringComparison.OrdinalIgnoreCase));

            candidates.Add(new SubtitleCandidate(
                file,
                language,
                assFonts,
                exactBase && suffix.Length == 0,
                extensionRank));
        }

        candidates.Sort((left, right) =>
        {
            int compare = left.IsAssFonts.CompareTo(right.IsAssFonts);
            if (compare != 0)
            {
                return compare;
            }

            compare = ExactPenalty(left, preferred).CompareTo(ExactPenalty(right, preferred));
            if (compare != 0)
            {
                return compare;
            }

            compare = left.ExtensionRank.CompareTo(right.ExtensionRank);
            if (compare != 0)
            {
                return compare;
            }

            return string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
        });

        return candidates;
    }

    /// <summary>Best sidecar for the video, or null when none matches.</summary>
    public static SubtitleCandidate? FindBest(
        string? videoPath,
        string? preferredLanguage = null) =>
        Find(videoPath, preferredLanguage).FirstOrDefault();

    /// <summary>True when the suffix belongs to the video (exact base or dotted suffix).</summary>
    public static bool IsSidecarOf(string? videoBaseName, string? subtitleStem) =>
        SidecarSuffix(videoBaseName, subtitleStem) is not null;

    /// <summary>Classifies language tokens such as chs/sc/zh-Hans/tc/en.</summary>
    public static SubtitleLanguage ClassifyTokens(IEnumerable<string>? tokens)
    {
        if (tokens is null)
        {
            return SubtitleLanguage.Unknown;
        }

        foreach (string token in tokens)
        {
            switch (SubtitleLanguagePreferences.Normalize(token))
            {
                case SubtitleLanguagePreferences.SimplifiedChinese:
                    return SubtitleLanguage.SimplifiedChinese;
                case SubtitleLanguagePreferences.TraditionalChinese:
                    return SubtitleLanguage.TraditionalChinese;
                case SubtitleLanguagePreferences.English:
                    return SubtitleLanguage.English;
                case SubtitleLanguagePreferences.Japanese:
                    return SubtitleLanguage.Japanese;
                default:
                    break;
            }
        }

        return SubtitleLanguage.Unknown;
    }

    private static string? SidecarSuffix(string? videoBaseName, string? subtitleStem)
    {
        if (string.IsNullOrEmpty(videoBaseName) || string.IsNullOrEmpty(subtitleStem))
        {
            return null;
        }

        if (string.Equals(videoBaseName, subtitleStem, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (subtitleStem.Length > videoBaseName.Length + 1 &&
            subtitleStem.StartsWith(videoBaseName + ".", StringComparison.OrdinalIgnoreCase))
        {
            return subtitleStem[(videoBaseName.Length + 1)..];
        }

        return null;
    }

    /// <summary>
    /// IINA-style loose match: ignore trailing release tags on the video name
    /// and accept a sidecar that keeps the same title/episode tokens. This
    /// catches "[Group] Show [01][1080p].mkv" + "[Group] Show [01].chs.ass"
    /// without matching a different episode.
    /// </summary>
    private static string? FuzzySuffix(string videoBase, string subtitleStem)
    {
        List<string> videoTokens = Tokenize(videoBase);
        while (videoTokens.Count > 0 && IsReleaseTag(videoTokens[^1]))
        {
            videoTokens.RemoveAt(videoTokens.Count - 1);
        }

        List<string> subtitleTokens = Tokenize(subtitleStem);
        if (videoTokens.Count < 2 || subtitleTokens.Count <= videoTokens.Count)
        {
            return null;
        }

        for (int i = 0; i < videoTokens.Count; i++)
        {
            if (!string.Equals(videoTokens[i], subtitleTokens[i], StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        string[] suffixTokens = subtitleTokens.Skip(videoTokens.Count).ToArray();
        if (suffixTokens.Length == 0 ||
            !suffixTokens.All(token => IsReleaseTag(token) || IsLanguageTag(token)))
        {
            return null;
        }

        return string.Join('.', suffixTokens);
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static bool IsLanguageTag(string token) =>
        SubtitleLanguagePreferences.Normalize(token) != SubtitleLanguagePreferences.Auto ||
        string.Equals(token, "assfonts", StringComparison.OrdinalIgnoreCase);

    private static bool IsReleaseTag(string token)
    {
        string normalized = token.ToLowerInvariant();
        return normalized is
            "1080p" or "2160p" or "1440p" or "720p" or "576p" or "480p" or
            "4k" or "8k" or "uhd" or "hdr" or "hdr10" or "dv" or "dolby" or
            "vision" or "atmos" or "x264" or "x265" or "h264" or "h265" or
            "hevc" or "avc" or "av1" or "10bit" or "8bit" or "ma10p" or
            "flac" or "aac" or "ac3" or "dts" or "truehd" or "web" or
            "webrip" or "webdl" or "bluray" or "bdrip" or "remux" or
            "proper" or "repack" or "v2" or "v3";
    }

    private static int ExactPenalty(SubtitleCandidate candidate, SubtitleLanguage preferred) =>
        candidate.IsExactBase ? 0 : 1 + LanguageRank(candidate.Language, preferred);

    private static int LanguageRank(SubtitleLanguage language, SubtitleLanguage preferred)
    {
        if (language == SubtitleLanguage.Unknown)
        {
            return 9;
        }

        if (preferred != SubtitleLanguage.Unknown && language == preferred)
        {
            return 0;
        }

        return language switch
        {
            SubtitleLanguage.SimplifiedChinese => 1,
            SubtitleLanguage.TraditionalChinese => 2,
            SubtitleLanguage.English => 3,
            SubtitleLanguage.Japanese => 4,
            _ => 9,
        };
    }

    private static int ExtensionRank(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return -1;
        }

        for (int i = 0; i < Extensions.Count; i++)
        {
            if (string.Equals(extension, Extensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
