namespace Wiina.Core.History;

/// <summary>
/// Small pure helpers for the "recent files/URLs" lists used by the welcome
/// window, the File → Open Recent menu and the settings window.
/// </summary>
public static class RecentList
{
    public const int DefaultCapacity = 10;

    /// <summary>
    /// Inserts <paramref name="value"/> at the front, removing a previous copy
    /// (case-insensitive), trimming blanks and capping the list length.
    /// </summary>
    public static IReadOnlyList<string> Push(
        IReadOnlyList<string>? items,
        string? value,
        int capacity = DefaultCapacity)
    {
        var result = new List<string>();
        int limit = Math.Max(1, capacity);
        if (!string.IsNullOrWhiteSpace(value))
        {
            result.Add(value.Trim());
        }

        if (items is not null)
        {
            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string trimmed = item.Trim();
                if (result.Any(existing => Same(existing, trimmed)))
                {
                    continue;
                }

                result.Add(trimmed);
                if (result.Count >= limit)
                {
                    break;
                }
            }
        }

        if (result.Count > limit)
        {
            result.RemoveRange(limit, result.Count - limit);
        }

        return result;
    }

    /// <summary>Removes one entry and keeps the remaining order.</summary>
    public static IReadOnlyList<string> Remove(
        IReadOnlyList<string>? items,
        string? value)
    {
        if (items is null || string.IsNullOrWhiteSpace(value))
        {
            return items?.ToArray() ?? Array.Empty<string>();
        }

        return items
            .Where(item => !Same(item, value))
            .ToArray();
    }

    /// <summary>Trims, de-duplicates and caps an untyped sequence.</summary>
    public static IReadOnlyList<string> Normalize(
        IEnumerable<string>? items,
        int capacity = DefaultCapacity)
    {
        if (items is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        int limit = Math.Max(1, capacity);
        foreach (string item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            string trimmed = item.Trim();
            if (result.Any(existing => Same(existing, trimmed)))
            {
                continue;
            }

            result.Add(trimmed);
            if (result.Count >= limit)
            {
                break;
            }
        }

        return result;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
