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
    public void FindsLooseSidecarWhenVideoHasExtraReleaseTags()
    {
        using var folder = new TempFolder();
        string video = folder.File("[Group] Show [01][1080p][x265].mkv");
        folder.File("[Group] Show [01].chs.ass");

        SubtitleCandidate? best = SmartSubtitleFinder.FindBest(video);

        Assert.NotNull(best);
        Assert.EndsWith("[Group] Show [01].chs.ass", best!.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(best.IsExactBase);
    }

    [Fact]
    public void LooseMatchDoesNotCrossEpisodes()
    {
        using var folder = new TempFolder();
        string video = folder.File("[Group] Show [01][1080p].mkv");
        folder.File("[Group] Show [02].chs.ass");

        Assert.Null(SmartSubtitleFinder.FindBest(video));
    }

    [Fact]
    public void AssFontsVariantsComeFirstThenLanguageOrder()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.sc.ass");
        folder.File("Movie.tc.ass");
        folder.File("Movie.sc.assfonts.ass");
        folder.File("Movie.tc.assfonts.ass");

        // assfonts embeds its fonts, so it is tried first; language ordering only
        // breaks ties inside each tier.
        Assert.EndsWith("Movie.sc.assfonts.ass", SmartSubtitleFinder.FindBest(video)!.FileName);

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);
        Assert.Equal(4, all.Count);
        Assert.True(all[0].IsAssFonts);
        Assert.True(all[1].IsAssFonts);
        Assert.False(all[2].IsAssFonts);
        Assert.False(all[3].IsAssFonts);
        Assert.Equal(SubtitleLanguage.SimplifiedChinese, all[0].Language);
        Assert.Equal(SubtitleLanguage.TraditionalChinese, all[1].Language);
        Assert.Equal(SubtitleLanguage.SimplifiedChinese, all[2].Language);
        Assert.Equal(SubtitleLanguage.TraditionalChinese, all[3].Language);
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
    public void AssFontsWinsOverThePlainVariantOfTheSameLanguage()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.sc.ass");
        folder.File("Movie.sc.assfonts.ass");

        // The plain variant is listed but no longer hides the embedded-font one.
        IReadOnlyList<SubtitleCandidate> all =
            SmartSubtitleFinder.Find(video, SubtitleLanguagePreferences.SimplifiedChinese);
        Assert.Equal(2, all.Count);
        Assert.EndsWith("Movie.sc.assfonts.ass", all[0].FileName);
        Assert.EndsWith("Movie.sc.ass", all[1].FileName);
        Assert.True(all[0].IsAssFonts);
        Assert.False(all[1].IsAssFonts);
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

    [Fact]
    public void ListsEverySidecarOfTheSameEpisodeNotJustTheBestOne()
    {
        using var folder = new TempFolder();
        // Mirrors the reported folder: one real video and several sidecars that all
        // belong to episode 01. The subtitle menu must offer every one of them.
        string video = folder.File("[Group] Show [01][1080p][x265].mkv");
        folder.File("[Group] Show [01][1080p][x265].chs.assfonts.ass");
        folder.File("[Group] Show [01][1080p][x265].chs.ass");
        folder.File("[Group] Show [01][1080p][x265].en.srt");

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);

        Assert.Equal(3, all.Count);
        Assert.Equal(3, all.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(all[0].IsAssFonts);
        Assert.EndsWith(".chs.assfonts.ass", all[0].FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(all, item => item.FileName.EndsWith(".chs.ass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, item => item.FileName.EndsWith(".en.srt", StringComparison.OrdinalIgnoreCase));
        Assert.All(all, item => Assert.DoesNotContain("[02]", item.FileName));
    }

    [Fact]
    public void OtherEpisodesNeverEnterTheCandidateList()
    {
        using var folder = new TempFolder();
        string video = folder.File("[Group] Show [01][1080p].mkv");
        folder.File("[Group] Show [01].chs.ass");
        folder.File("[Group] Show [01].chs.assfonts.ass");
        folder.File("[Group] Show [02].chs.ass");
        folder.File("[Group] Show [02].chs.assfonts.ass");
        folder.File("[Group] Show [01][1080p].chs.ass");
        folder.File("[Group] Show [01][1080p].chs.assfonts.ass");

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);

        Assert.Equal(4, all.Count);
        Assert.All(all, item => Assert.StartsWith("[Group] Show [01]", item.FileName));
        Assert.All(all, item => Assert.DoesNotContain("[02]", item.FileName));
        Assert.True(all[0].IsAssFonts);
        Assert.True(all[1].IsAssFonts);
    }

    [Fact]
    public void PlainAndAssFontsVariantsAreBothKept()
    {
        using var folder = new TempFolder();
        string video = folder.File("Show [01].mkv");
        folder.File("Show [01].chs.ass");
        folder.File("Show [01].chs.assfonts.ass");

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);

        Assert.Equal(2, all.Count);
        Assert.True(all[0].IsAssFonts);
        Assert.False(all[1].IsAssFonts);
        Assert.Equal(
            new[] { "Show [01].chs.assfonts.ass", "Show [01].chs.ass" },
            all.Select(item => item.FileName).ToArray());
    }

    [Theory]
    [InlineData("Show [01][1080p]", "Show [01].chs.assfonts", true)]
    [InlineData("Show [01][1080p]", "Show [01][1080p].chs.assfonts", true)]
    [InlineData("Show [01][1080p]", "Show [02].chs.assfonts", false)]
    [InlineData("Show [01][1080p]", "Show [01] Extra.chs.ass", false)]
    [InlineData("Show [01][1080p]", "Other Show [01].chs.ass", false)]
    [InlineData("", "Show [01].chs.ass", false)]
    [InlineData("Show [01]", "", false)]
    public void SameEpisodeHelperRejectsOtherEpisodes(string videoBase, string subtitleStem, bool expected)
    {
        Assert.Equal(expected, SmartSubtitleFinder.IsSameEpisode(videoBase, subtitleStem));
    }

    [Fact]
    public void MatchTrackMapsMpvNamesBackToTheCandidate()
    {
        using var folder = new TempFolder();
        string video = folder.File("Show [01].mkv");
        folder.File("Show [01].chs.ass");
        string assfonts = folder.File("Show [01].chs.assfonts.ass");
        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);
        Assert.Equal(2, all.Count);

        Assert.Equal(assfonts, SmartSubtitleFinder.MatchTrack(all, assfonts)?.Path);
        Assert.Equal(assfonts, SmartSubtitleFinder.MatchTrack(all, Path.GetFileName(assfonts))?.Path);
        Assert.Equal(
            "Show [01].chs.ass",
            SmartSubtitleFinder.MatchTrack(all, "chs.ass")?.FileName);
        Assert.Equal(
            "Show [01].chs.assfonts.ass",
            SmartSubtitleFinder.MatchTrack(all, "chs.assfonts.ass")?.FileName);
        Assert.Null(SmartSubtitleFinder.MatchTrack(all, "deu.ass"));
        Assert.Null(SmartSubtitleFinder.MatchTrack(all, null));
        Assert.Null(SmartSubtitleFinder.MatchTrack(Array.Empty<SubtitleCandidate>(), "chs.ass"));
    }

    [Fact]
    public void IndexSubPairIsListedOnce()
    {
        using var folder = new TempFolder();
        string video = folder.File("Movie.mkv");
        folder.File("Movie.chs.idx");
        folder.File("Movie.chs.sub");

        IReadOnlyList<SubtitleCandidate> all = SmartSubtitleFinder.Find(video);

        Assert.Single(all);
        Assert.EndsWith(".idx", all[0].FileName, StringComparison.OrdinalIgnoreCase);
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
