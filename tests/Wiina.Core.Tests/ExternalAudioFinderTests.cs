using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wiina.Core.Audio;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class ExternalAudioFinderTests
{
    [Fact]
    public void FindsSameBaseMkaSidecar()
    {
        using var folder = new TempFolder();
        string video = folder.File(
            "[Airota&VCB-Studio] Gekijouban Hibike! Euphonium Chikai no Finale [Ma10p_1080p][x265_flac].mkv");
        folder.File(
            "[Airota&VCB-Studio] Gekijouban Hibike! Euphonium Chikai no Finale [Ma10p_1080p][x265_flac].mka");

        ExternalAudioCandidate? best = ExternalAudioFinder.FindBest(video);

        Assert.NotNull(best);
        Assert.EndsWith(".mka", best!.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(best.IsExactBase);
    }

    [Fact]
    public void PrefersExactBaseOverDottedSuffix()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.jpn.mka");
        folder.File("Movie.mka");

        IReadOnlyList<ExternalAudioCandidate> found = ExternalAudioFinder.Find(video);

        Assert.Equal(2, found.Count);
        Assert.Equal("Movie.mka", found[0].FileName);
        Assert.True(found[0].IsExactBase);
        Assert.False(found[1].IsExactBase);
    }

    [Fact]
    public void OnlyMkaIsAutoMatched()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.ac3");
        folder.File("Movie.flac");
        folder.File("Movie.mka");

        IReadOnlyList<ExternalAudioCandidate> found = ExternalAudioFinder.Find(video);

        Assert.Single(found);
        Assert.EndsWith(".mka", found[0].FileName);
    }

    [Fact]
    public void IgnoresUnrelatedAudio()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie2.mka");
        folder.File("Other.mka");
        folder.File("Movie.mka");

        IReadOnlyList<ExternalAudioCandidate> found = ExternalAudioFinder.Find(video);

        Assert.Single(found);
        Assert.Equal("Movie.mka", found[0].FileName);
    }

    [Fact]
    public void SupportsDottedBaseNamesAndCaseInsensitiveMatches()
    {
        using var folder = new TempFolder();
        string video = folder.File("My.Movie.2024.MKV");
        folder.File("my.movie.2024.MKA");

        ExternalAudioCandidate? best = ExternalAudioFinder.FindBest(video);

        Assert.NotNull(best);
        Assert.True(best!.IsExactBase);
    }

    [Fact]
    public void MissingPathReturnsEmpty()
    {
        Assert.Empty(ExternalAudioFinder.Find(null));
        Assert.Empty(ExternalAudioFinder.Find(string.Empty));
        Assert.Empty(ExternalAudioFinder.Find(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "x.mkv")));
        Assert.Null(ExternalAudioFinder.FindBest(null));
    }

    [Theory]
    [InlineData("a.mka", true)]
    [InlineData("a.MKA", true)]
    [InlineData("a.ac3", true)]
    [InlineData("a.flac", true)]
    [InlineData("a.opus", true)]
    [InlineData("a.m4a", true)]
    [InlineData("a.mkv", false)]
    [InlineData("a.ass", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void IsSupportedAudioChecksPickerExtensions(string path, bool expected)
    {
        Assert.Equal(expected, ExternalAudioFinder.IsSupportedAudio(path));
    }

    [Fact]
    public void PickerListCoversRequestedFormats()
    {
        string[] required =
        {
            ".mka", ".ac3", ".dts", ".aac", ".flac", ".opus", ".wav", ".mp3", ".m4a", ".ogg",
        };

        Assert.All(required, extension =>
            Assert.Contains(extension, ExternalAudioFinder.PickerExtensions));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wiina-audio-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort for the test sandbox.
            }
        }
    }
}
