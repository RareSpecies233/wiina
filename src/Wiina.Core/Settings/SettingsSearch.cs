using Wiina.Core.Localization;

namespace Wiina.Core.Settings;

/// <summary>One search result: either a concrete row or a page header.</summary>
public sealed record SettingsSearchHit(
    SettingsPageInfo Page,
    SettingsRowDefinition? Row,
    int Score,
    string Language = AppLanguage.ZhHans)
{
    public string Title => Row is null
        ? SettingsLocalization.PageTitle(Page, Language)
        : SettingsLocalization.Title(Row, Language);

    public string Subtitle => Row is null
        ? SettingsLocalization.PageSummary(Page, Language)
        : $"{SettingsLocalization.PageTitle(Page, Language)} › " +
          SettingsLocalization.Section(Row.Section, Language);

    public string? RowId => Row?.Id;
}

/// <summary>
/// Search index over settings pages, sections and rows (spec §12.2). Ranking:
/// exact title &gt; title prefix &gt; title word prefix &gt; title contains &gt;
/// keywords &gt; description &gt; section &gt; page. Ties keep page/row order.
/// </summary>
public sealed class SettingsSearchIndex
{
    private readonly SettingsPageInfo[] _pages;
    private readonly SettingsRowDefinition[] _rows;
    private readonly Dictionary<SettingsPageId, int> _pageOrder;
    private readonly string _language;

    public SettingsSearchIndex(
        IEnumerable<SettingsPageInfo> pages,
        IEnumerable<SettingsRowDefinition> rows,
        string language = AppLanguage.ZhHans)
    {
        _pages = pages.ToArray();
        _rows = rows.ToArray();
        _language = language;
        _pageOrder = _pages
            .Select((page, index) => (page.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
    }

    public IReadOnlyList<SettingsSearchHit> Search(
        string? query,
        int limit = 20,
        string? language = null)
    {
        string resolvedLanguage = language ?? _language;
        string text = (query ?? string.Empty).Trim();
        if (text.Length == 0 || limit <= 0)
        {
            return Array.Empty<SettingsSearchHit>();
        }

        string[] tokens = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<SettingsSearchHit>();

        for (int index = 0; index < _rows.Length; index++)
        {
            SettingsRowDefinition row = _rows[index];
            SettingsPageInfo page = PageOf(row.Page);
            int? score = ScoreRow(row, page, text, tokens, resolvedLanguage);
            if (score is not null)
            {
                hits.Add(new SettingsSearchHit(page, row, score.Value * 1000 + index, resolvedLanguage));
            }
        }

        var matchedPages = hits.Select(hit => hit.Page.Id).ToHashSet();
        for (int index = 0; index < _pages.Length; index++)
        {
            SettingsPageInfo page = _pages[index];
            if (matchedPages.Contains(page.Id))
            {
                continue;
            }

            int? score = ScorePage(page, text, tokens, resolvedLanguage);
            if (score is not null)
            {
                hits.Add(new SettingsSearchHit(page, null, score.Value * 1000 + index, resolvedLanguage));
            }
        }

        return hits
            .OrderBy(hit => hit.Score)
            .ThenBy(hit => hit.Page.Id)
            .ThenBy(hit => hit.Row?.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private SettingsPageInfo PageOf(SettingsPageId id) =>
        _pages.First(page => page.Id == id);

    private int? ScoreRow(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query,
        string[] tokens,
        string language)
    {
        string combined = string.Join(
            ' ',
            row.Title,
            SettingsLocalization.Title(row, language),
            row.Keywords,
            row.Description,
            SettingsLocalization.Description(row, language),
            row.Section,
            SettingsLocalization.Section(row.Section, language),
            page.Title,
            SettingsLocalization.PageTitle(page, language),
            page.Summary,
            SettingsLocalization.PageSummary(page, language));

        if (!AllTokensPresent(combined, tokens))
        {
            return null;
        }

        int? full = BestFieldScore(row, page, query, language);
        if (full is not null)
        {
            return full;
        }

        // Multi-word query where no single field contains the whole phrase:
        // sum the best per-token field scores, then push below phrase matches.
        int sum = 0;
        foreach (string token in tokens)
        {
            int? tokenScore = BestFieldScore(row, page, token, language);
            if (tokenScore is null)
            {
                return null;
            }

            sum += tokenScore.Value;
        }

        return sum + 40;
    }

    private int? ScorePage(
        SettingsPageInfo page,
        string query,
        string[] tokens,
        string language)
    {
        string title = SettingsLocalization.PageTitle(page, language);
        string summary = SettingsLocalization.PageSummary(page, language);
        string combined = string.Join(' ', page.Title, title, page.Summary, summary);
        if (!AllTokensPresent(combined, tokens))
        {
            return null;
        }

        int? titleScore = FieldScore(title, query, 24);
        if (titleScore is not null)
        {
            return titleScore.Value + 24;
        }

        int? summaryScore = FieldScore(summary, query, 30);
        return summaryScore is null ? null : summaryScore.Value + 24;
    }

    private int? BestFieldScore(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query,
        string language)
    {
        int? best = null;
        foreach ((string text, int weight) in new[]
                 {
                     (SettingsLocalization.Title(row, language), 0),
                     (row.Keywords, 6),
                     (SettingsLocalization.Description(row, language), 12),
                     (SettingsLocalization.Section(row.Section, language), 18),
                     (SettingsLocalization.PageTitle(page, language), 24),
                 })
        {
            int? score = FieldScore(text, query, weight);
            if (score is not null && (best is null || score < best))
            {
                best = score;
            }
        }

        return best;
    }

    /// <summary>Lower is better; null means the field does not contain the query.</summary>
    private static int? FieldScore(string? field, string query, int weight)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        string text = field.ToLowerInvariant();
        string needle = query.ToLowerInvariant();
        int index = text.IndexOf(needle, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        if (text.Length == needle.Length)
        {
            return weight;
        }

        if (index == 0)
        {
            return weight + 1;
        }

        bool wordStart = index > 0 &&
            !char.IsLetterOrDigit(text[index - 1]) &&
            char.GetUnicodeCategory(text[index - 1]) !=
                System.Globalization.UnicodeCategory.ConnectorPunctuation;
        return weight + (wordStart ? 2 : 3);
    }

    private static bool AllTokensPresent(string text, string[] tokens)
    {
        string lower = text.ToLowerInvariant();
        foreach (string token in tokens)
        {
            if (!lower.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

