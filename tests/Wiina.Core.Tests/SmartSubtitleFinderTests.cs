using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wiina.Core.Subtitles;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class SmartSubtitleFinderTests
{
    [Fact]
    public void FindsChineseAssFontsSidecarFromTestLibrary()
    {
        using var folder = new TempFolder();
        string video = folder.File(
            "[SweetSub&VCB-Studio] 16bit Sensation Another Layer [01][Ma10p_1080p][x265_flac].mkv");
        folder.File(
            "[SweetSub&VCB-Studio] 16bit Sensation Another Layer [01][Ma10p_1080p][x265_flac].chs.assfonts.ass");

        SubtitleCandidate? best = SmartSubtitleFinder.FindBest(video);

        Assert.NotNull(best);
        Assert.EndsWith(".chs.assfonts.ass", best!.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SubtitleLanguage.SimplifiedChinese, best.Language);
        Assert.True(best.IsAssFonts);
        Assert.False(best.IsExactBase);
    }

    [Fact]
    public void PrefersSimplifiedThenTraditionalAndAssFontsLast()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.sc.ass");
        folder.File("Movie.tc.ass");
        folder.File("Movie.sc.assfonts.ass");
        folder.File("Movie.tc.assfonts.ass");

        Assert.EndsWith("Movie.sc.ass", SmartSubtitleFinder.FindBest(video)!.FileName);

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);
        Assert.Equal(4, all.Count);
        Assert.False(all[0].IsAssFonts);
        Assert.False(all[1].IsAssFonts);
        Assert.True(all[2].IsAssFonts);
        Assert.True(all[3].IsAssFonts);
        Assert.Equal(SubtitleLanguage.SimplifiedChinese, all[0].Language);
        Assert.Equal(SubtitleLanguage.TraditionalChinese, all[1].Language);
    }

    [Fact]
    public void PreferredLanguageWinsWithinTheSameTier()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.sc.ass");
        folder.File("Movie.tc.ass");

        Assert.EndsWith(
            "Movie.tc.ass",
            SmartSubtitleFinder.FindBest(video, SubtitleLanguagePreferences.TraditionalChinese)!.FileName);
        Assert.EndsWith(
            "Movie.sc.ass",
            SmartSubtitleFinder.FindBest(video, SubtitleLanguagePreferences.SimplifiedChinese)!.FileName);
    }

    [Fact]
    public void UnknownPreferenceFallsBackToSimplifiedOrder()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.tc.ass");
        folder.File("Movie.sc.ass");

        Assert.EndsWith("Movie.sc.ass", SmartSubtitleFinder.FindBest(video, "klingon")!.FileName);
        Assert.EndsWith("Movie.sc.ass", SmartSubtitleFinder.FindBest(video, null)!.FileName);
    }

    [Fact]
    public void ExactBaseBeatsLanguageTags()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.srt");
        folder.File("Movie.chs.ass");

        Assert.EndsWith("Movie.srt", SmartSubtitleFinder.FindBest(video)!.FileName);
    }

    [Fact]
    public void AssExtensionBeatsOtherExtensionsOnTie()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.srt");
        folder.File("Movie.ass");

        Assert.EndsWith("Movie.ass", SmartSubtitleFinder.FindBest(video)!.FileName);
    }

    [Fact]
    public void AssFontsIsAlwaysTheFallback()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.tc.ass");
        folder.File("Movie.sc.assfonts.ass");

        string traditional = folder.File("Movie.tc.ass");
        Assert.EndsWith(
            "Movie.tc.ass",
            SmartSubtitleFinder.FindBest(video, SubtitleLanguagePreferences.SimplifiedChinese)!.FileName);

        File.Delete(traditional);
        folder.File("Movie.tc.assfonts.ass");
        Assert.EndsWith(
            "Movie.sc.assfonts.ass",
            SmartSubtitleFinder.FindBest(video, SubtitleLanguagePreferences.SimplifiedChinese)!.FileName);
    }

    [Fact]
    public void MatchesCaseInsensitivelyAndWithDottedBaseNames()
    {
        using var folder = new TempFolder();
        string video = folder.File("My.Movie.2024.MKV");
        folder.File("my.movie.2024.CHS.ASS");

        SubtitleCandidate? best = SmartSubtitleFinder.FindBest(video);

        Assert.NotNull(best);
        Assert.Equal(SubtitleLanguage.SimplifiedChinese, best!.Language);
        Assert.False(best.IsExactBase);
    }

    [Fact]
    public void SupportsLanguageTagsWithDashes()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.zh-Hans.ass");
        folder.File("Movie.zh-Hant.ass");

        Assert.EndsWith(
            "Movie.zh-Hans.ass",
            SmartSubtitleFinder.FindBest(video)!.FileName);
        Assert.EndsWith(
            "Movie.zh-Hant.ass",
            SmartSubtitleFinder.FindBest(video, "zh-Hant")!.FileName);
    }

    [Fact]
    public void IgnoresUnrelatedFilesAndUnknownExtensions()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.chs.ass");
        folder.File("Movie2.ass");
        folder.File("Other.chs.ass");
        folder.File("Movie.txt");
        folder.File("Movie.mkv.part");

        IReadOnlyList<SubtitleCandidate> found = SmartSubtitleFinder.Find(video);

        Assert.Single(found);
        Assert.EndsWith("Movie.chs.ass", found[0].FileName);
    }

    [Fact]
    public void IdxRequiresCompanionSubAndSubIsHiddenWhenIdxExists()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.idx");
        folder.File("Movie.sub");

        IReadOnlyList<SubtitleCandidate> found = SmartSubtitleFinder.Find(video);

        Assert.Single(found);
        Assert.EndsWith("Movie.idx", found[0].FileName);
    }

    [Fact]
    public void IdxWithoutSubIsSkipped()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.idx");

        Assert.Empty(SmartSubtitleFinder.Find(video));
    }

    [Fact]
    public void OrderingIsDeterministic()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.en.srt");
        folder.File("Movie.ass");
        folder.File("Movie.chs.ass");
        folder.File("Movie.ja.ass");

        string[] first = SmartSubtitleFinder.Find(video).Select(item => item.FileName).ToArray();
        string[] second = SmartSubtitleFinder.Find(video).Select(item => item.FileName).ToArray();

        Assert.Equal(first, second);
        Assert.Equal("Movie.ass", first[0]);
        Assert.Equal("Movie.chs.ass", first[1]);
        Assert.Equal("Movie.en.srt", first[2]);
        Assert.Equal("Movie.ja.ass", first[3]);
    }

    [Fact]
    public void MissingOrInvalidPathReturnsEmpty()
    {
        Assert.Empty(SmartSubtitleFinder.Find(null));
        Assert.Empty(SmartSubtitleFinder.Find(string.Empty));
        Assert.Empty(SmartSubtitleFinder.Find(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "x.mkv")));
        Assert.Null(SmartSubtitleFinder.FindBest(null));
    }

    [Theory]
    [InlineData("chs", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("sc", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("zh-CN", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("chi", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("zho", SubtitleLanguage.SimplifiedChinese)]
    [InlineData("tc", SubtitleLanguage.TraditionalChinese)]
    [InlineData("cht", SubtitleLanguage.TraditionalChinese)]
    [InlineData("zh-Hant", SubtitleLanguage.TraditionalChinese)]
    [InlineData("zh-TW", SubtitleLanguage.TraditionalChinese)]
    [InlineData("en", SubtitleLanguage.English)]
    [InlineData("eng", SubtitleLanguage.English)]
    [InlineData("ja", SubtitleLanguage.Japanese)]
    [InlineData("jpn", SubtitleLanguage.Japanese)]
    [InlineData("assfonts", SubtitleLanguage.Unknown)]
    public void ClassifiesLanguageTokens(string token, SubtitleLanguage expected)
    {
        Assert.Equal(expected, SmartSubtitleFinder.ClassifyTokens(new[] { token }));
    }

    [Fact]
    public void LanguagePreferenceAliasesNormalize()
    {
        Assert.Equal(SubtitleLanguagePreferences.SimplifiedChinese, SubtitleLanguagePreferences.Normalize("sc"));
        Assert.Equal(SubtitleLanguagePreferences.SimplifiedChinese, SubtitleLanguagePreferences.Normalize("zh-CN"));
        Assert.Equal(SubtitleLanguagePreferences.TraditionalChinese, SubtitleLanguagePreferences.Normalize("tc"));
        Assert.Equal(SubtitleLanguagePreferences.English, SubtitleLanguagePreferences.Normalize("ENG"));
        Assert.Equal(SubtitleLanguagePreferences.Japanese, SubtitleLanguagePreferences.Normalize("jp"));
        Assert.Equal(SubtitleLanguagePreferences.Auto, SubtitleLanguagePreferences.Normalize(null));
        Assert.Equal(SubtitleLanguagePreferences.Auto, SubtitleLanguagePreferences.Normalize("system"));
        Assert.Equal(5, SubtitleLanguagePreferences.All.Count);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wiina-subtitle-tests",
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
