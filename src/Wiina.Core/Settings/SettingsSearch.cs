namespace Wiina.Core.Settings;

/// <summary>One search result: either a concrete row or a page header.</summary>
public sealed record SettingsSearchHit(
    SettingsPageInfo Page,
    SettingsRowDefinition? Row,
    int Score)
{
    public string Title => Row?.Title ?? Page.Title;

    public string Subtitle => Row is null
        ? Page.Summary
        : $"{Page.Title} › {Row.Section}";

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

    public SettingsSearchIndex(
        IEnumerable<SettingsPageInfo> pages,
        IEnumerable<SettingsRowDefinition> rows)
    {
        _pages = pages.ToArray();
        _rows = rows.ToArray();
        _pageOrder = _pages
            .Select((page, index) => (page.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
    }

    public IReadOnlyList<SettingsSearchHit> Search(string? query, int limit = 20)
    {
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
            int? score = ScoreRow(row, page, text, tokens);
            if (score is not null)
            {
                hits.Add(new SettingsSearchHit(page, row, score.Value * 1000 + index));
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

            int? score = ScorePage(page, text, tokens);
            if (score is not null)
            {
                hits.Add(new SettingsSearchHit(page, null, score.Value * 1000 + index));
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

    private static int? ScoreRow(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query,
        string[] tokens)
    {
        string combined = string.Join(
            ' ',
            row.Title,
            row.Keywords,
            row.Description,
            row.Section,
            page.Title,
            page.Summary);

        if (!AllTokensPresent(combined, tokens))
        {
            return null;
        }

        int? full = BestFieldScore(row, page, query);
        if (full is not null)
        {
            return full;
        }

        // Multi-word query where no single field contains the whole phrase:
        // sum the best per-token field scores, then push below phrase matches.
        int sum = 0;
        foreach (string token in tokens)
        {
            int? tokenScore = BestFieldScore(row, page, token);
            if (tokenScore is null)
            {
                return null;
            }

            sum += tokenScore.Value;
        }

        return sum + 40;
    }

    private static int? ScorePage(
        SettingsPageInfo page,
        string query,
        string[] tokens)
    {
        string combined = page.Title + " " + page.Summary;
        if (!AllTokensPresent(combined, tokens))
        {
            return null;
        }

        int? title = FieldScore(page.Title, query, 24);
        if (title is not null)
        {
            return title.Value + 24;
        }

        int? summary = FieldScore(page.Summary, query, 30);
        return summary is null ? null : summary.Value + 24;
    }

    private static int? BestFieldScore(
        SettingsRowDefinition row,
        SettingsPageInfo page,
        string query)
    {
        int? best = null;
        foreach ((string text, int weight) in new[]
                 {
                     (row.Title, 0),
                     (row.Keywords, 6),
                     (row.Description, 12),
                     (row.Section, 18),
                     (page.Title, 24),
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
