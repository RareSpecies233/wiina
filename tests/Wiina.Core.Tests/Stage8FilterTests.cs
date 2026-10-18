using Wiina.Core.Diagnostics;
using Wiina.Core.Filters;
using Wiina.Core.Settings;
using Wiina.Core.Subtitles;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class Stage8FilterTests
{
    // ----------------------------------------------------------- video graph

    [Fact]
    public void VideoGraphPreservesListOrder()
    {
        var filters = new List<VideoFilterSetting>
        {
            VideoFilterCatalog.Create(VideoFilterKind.Sharpen),
            VideoFilterCatalog.Create(VideoFilterKind.Denoise),
            VideoFilterCatalog.Create(VideoFilterKind.Deband),
        };
        filters[0].Amount = 0.8;
        filters[1].Strength = 4;
        filters[2].Threshold = 0.02;

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            filters, 1920, 1080, VideoFilterLegacy.None);

        Assert.Empty(result.Errors);
        Assert.Equal(
            "unsharp=5:5:0.8,hqdn3d=4:3:6:4.5," +
            "deband=1thr=0.02:2thr=0.02:3thr=0.02:4thr=0.02",
            result.Graph);
        Assert.Equal(3, result.Applied.Count);
    }

    [Fact]
    public void VideoGraphBuildsColorAdjustAndHue()
    {
        VideoFilterSetting color = VideoFilterCatalog.Create(VideoFilterKind.ColorAdjust);
        color.Brightness = 0.1;
        color.Contrast = 1.2;
        color.Saturation = 1.3;
        color.Gamma = 1.1;
        color.Hue = 15;

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[] { color }, 0, 0, VideoFilterLegacy.None);

        Assert.Empty(result.Errors);
        Assert.Equal(
            "eq=brightness=0.1:contrast=1.2:saturation=1.3:gamma=1.1,hue=h=15",
            result.Graph);
    }

    [Fact]
    public void VideoGraphSkipsDisabledFilters()
    {
        VideoFilterSetting sharpen = VideoFilterCatalog.Create(VideoFilterKind.Sharpen);
        sharpen.Enabled = false;
        VideoFilterSetting flip = VideoFilterCatalog.Create(VideoFilterKind.FlipHorizontal);

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[] { sharpen, flip }, 0, 0, VideoFilterLegacy.None);

        Assert.Equal("hflip", result.Graph);
    }

    [Theory]
    [InlineData(VideoFilterKind.Rotate, 90, "transpose=1")]
    [InlineData(VideoFilterKind.Rotate, 180, "hflip,vflip")]
    [InlineData(VideoFilterKind.Rotate, 270, "transpose=2")]
    public void RotateUsesTranspose(VideoFilterKind kind, int degrees, string expected)
    {
        VideoFilterSetting filter = VideoFilterCatalog.Create(kind);
        filter.Rotation = degrees;

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[] { filter }, 0, 0, VideoFilterLegacy.None);

        Assert.Equal(expected, result.Graph);
    }

    [Fact]
    public void LegacyCropDelogoAndFlipsKeepStageTwoOrder()
    {
        var legacy = new VideoFilterLegacy(
            FlipHorizontal: true,
            FlipVertical: true,
            CropAspect: 4.0 / 3.0,
            Rotation: 0,
            Deinterlace: false,
            DelogoEnabled: true,
            DelogoX: 10,
            DelogoY: 20,
            DelogoWidth: 100,
            DelogoHeight: 50);

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[] { VideoFilterCatalog.Create(VideoFilterKind.Sharpen) },
            1920,
            1080,
            legacy);

        Assert.Empty(result.Errors);
        Assert.StartsWith("crop=1440:1080:240:0,", result.Graph);
        Assert.Contains("delogo=x=10:y=20:w=100:h=50", result.Graph);
        Assert.EndsWith("hflip,vflip", result.Graph);
    }

    [Fact]
    public void InvalidCropIsReportedAndPlaybackGraphStillBuilds()
    {
        VideoFilterSetting crop = VideoFilterCatalog.Create(VideoFilterKind.Crop);
        crop.X = 2000;
        crop.Width = 400;
        crop.Height = 400;
        VideoFilterSetting sharpen = VideoFilterCatalog.Create(VideoFilterKind.Sharpen);
        sharpen.Amount = 0.5;

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[] { crop, sharpen }, 1920, 1080, VideoFilterLegacy.None);

        Assert.Single(result.Errors);
        Assert.Contains("超出", result.Errors[0]);
        Assert.Equal("unsharp=5:5:0.5", result.Graph);
    }

    [Fact]
    public void DuplicateFilterKindsAreRejected()
    {
        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[]
            {
                VideoFilterCatalog.Create(VideoFilterKind.Sharpen),
                VideoFilterCatalog.Create(VideoFilterKind.Sharpen),
            },
            0,
            0,
            VideoFilterLegacy.None);

        Assert.Single(result.Errors);
        Assert.Contains("只能添加一次", result.Errors[0]);
        Assert.Equal("unsharp=5:5:0.5", result.Graph);
    }

    [Fact]
    public void LegacyToggleConflictsAreReported()
    {
        var legacy = new VideoFilterLegacy(
            true, false, 0, 90, true, true, 0, 0, 320, 80);

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            new[]
            {
                VideoFilterCatalog.Create(VideoFilterKind.FlipHorizontal),
                VideoFilterCatalog.Create(VideoFilterKind.Rotate),
                VideoFilterCatalog.Create(VideoFilterKind.Deinterlace),
                VideoFilterCatalog.Create(VideoFilterKind.Delogo),
            },
            0,
            0,
            legacy);

        Assert.Equal(4, result.Errors.Count);
        Assert.Equal("delogo=x=0:y=0:w=320:h=80,hflip", result.Graph);
    }

    [Fact]
    public void EmptyListProducesEmptyGraph()
    {
        VideoFilterGraphResult result = VideoFilterGraph.Build(
            Array.Empty<VideoFilterSetting>(), 0, 0, null);

        Assert.Equal(string.Empty, result.Graph);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void FilterDescriptionIncludesParameters()
    {
        VideoFilterSetting crop = VideoFilterCatalog.Create(VideoFilterKind.Crop);
        crop.Width = 640;
        crop.Height = 360;
        crop.X = 20;
        crop.Y = 10;

        Assert.Equal("640×360 @ 20,10", VideoFilterCatalog.Describe(crop));
    }

    // ------------------------------------------------------------ equalizer

    [Fact]
    public void EqualizerHasTenDocumentedBands()
    {
        Assert.Equal(10, EqualizerBands.BandCount);
        Assert.Equal(
            new[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 },
            EqualizerBands.Frequencies);
        Assert.Equal(10, EqualizerBands.NormalizeGains(null).Count);
    }

    [Theory]
    [InlineData(-20, -12)]
    [InlineData(0, 0)]
    [InlineData(8.5, 8.5)]
    [InlineData(20, 12)]
    [InlineData(double.NaN, 0)]
    public void EqualizerGainsAreClamped(double input, double expected)
    {
        Assert.Equal(expected, EqualizerBands.ClampGain(input));
    }

    [Fact]
    public void EqualizerPresetsRoundTripAndClamp()
    {
        IReadOnlyList<double> rock = EqualizerBands.CreatePreset("Rock");
        Assert.Equal(10, rock.Count);
        Assert.True(EqualizerBands.MatchesPreset(rock, "Rock"));
        Assert.Equal("Rock", EqualizerBands.DetectPreset(rock));

        List<double> gains = EqualizerBands.CreatePreset("Bass").ToList();
        gains[0] = 40;
        gains = EqualizerBands.NormalizeGains(gains);
        Assert.Equal(12, gains[0]);
        Assert.Equal(EqualizerBands.CustomPreset, EqualizerBands.DetectPreset(gains));
    }

    [Fact]
    public void EqualizerGraphUsesFfmpegParametricBands()
    {
        var gains = new double[] { -12, -6, 0, 3, 6, 9, 12, 1.5, -1.5, 0.5 };

        string graph = EqualizerBands.BuildGraph(true, gains);

        Assert.Contains("equalizer=f=31:t=q:w=1:g=-12", graph);
        Assert.Contains("equalizer=f=16000:t=q:w=1:g=0.5", graph);
        Assert.DoesNotContain("g0=", graph);
        Assert.Equal(9, graph.Split(',').Length);
        Assert.Equal(string.Empty, EqualizerBands.BuildGraph(false, gains));
        Assert.Equal(string.Empty, EqualizerBands.BuildGraph(true, new double[10]));
    }

    [Fact]
    public void AudioGraphCombinesEqAndNormalizers()
    {
        var loudnorm = new AudioFilterSetting
        {
            Kind = AudioFilterKind.Loudnorm,
            TargetLoudness = -16,
            TruePeak = -1.5,
            LoudnessRange = 11,
        };
        var dynaudnorm = new AudioFilterSetting
        {
            Kind = AudioFilterKind.Dynaudnorm,
            FrameLength = 150,
            GaussianSize = 15,
            Peak = 0.95,
            MaxGain = 10,
        };

        AudioFilterGraphResult result = AudioFilterGraph.Build(
            true,
            new double[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new[] { loudnorm, dynaudnorm });

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Applied.Count);
        Assert.StartsWith("equalizer=f=31:t=q:w=1:g=1,loudnorm=I=-16", result.Graph);
        Assert.Contains("loudnorm=I=-16:TP=-1.5:LRA=11", result.Graph);
        Assert.Contains("dynaudnorm=f=150:g=15:p=0.95:m=10", result.Graph);
    }

    [Fact]
    public void AudioGraphReportsInvalidLoudnormAndKeepsEq()
    {
        var loudnorm = new AudioFilterSetting
        {
            Kind = AudioFilterKind.Loudnorm,
            TargetLoudness = -90,
        };

        AudioFilterGraphResult result = AudioFilterGraph.Build(
            true, new double[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, new[] { loudnorm });

        Assert.Single(result.Errors);
        Assert.Contains("loudnorm", result.Errors[0]);
        Assert.StartsWith("equalizer=f=31:t=q:w=1:g=2", result.Graph);
    }

    [Fact]
    public void DisabledEqualizerAndFiltersProduceEmptyGraph()
    {
        AudioFilterGraphResult result = AudioFilterGraph.Build(
            false,
            new double[10],
            new[] { new AudioFilterSetting { Kind = AudioFilterKind.Loudnorm, Enabled = false } });

        Assert.Equal(string.Empty, result.Graph);
        Assert.Empty(result.Errors);
    }

    // ------------------------------------------------------------- settings

    [Fact]
    public void SettingsCatalogExposesStage8Rows()
    {
        string[] ids =
        {
            "video.filters", "video.tone-mapping", "video.hdr-compute-peak",
            "video.hwdec-mode", "video.output-levels", "video.delogo-enabled",
            "video.delogo-x", "video.delogo-y", "video.delogo-width",
            "video.delogo-height", "audio.equalizer", "audio.equalizer-preset",
            "audio.equalizer-reset", "audio.filters", "subtitles.ass-override",
            "subtitles.bold", "subtitles.italic", "subtitles.image-warning",
        };

        foreach (string id in ids)
        {
            SettingsRowDefinition? row = SettingsCatalog.Find(id);
            Assert.NotNull(row);
            Assert.True(string.IsNullOrEmpty(row.UnavailableReason), id);
        }
    }

    [Fact]
    public void Stage8SettingsRowsRoundTrip()
    {
        var settings = new AppSettings();

        SettingsRowDefinition mapping = SettingsCatalog.Find("video.tone-mapping")!;
        mapping.Set!(settings, 1);
        Assert.Equal("bt.2390", settings.VideoToneMapping);

        SettingsRowDefinition peak = SettingsCatalog.Find("video.target-peak")!;
        peak.Set!(settings, 120);
        Assert.Equal(120, settings.VideoTargetPeak);

        SettingsRowDefinition ass = SettingsCatalog.Find("subtitles.ass-override")!;
        ass.Set!(settings, 0);
        Assert.Equal("no", settings.SubtitleAssOverride);

        SettingsRowDefinition eq = SettingsCatalog.Find("audio.equalizer")!;
        eq.Set!(settings, true);
        Assert.True(settings.EqualizerEnabled);

        SettingsRowDefinition preset = SettingsCatalog.Find("audio.equalizer-preset")!;
        preset.Set!(settings, 0);
        Assert.Equal("Flat", settings.EqualizerPreset);
        Assert.All(settings.EqualizerGains, gain => Assert.Equal(0, gain));
    }

    [Fact]
    public void EqualizerPresetRowCopiesPresetGains()
    {
        var settings = new AppSettings();
        SettingsRowDefinition preset = SettingsCatalog.Find("audio.equalizer-preset")!;
        preset.Set!(settings, 5);
        Assert.Equal("Bass", settings.EqualizerPreset);
        Assert.True(EqualizerBands.MatchesPreset(settings.EqualizerGains, "Bass"));
        Assert.Equal(10, settings.EqualizerGains.Count);
    }

    // ------------------------------------------------------------ inspector

    [Fact]
    public void InspectorFormatsTechnicalUnits()
    {
        Assert.Equal("1.5 GiB", InspectorFormatter.Bytes(1610612736));
        Assert.Equal("—", InspectorFormatter.Bytes(0));
        Assert.Equal("3840 × 2160", InspectorFormatter.Resolution(3840, 2160));
        Assert.Equal("23.976 fps", InspectorFormatter.FrameRate(23.976));
        Assert.Equal("8.2 Mb/s", InspectorFormatter.BitRate(8_200_000));
        Assert.Equal("48 kHz", InspectorFormatter.SampleRate(48000));
    }

    [Fact]
    public void InspectorFormatsColourAndHdr()
    {
        string colour = InspectorFormatter.Colour(
            "bt2020", "pq", "bt2020nc", "limited", 1000, 0.005, 1000);
        Assert.Contains("bt2020", colour);
        Assert.Contains("峰值 1000", colour);
        Assert.Equal("HDR", InspectorFormatter.DynamicRange("pq", 1000));
        Assert.Equal("SDR", InspectorFormatter.DynamicRange("bt1886", 100));
    }

    [Theory]
    [InlineData("pgs", true, false)]
    [InlineData("dvd_subtitle", true, false)]
    [InlineData("ass", false, true)]
    [InlineData("subrip", false, false)]
    public void InspectorClassifiesSubtitleCodecs(string codec, bool image, bool ass)
    {
        Assert.Equal(image, InspectorFormatter.IsImageSubtitle(codec));
        Assert.Equal(ass, InspectorFormatter.IsAssSubtitle(codec));
    }

    // ----------------------------------------------------- subtitle advisory

    [Theory]
    [InlineData("pgs", SubtitleStyleIssue.Image, false)]
    [InlineData("dvd_subtitle", SubtitleStyleIssue.Image, false)]
    [InlineData("ass", SubtitleStyleIssue.Ass, true)]
    [InlineData("subrip", SubtitleStyleIssue.None, true)]
    public void SubtitleAdvisoryMatchesCodec(
        string codec,
        SubtitleStyleIssue issue,
        bool allowStyle)
    {
        SubtitleStyleAdvice advice = SubtitleStyleAdvisory.Analyze(codec);
        Assert.Equal(issue, advice.Issue);
        Assert.Equal(allowStyle, advice.AllowStyleOverride);
        if (issue == SubtitleStyleIssue.None)
        {
            Assert.Empty(advice.Message);
        }
        else
        {
            Assert.NotEmpty(advice.Message);
        }
    }

    [Fact]
    public void ImageSubtitleWarningMentionsBitmapLimitation()
    {
        SubtitleStyleAdvice advice = SubtitleStyleAdvisory.Analyze("hdmv_pgs_subtitle");
        Assert.Contains("图像字幕", advice.Message);
        Assert.Contains("无法调整", advice.Message);
    }
}
