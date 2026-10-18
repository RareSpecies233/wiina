using Wiina.Core.History;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class RecentListTests
{
    [Fact]
    public void PushInsertsAtFront()
    {
        IReadOnlyList<string> list = RecentList.Push(new[] { "a.mp4", "b.mp4" }, "c.mp4");
        Assert.Equal(new[] { "c.mp4", "a.mp4", "b.mp4" }, list);
    }

    [Fact]
    public void PushMovesExistingEntryToFrontCaseInsensitively()
    {
        IReadOnlyList<string> list = RecentList.Push(
            new[] { "Movie.MKV", "other.mp4" },
            "movie.mkv");
        Assert.Equal(2, list.Count);
        Assert.Equal("movie.mkv", list[0]);
        Assert.Equal("other.mp4", list[1]);
    }

    [Fact]
    public void PushCapsTheList()
    {
        var existing = Enumerable.Range(0, 12)
            .Select(index => $"file{index}.mp4")
            .ToArray();
        IReadOnlyList<string> list = RecentList.Push(existing, "new.mp4", capacity: 3);
        Assert.Equal(3, list.Count);
        Assert.Equal("new.mp4", list[0]);
        Assert.Equal("file0.mp4", list[1]);
        Assert.Equal("file1.mp4", list[2]);
    }

    [Fact]
    public void PushIgnoresBlankValuesAndTrims()
    {
        Assert.Empty(RecentList.Push(null, "   "));
        Assert.Equal(new[] { "a.mp4" }, RecentList.Push(null, "  a.mp4  "));
        Assert.Equal(new[] { "a.mp4" }, RecentList.Push(new[] { " " }, "a.mp4"));
    }

    [Fact]
    public void RemoveDropsOnlyTheMatchingEntry()
    {
        IReadOnlyList<string> list = RecentList.Remove(
            new[] { "a.mp4", "b.mp4", "c.mp4" },
            "B.MP4");
        Assert.Equal(new[] { "a.mp4", "c.mp4" }, list);
        Assert.Single(RecentList.Remove(new[] { "a" }, "missing"));
        Assert.Empty(RecentList.Remove(null, "a"));
    }

    [Fact]
    public void NormalizeDeduplicatesAndFilters()
    {
        IReadOnlyList<string> list = RecentList.Normalize(
            new[] { " a.mp4 ", "", "A.MP4", "b.mp4", "c.mp4" },
            capacity: 2);
        Assert.Equal(new[] { "a.mp4", "b.mp4" }, list);
        Assert.Empty(RecentList.Normalize(null));
    }
}
