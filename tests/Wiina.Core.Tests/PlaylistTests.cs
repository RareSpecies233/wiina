using Xunit;
using Wiina.Core.Models;

namespace Wiina.Core.Tests;

public sealed class PlaylistTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wiina-playlist-tests",
        Guid.NewGuid().ToString("N"));

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
    public void AddSelectsFirstAndAppends()
    {
        var playlist = new Playlist();
        Assert.Equal(0, playlist.Add("/a.mp4"));
        Assert.Equal(1, playlist.Add("/b.mp4"));

        Assert.Equal(2, playlist.Count);
        Assert.Equal(0, playlist.CurrentIndex);
        Assert.Equal("/a.mp4", playlist.Current!.Path);
    }

    [Fact]
    public void RemoveBeforeCurrentShiftsCurrentIndex()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b", "/c" });
        playlist.SetCurrent(2);

        Assert.True(playlist.RemoveAt(0));

        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Equal("/c", playlist.Current!.Path);
    }

    [Fact]
    public void RemoveCurrentKeepsNextEntrySelected()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b", "/c" });
        playlist.SetCurrent(1);

        Assert.True(playlist.RemoveAt(1));

        Assert.Equal(1, playlist.CurrentIndex);
        Assert.Equal("/c", playlist.Current!.Path);
    }

    [Fact]
    public void RemoveLastEntryClearsCurrent()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/only" });

        Assert.True(playlist.RemoveAt(0));
        Assert.Equal(-1, playlist.CurrentIndex);
        Assert.Null(playlist.Current);
    }

    [Fact]
    public void MoveReordersAndTracksCurrentItem()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b", "/c", "/d" });
        playlist.SetCurrent(1);

        Assert.True(playlist.Move(1, 3));

        Assert.Equal(new[] { "/a", "/c", "/d", "/b" },
            playlist.Items.Select(item => item.Path));
        Assert.Equal(3, playlist.CurrentIndex);
        Assert.Equal("/b", playlist.Current!.Path);
    }

    [Fact]
    public void MoveBackwardsTracksCurrentItem()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b", "/c" });
        playlist.SetCurrent(2);

        Assert.True(playlist.Move(2, 0));

        Assert.Equal(0, playlist.CurrentIndex);
        Assert.Equal("/c", playlist.Current!.Path);
        Assert.Equal(new[] { "/c", "/a", "/b" },
            playlist.Items.Select(item => item.Path));
    }

    [Fact]
    public void InvalidMoveOrSelectionIsRejected()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b" });

        Assert.False(playlist.Move(0, 2));
        Assert.False(playlist.Move(-1, 0));
        Assert.False(playlist.SetCurrent(2));
        Assert.False(playlist.RemoveAt(5));
    }

    [Fact]
    public void InsertAdjustsCurrentIndex()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/a", "/b" });
        playlist.SetCurrent(1);

        Assert.True(playlist.Insert(0, "/z"));

        Assert.Equal(2, playlist.CurrentIndex);
        Assert.Equal("/b", playlist.Current!.Path);
    }

    [Fact]
    public void M3uRoundTripPreservesPathsAndTitles()
    {
        Playlist playlist = Playlist.FromPaths(new[] { "/media/one.mkv", "/media/two.mp4" });
        playlist.SetCurrent(1);
        string text = playlist.ToM3u();

        Assert.Contains("#EXTM3U", text);
        Playlist parsed = Playlist.ParseM3u(text);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("/media/one.mkv", parsed.Items[0].Path);
        Assert.Equal("/media/two.mp4", parsed.Items[1].Path);
        Assert.Equal(0, parsed.CurrentIndex);
    }

    [Fact]
    public void ParseM3uReadsExtinfTitlesAndSkipsComments()
    {
        const string text = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXTINF:-1,First movie
            C:\media\first.mkv
            # a comment

            relative/second.mp4
            """;

        Playlist playlist = Playlist.ParseM3u(text);

        Assert.Equal(2, playlist.Count);
        Assert.Equal("First movie", playlist.Items[0].Title);
        Assert.Equal(@"C:\media\first.mkv", playlist.Items[0].Path);
        Assert.Null(playlist.Items[1].Title);
    }

    [Fact]
    public void ParseM3uEmptyTextYieldsEmptyPlaylist()
    {
        Assert.Empty(Playlist.ParseM3u(null).Items);
        Assert.Empty(Playlist.ParseM3u(string.Empty).Items);
    }

    [Fact]
    public void SaveAndLoadM3uRoundTrips()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "list.m3u");
        Playlist playlist = Playlist.FromPaths(new[] { "/one", "/two" });

        playlist.SaveM3u(path);
        Playlist loaded = Playlist.LoadM3u(path);

        Assert.True(File.Exists(path));
        Assert.Equal(
            new[] { "/one", "/two" },
            loaded.Items.Select(item => item.Path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void RepeatAndShuffleAreStored()
    {
        var playlist = new Playlist
        {
            RepeatMode = PlaylistRepeatMode.One,
            Shuffle = true,
        };

        Assert.Equal(PlaylistRepeatMode.One, playlist.RepeatMode);
        Assert.True(playlist.Shuffle);
    }
}
