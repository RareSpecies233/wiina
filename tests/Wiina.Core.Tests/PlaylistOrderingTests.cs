using Xunit;
using Wiina.Core.Files;

namespace Wiina.Core.Tests;

public sealed class PlaylistOrderingTests
{
    private static string[] Episodes(int count)
    {
        return Enumerable
            .Range(1, count)
            .Select(number => $@"C:\shows\EP{number:00}.mkv")
            .ToArray();
    }

    [Fact]
    public void MiddleEpisodeKeepsFolderOrderAndCurrentIndex()
    {
        string[] folder = Episodes(12);

        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP08.mkv", folder));

        Assert.Equal(12, expansion.Entries.Count);
        Assert.Equal(@"C:\shows\EP01.mkv", expansion.Entries[0]);
        Assert.Equal(@"C:\shows\EP12.mkv", expansion.Entries[^1]);
        Assert.Equal(7, expansion.CurrentIndex);
        Assert.Equal(@"C:\shows\EP07.mkv", expansion.Entries[expansion.CurrentIndex - 1]);
        Assert.Equal(@"C:\shows\EP09.mkv", expansion.Entries[expansion.CurrentIndex + 1]);
    }

    [Fact]
    public void FirstEpisodeStartsAtZero()
    {
        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP01.mkv", Episodes(12)));

        Assert.Equal(0, expansion.CurrentIndex);
        Assert.Equal(@"C:\shows\EP01.mkv", expansion.Entries[0]);
    }

    [Fact]
    public void LastEpisodeIsStillPlayableFromTheEnd()
    {
        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP99.mkv", Episodes(99)));

        Assert.Equal(98, expansion.CurrentIndex);
        Assert.Equal(@"C:\shows\EP99.mkv", expansion.Entries[^1]);
    }

    [Fact]
    public void NaturalOrderPutsEpisodeTwoBeforeEpisodeTen()
    {
        string[] folder =
        {
            @"C:\shows\EP10.mkv",
            @"C:\shows\EP2.mkv",
            @"C:\shows\EP1.mkv",
        };

        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP10.mkv", folder));

        Assert.Equal(
            new[]
            {
                @"C:\shows\EP1.mkv",
                @"C:\shows\EP2.mkv",
                @"C:\shows\EP10.mkv",
            },
            expansion.Entries);
        Assert.Equal(2, expansion.CurrentIndex);
    }

    [Fact]
    public void MixedCaseAndPaddedEpisodesSortNaturally()
    {
        string[] folder =
        {
            @"C:\shows\Ep10.mkv",
            @"C:\shows\EP9.mkv",
            @"C:\shows\ep08.mkv",
        };

        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP9.mkv", folder));

        Assert.Equal(
            new[]
            {
                @"C:\shows\ep08.mkv",
                @"C:\shows\EP9.mkv",
                @"C:\shows\Ep10.mkv",
            },
            expansion.Entries);
        Assert.Equal(1, expansion.CurrentIndex);
    }

    [Fact]
    public void NumericValueTiesPreferTheUnpaddedName()
    {
        IReadOnlyList<string> sorted = PlaylistOrdering.SortNatural(
            new[] { @"C:\shows\EP08.mkv", @"C:\shows\EP8.mkv" });

        Assert.Equal(@"C:\shows\EP8.mkv", sorted[0]);
        Assert.Equal(@"C:\shows\EP08.mkv", sorted[1]);
    }

    [Fact]
    public void DuplicatesAreRemovedKeepingTheFirstOccurrence()
    {
        string[] folder =
        {
            @"C:\shows\EP08.mkv",
            @"C:\shows\EP07.mkv",
            @"c:\shows\ep08.MKV",
        };

        PlaylistExpansion expansion = Assert.IsType<PlaylistExpansion>(
            PlaylistOrdering.ExpandSingleFile(@"c:\shows\ep08.MKV", folder));

        Assert.Equal(2, expansion.Entries.Count);
        Assert.Equal(@"C:\shows\EP08.mkv", expansion.Entries[1]);
        Assert.Equal(1, expansion.CurrentIndex);
    }

    [Fact]
    public void MissingOrNonDirectoryInputFallsBackToNull()
    {
        Assert.Null(PlaylistOrdering.ExpandSingleFile(
            @"C:\other\EP08.mkv",
            Episodes(3)));
        Assert.Null(PlaylistOrdering.ExpandSingleFile(null, Episodes(3)));
        Assert.Null(PlaylistOrdering.ExpandSingleFile(@"C:\shows\EP08.mkv", null));
        Assert.Null(PlaylistOrdering.ExpandSingleFile(
            @"C:\shows\EP08.mkv",
            Array.Empty<string>()));
    }

    [Fact]
    public void RecursiveSubfoldersSortNaturally()
    {
        IReadOnlyList<string> sorted = PlaylistOrdering.SortNatural(
            new[]
            {
                @"C:\shows\Season 10\EP02.mkv",
                @"C:\shows\Season 2\EP10.mkv",
                @"C:\shows\Season 2\EP09.mkv",
            });

        Assert.Equal(
            new[]
            {
                @"C:\shows\Season 2\EP09.mkv",
                @"C:\shows\Season 2\EP10.mkv",
                @"C:\shows\Season 10\EP02.mkv",
            },
            sorted);
    }
}
