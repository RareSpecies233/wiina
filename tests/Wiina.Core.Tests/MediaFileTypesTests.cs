using Xunit;
using Wiina.Core.Files;

namespace Wiina.Core.Tests;

public sealed class MediaFileTypesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "wiina-filetype-tests",
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

    [Theory]
    [InlineData("movie.mkv", MediaKind.Video)]
    [InlineData("clip.MP4", MediaKind.Video)]
    [InlineData("stream.webm", MediaKind.Video)]
    [InlineData("song.flac", MediaKind.Audio)]
    [InlineData("song.MP3", MediaKind.Audio)]
    [InlineData("captions.srt", MediaKind.Subtitle)]
    [InlineData("captions.ASS", MediaKind.Subtitle)]
    [InlineData("list.m3u", MediaKind.Playlist)]
    [InlineData("list.m3u8", MediaKind.Playlist)]
    [InlineData("notes.txt", MediaKind.Unknown)]
    [InlineData("no-extension", MediaKind.Unknown)]
    [InlineData(null, MediaKind.Unknown)]
    public void ClassifyUsesExtensionCaseInsensitively(
        string? path,
        MediaKind expected)
    {
        Assert.Equal(expected, MediaFileTypes.Classify(path));
    }

    [Theory]
    [InlineData("movie.mkv", true)]
    [InlineData("song.flac", true)]
    [InlineData("captions.srt", false)]
    [InlineData("list.m3u", false)]
    [InlineData("notes.txt", false)]
    public void IsPlayableMediaOnlyAllowsAudioVideo(string path, bool expected)
    {
        Assert.Equal(expected, MediaFileTypes.IsPlayableMedia(path));
    }

    [Fact]
    public void SubtitleAndPlaylistHelpersAgree()
    {
        Assert.True(MediaFileTypes.IsSubtitle("captions.srt"));
        Assert.False(MediaFileTypes.IsSubtitle("movie.mkv"));
        Assert.True(MediaFileTypes.IsPlaylist("list.m3u8"));
        Assert.False(MediaFileTypes.IsPlaylist("movie.mkv"));
        Assert.True(MediaFileTypes.IsMediaFile("captions.vtt"));
        Assert.False(MediaFileTypes.IsMediaFile("notes.txt"));
    }

    [Fact]
    public void GetExtensionLowercasesAndKeepsTheDot()
    {
        Assert.Equal(".mkv", MediaFileTypes.GetExtension(@"C:\media\Movie.MKV"));
        Assert.Equal(string.Empty, MediaFileTypes.GetExtension(null));
        Assert.Equal(string.Empty, MediaFileTypes.GetExtension("no-extension"));
    }

    [Fact]
    public void ExpandFolderFiltersSupportedFilesAndSorts()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(_directory, "b.mp4"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "a.mkv"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "captions.srt"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "list.m3u"), string.Empty);
        File.WriteAllText(
            Path.Combine(_directory, "nested", "c.webm"),
            string.Empty);

        IReadOnlyList<string> recursive = MediaFileTypes.ExpandFolder(_directory);

        Assert.Equal(4, recursive.Count);
        Assert.Equal(
            new[] { "a.mkv", "b.mp4", "list.m3u", "c.webm" },
            recursive.Select(Path.GetFileName));
    }

    [Fact]
    public void ExpandFolderSortsEpisodesNaturally()
    {
        Directory.CreateDirectory(_directory);
        foreach (string name in new[] { "EP10.mkv", "EP2.mkv", "EP1.mkv" })
        {
            File.WriteAllText(Path.Combine(_directory, name), string.Empty);
        }

        IReadOnlyList<string> files = MediaFileTypes.ExpandFolder(
            _directory,
            recursive: false);

        Assert.Equal(
            new[] { "EP1.mkv", "EP2.mkv", "EP10.mkv" },
            files.Select(Path.GetFileName));
    }

    [Fact]
    public void ExpandNonRecursiveSkipsSubfolders()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(_directory, "a.mkv"), string.Empty);
        File.WriteAllText(
            Path.Combine(_directory, "nested", "c.webm"),
            string.Empty);

        IReadOnlyList<string> top = MediaFileTypes.ExpandFolder(
            _directory,
            recursive: false);

        Assert.Single(top);
        Assert.EndsWith("a.mkv", top[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandMissingFolderReturnsEmpty()
    {
        Assert.Empty(MediaFileTypes.ExpandFolder(
            Path.Combine(_directory, "missing")));
    }

    [Fact]
    public void FilterPlayableKeepsOrderAndDropsNonMedia()
    {
        string[] input =
        {
            "/b.mp4",
            "/captions.srt",
            "/a.mkv",
            "/notes.txt",
        };

        Assert.Equal(
            new[] { "/b.mp4", "/a.mkv" },
            MediaFileTypes.FilterPlayable(input));
    }
}
