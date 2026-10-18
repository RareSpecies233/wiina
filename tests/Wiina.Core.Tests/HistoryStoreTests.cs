using Xunit;
using Wiina.Core.History;

namespace Wiina.Core.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wiina-history-tests",
        Guid.NewGuid().ToString("N"));

    private string HistoryPath => Path.Combine(_directory, "history.json");

    private HistoryStore CreateStore() => new(HistoryPath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void RecordThenFindReturnsEntry()
    {
        HistoryStore store = CreateStore();
        store.Record(new HistoryEntry
        {
            Path = @"C:\media\one.mkv",
            Title = "One",
            Duration = 120,
            Position = 42,
            LastPlayedUtc = DateTimeOffset.UtcNow,
        });

        HistoryEntry? found = CreateStore().Find(@"c:\MEDIA\one.mkv");

        Assert.NotNull(found);
        Assert.Equal("One", found!.Title);
        Assert.Equal(42, found.Position);
    }

    [Fact]
    public void RecordUpdatesExistingPathAndKeepsOneEntry()
    {
        HistoryStore store = CreateStore();
        store.Record(new HistoryEntry
        {
            Path = "/media/a.mp4",
            Title = "Old",
            Duration = 100,
            Position = 10,
        });
        store.Record(new HistoryEntry
        {
            Path = "/media/a.mp4",
            Title = "New",
            Duration = 120,
            Position = 30,
        });

        IReadOnlyList<HistoryEntry> entries = CreateStore().Load();

        Assert.Single(entries);
        Assert.Equal("New", entries[0].Title);
        Assert.Equal(30, entries[0].Position);
    }

    [Fact]
    public void RecentOrdersByLastPlayedDescending()
    {
        HistoryStore store = CreateStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        store.Record(new HistoryEntry
        {
            Path = "/a", LastPlayedUtc = now.AddMinutes(-10),
        });
        store.Record(new HistoryEntry
        {
            Path = "/b", LastPlayedUtc = now.AddMinutes(-1),
        });
        store.Record(new HistoryEntry
        {
            Path = "/c", LastPlayedUtc = now.AddMinutes(-20),
        });

        IReadOnlyList<HistoryEntry> recent = store.Recent(2);

        Assert.Equal(new[] { "/b", "/a" }, recent.Select(entry => entry.Path));
    }

    [Fact]
    public void RecordPositionPreservesTitleAndDuration()
    {
        HistoryStore store = CreateStore();
        store.Record(new HistoryEntry
        {
            Path = "/media/b.mkv",
            Title = "Bee",
            Duration = 90,
            Position = 1,
        });

        store.RecordPosition("/media/b.mkv", 55, duration: 0);

        HistoryEntry? found = store.Find("/media/b.mkv");
        Assert.NotNull(found);
        Assert.Equal("Bee", found!.Title);
        Assert.Equal(90, found.Duration);
        Assert.Equal(55, found.Position);
    }

    [Fact]
    public void RemoveReturnsTrueOnlyWhenEntryExisted()
    {
        HistoryStore store = CreateStore();
        store.Record(new HistoryEntry { Path = "/media/c.mp4" });

        Assert.True(store.Remove("/media/c.mp4"));
        Assert.False(store.Remove("/media/c.mp4"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void CorruptFileLoadsAsEmpty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(HistoryPath, "{ not json");

        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void SaveLeavesNoTemporaryFile()
    {
        HistoryStore store = CreateStore();
        store.Record(new HistoryEntry { Path = "/media/d.mp4" });

        Assert.True(File.Exists(HistoryPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void MaxEntriesIsEnforced()
    {
        var store = new HistoryStore(HistoryPath) { MaxEntries = 3 };
        for (int i = 0; i < 6; i++)
        {
            store.Record(new HistoryEntry
            {
                Path = $"/media/{i}.mp4",
                LastPlayedUtc = DateTimeOffset.UtcNow.AddSeconds(i),
            });
        }

        Assert.Equal(3, CreateStore().Load().Count);
    }
}
