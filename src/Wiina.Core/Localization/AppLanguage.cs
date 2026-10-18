using System.Globalization;

namespace Wiina.Core.Localization;

/// <summary>
/// Language preference handling for the Stage 11 localization layer. The
/// persisted setting stores one of <c>system</c>, <c>zh-Hans</c> or <c>en</c>;
/// this type is pure so the resolver can be unit-tested without WinUI.
/// </summary>
public static class AppLanguage
{
    /// <summary>Persisted value that asks the app to follow the OS language list.</summary>
    public const string System = "system";

    public const string ZhHans = "zh-Hans";

    public const string English = "en";

    /// <summary>Languages rendered by the app, in fallback order.</summary>
    public static readonly IReadOnlyList<string> Supported = new[] { ZhHans, English };

    /// <summary>
    /// Resolves a stored preference (or a raw BCP-47 tag) to a supported
    /// language. Unsupported system languages fall back to English; unknown
    /// preference values are treated like <see cref="System"/>.
    /// </summary>
    public static string Resolve(string? preference, IEnumerable<string>? systemLanguages = null)
    {
        string normalized = NormalizeTag(preference);
        if (normalized.Length == 0)
        {
            // Missing/empty preference means "no choice was stored yet"; the
            // product default is Simplified Chinese.
            return ZhHans;
        }

        if (normalized == English)
        {
            return English;
        }

        if (normalized.StartsWith("zh", StringComparison.Ordinal))
        {
            return ZhHans;
        }

        if (!string.IsNullOrEmpty(normalized) && normalized != System)
        {
            // A concrete but unsupported tag ("de-DE") behaves like a system
            // request rather than silently choosing Chinese.
            return ResolveSystem(systemLanguages);
        }

        return ResolveSystem(systemLanguages);
    }

    /// <summary>True when the stored value is one of the supported preferences.</summary>
    public static bool IsSupportedPreference(string? preference)
    {
        string normalized = NormalizeTag(preference);
        return normalized is System or ZhHans or English;
    }

    /// <summary>Choice index used by the settings row (0 system, 1 zh-Hans, 2 en).</summary>
    public static int ToChoiceIndex(string? preference) => NormalizeTag(preference) switch
    {
        ZhHans => 1,
        English => 2,
        _ => 0,
    };

    /// <summary>Maps a settings choice index back to the persisted value.</summary>
    public static string FromChoiceIndex(int index) => index switch
    {
        1 => ZhHans,
        2 => English,
        _ => System,
    };

    /// <summary>True for right-to-left language tags (RTL layout is supported, not shipped).</summary>
    public static bool IsRightToLeft(string? languageTag)
    {
        string normalized = NormalizeTag(languageTag);
        return normalized is "ar" or "he" or "fa" or "ur" or "ps" or "sd" or "ug" or "yi" or "dv" ||
            normalized.StartsWith("ar-", StringComparison.Ordinal) ||
            normalized.StartsWith("he-", StringComparison.Ordinal) ||
            normalized.StartsWith("fa-", StringComparison.Ordinal) ||
            normalized.StartsWith("ur-", StringComparison.Ordinal);
    }

    /// <summary>Normalizes casing/underscores without opening a full ICU dependency.</summary>
    public static string NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        string value = tag.Trim().Replace('_', '-').ToLowerInvariant();
        if (value == "system")
        {
            return System;
        }

        // Keep the canonical casing expected by Windows for display names.
        if (value is "zh" or "zh-cn" or "zh-sg" or "zh-hans" or "zh-hans-cn")
        {
            return ZhHans;
        }

        if (value is "en" or "en-us" or "en-gb" || value.StartsWith("en-", StringComparison.Ordinal))
        {
            return English;
        }

        if (value.StartsWith("zh-", StringComparison.Ordinal))
        {
            return ZhHans;
        }

        return value;
    }

    private static string ResolveSystem(IEnumerable<string>? systemLanguages)
    {
        if (systemLanguages is not null)
        {
            foreach (string candidate in systemLanguages)
            {
                string normalized = NormalizeTag(candidate);
                if (normalized == ZhHans)
                {
                    return ZhHans;
                }

                if (normalized == English)
                {
                    return English;
                }
            }
        }

        // Neutral fallback for a system language the app does not ship:
        // English is more broadly useful than assuming Chinese.
        return English;
    }

    /// <summary>Display name for a supported language (used by diagnostics/about).</summary>
    public static string DisplayName(string language) => NormalizeTag(language) switch
    {
        ZhHans => "简体中文",
        English => "English",
        _ => CultureInfo.GetCultureInfo(language).DisplayName,
    };
}
