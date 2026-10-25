namespace Wiina.Core.Files;

/// <summary>
/// Result of expanding a single opened file into its folder queue.
/// <paramref name="Entries"/> is the complete, naturally sorted queue and
/// <paramref name="CurrentIndex"/> points at the file the user opened, so
/// "next" moves to the following episode instead of the first one.
/// </summary>
public sealed record PlaylistExpansion(
    IReadOnlyList<string> Entries,
    int CurrentIndex);

/// <summary>
/// Pure playlist ordering rules shared by the app and tests.
/// </summary>
public static class PlaylistOrdering
{
    /// <summary>
    /// Natural, case-insensitive path order: digit runs compare by numeric
    /// value ("EP2" before "EP10"), non-digit runs compare case-insensitively,
    /// and exact ties fall back to an ordinal comparison for determinism.
    /// </summary>
    public static IComparer<string> NaturalComparer { get; } = new NaturalPathComparer();

    /// <summary>Returns <paramref name="paths"/> in natural order (stable for equals).</summary>
    public static IReadOnlyList<string> SortNatural(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = paths.ToList();
        result.Sort(NaturalComparer);
        return result;
    }

    /// <summary>
    /// Builds the auto-expanded queue for a single opened file.
    /// <paramref name="folderFiles"/> is the folder listing (usually
    /// <see cref="MediaFileTypes.ExpandFolder(string, bool)"/>); duplicates are
    /// removed keeping the first occurrence. Returns <c>null</c> when the
    /// opened file is not part of the listing (missing, unreadable or not a
    /// media file), which lets the caller fall back to the old behavior.
    /// </summary>
    public static PlaylistExpansion? ExpandSingleFile(
        string? currentFile,
        IEnumerable<string>? folderFiles)
    {
        if (string.IsNullOrWhiteSpace(currentFile) || folderFiles is null)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<string>();
        foreach (string file in folderFiles)
        {
            if (string.IsNullOrWhiteSpace(file) || !seen.Add(file))
            {
                continue;
            }

            entries.Add(file);
        }

        int currentIndex = IndexOf(entries, currentFile);
        if (currentIndex < 0)
        {
            return null;
        }

        entries.Sort(NaturalComparer);
        currentIndex = IndexOf(entries, currentFile);
        return currentIndex < 0
            ? null
            : new PlaylistExpansion(entries, currentIndex);
    }

    private static int IndexOf(List<string> entries, string path)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i], path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class NaturalPathComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int natural = CompareNatural(x, y);
            return natural != 0
                ? natural
                : string.CompareOrdinal(x, y);
        }

        private static int CompareNatural(string x, string y)
        {
            int i = 0;
            int j = 0;
            while (i < x.Length && j < y.Length)
            {
                bool xDigit = char.IsDigit(x[i]);
                bool yDigit = char.IsDigit(y[j]);
                if (xDigit && yDigit)
                {
                    int xStart = i;
                    int yStart = j;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        j++;
                    }

                    int digits = CompareDigitRuns(
                        x.AsSpan(xStart, i - xStart),
                        y.AsSpan(yStart, j - yStart));
                    if (digits != 0)
                    {
                        return digits;
                    }

                    continue;
                }

                if (xDigit != yDigit)
                {
                    // Numbers sort before letters, matching Explorer-like order.
                    return xDigit ? -1 : 1;
                }

                int xRunStart = i;
                int yRunStart = j;
                while (i < x.Length && !char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && !char.IsDigit(y[j]))
                {
                    j++;
                }

                int text = x
                    .AsSpan(xRunStart, i - xRunStart)
                    .CompareTo(
                        y.AsSpan(yRunStart, j - yRunStart),
                        StringComparison.OrdinalIgnoreCase);
                if (text != 0)
                {
                    return text;
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }

        private static int CompareDigitRuns(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
        {
            ReadOnlySpan<char> xValue = TrimLeadingZeros(x);
            ReadOnlySpan<char> yValue = TrimLeadingZeros(y);
            int length = xValue.Length.CompareTo(yValue.Length);
            if (length != 0)
            {
                return length;
            }

            int value = xValue.SequenceCompareTo(yValue);
            if (value != 0)
            {
                return value;
            }

            // Same numeric value: fewer leading zeros first ("EP8" before "EP08").
            int zeros = x.Length.CompareTo(y.Length);
            return zeros != 0 ? zeros : x.SequenceCompareTo(y);
        }

        private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> value)
        {
            int index = 0;
            while (index < value.Length - 1 && value[index] == '0')
            {
                index++;
            }

            return value[index..];
        }
    }
}
