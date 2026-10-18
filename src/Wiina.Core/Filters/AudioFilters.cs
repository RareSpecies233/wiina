using System.Globalization;

namespace Wiina.Core.Filters;

/// <summary>10-band graphic equalizer constants and preset tables.</summary>
public static class EqualizerBands
{
    public static readonly IReadOnlyList<int> Frequencies =
        new[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static readonly IReadOnlyList<string> Labels =
        new[] { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    public const double MinimumGain = -12;

    public const double MaximumGain = 12;

    public const int BandCount = 10;

    public const string CustomPreset = "自定义";

    public static readonly IReadOnlyList<string> PresetNames = new[]
    {
        "Flat", "Rock", "Jazz", "Classical", "Pop", "Bass", "Treble", "Vocal",
    };

    private static readonly Dictionary<string, double[]> Presets = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"] = new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        ["Rock"] = new double[] { 5, 4, 2, 0, -1, 0, 2, 3, 4, 4 },
        ["Jazz"] = new double[] { 3, 2, 1, 2, -1, -1, 0, 1, 2, 3 },
        ["Classical"] = new double[] { 4, 3, 2, 1, -1, -1, 0, 2, 3, 4 },
        ["Pop"] = new double[] { -1, 0, 2, 4, 4, 2, 0, -1, -1, -1 },
        ["Bass"] = new double[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
        ["Treble"] = new double[] { 0, 0, 0, 0, 0, 0, 2, 4, 5, 6 },
        ["Vocal"] = new double[] { -2, -1, 0, 2, 4, 4, 2, 0, -1, -2 },
    };

    public static double ClampGain(double gain) =>
        Math.Clamp(double.IsFinite(gain) ? gain : 0, MinimumGain, MaximumGain);

    public static IReadOnlyList<double> CreatePreset(string? name) =>
        Presets.TryGetValue(name ?? string.Empty, out double[]? gains)
            ? gains.ToArray()
            : new double[BandCount];

    /// <summary>Pads/truncates arbitrary JSON input to exactly ten clamped bands.</summary>
    public static List<double> NormalizeGains(IEnumerable<double>? gains)
    {
        var result = new List<double>(BandCount);
        if (gains is not null)
        {
            foreach (double gain in gains.Take(BandCount))
            {
                result.Add(ClampGain(gain));
            }
        }

        while (result.Count < BandCount)
        {
            result.Add(0);
        }

        return result;
    }

    public static bool MatchesPreset(IReadOnlyList<double>? gains, string name)
    {
        if (!Presets.TryGetValue(name, out double[]? preset) ||
            gains is null ||
            gains.Count != BandCount)
        {
            return false;
        }

        for (int i = 0; i < BandCount; i++)
        {
            if (Math.Abs(ClampGain(gains[i]) - preset[i]) > 0.001)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns the first preset name matching the gains, or 自定义.</summary>
    public static string DetectPreset(IReadOnlyList<double>? gains)
    {
        foreach (string name in PresetNames)
        {
            if (MatchesPreset(gains, name))
            {
                return name;
            }
        }

        return CustomPreset;
    }

    /// <summary>
    /// Builds ten chained FFmpeg parametric EQ biquads. This libmpv build
    /// exposes the ffmpeg <c>equalizer</c> filter (f/t/w/g), not the old mpv
    /// ten-band <c>g0..g9</c> options, so each band is one biquad.
    /// </summary>
    public static string BuildGraph(bool enabled, IReadOnlyList<double>? gains)
    {
        List<double> normalized = NormalizeGains(gains);
        if (!enabled)
        {
            return string.Empty;
        }

        var filters = new List<string>();
        for (int i = 0; i < BandCount; i++)
        {
            double gain = normalized[i];
            if (Math.Abs(gain) < 0.05)
            {
                continue;
            }

            filters.Add(
                $"equalizer=f={Frequencies[i]}:t=q:w=1:g=" +
                gain.ToString("0.#", CultureInfo.InvariantCulture));
        }

        return string.Join(',', filters);
    }
}

/// <summary>Additional audio filters supported by this libmpv build.</summary>
public enum AudioFilterKind
{
    Loudnorm,
    Dynaudnorm,
}

public sealed class AudioFilterSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public AudioFilterKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    public double TargetLoudness { get; set; } = -16;

    public double TruePeak { get; set; } = -1.5;

    public double LoudnessRange { get; set; } = 11;

    public int FrameLength { get; set; } = 150;

    public int GaussianSize { get; set; } = 15;

    public double Peak { get; set; } = 0.95;

    public double MaxGain { get; set; } = 10;

    public AudioFilterSetting Clone() => (AudioFilterSetting)MemberwiseClone();
}

public sealed class AudioFilterPreset
{
    public string Name { get; set; } = string.Empty;

    public List<double> EqualizerGains { get; set; } = new();

    public bool EqualizerEnabled { get; set; } = true;

    public List<AudioFilterSetting> Filters { get; set; } = new();
}

public static class AudioFilterCatalog
{
    public sealed record KindInfo(AudioFilterKind Kind, string Name, string Description);

    public static readonly IReadOnlyList<KindInfo> All = new[]
    {
        new KindInfo(AudioFilterKind.Loudnorm, "响度标准化 (loudnorm)",
            "按 EBU R128 目标响度标准化；双声道以上会占用更多 CPU。"),
        new KindInfo(AudioFilterKind.Dynaudnorm, "动态音量 (dynaudnorm)",
            "压缩过响/过轻片段；适合对白与纪录片。"),
    };

    public static KindInfo Info(AudioFilterKind kind) =>
        All.First(info => info.Kind == kind);

    public static AudioFilterSetting Create(AudioFilterKind kind) => new() { Kind = kind };

    public static string Describe(AudioFilterSetting filter) => filter.Kind switch
    {
        AudioFilterKind.Loudnorm =>
            $"I {filter.TargetLoudness:0.#} LUFS · TP {filter.TruePeak:0.#} dB",
        AudioFilterKind.Dynaudnorm =>
            $"帧 {filter.FrameLength} ms · 最大增益 {filter.MaxGain:0.#} dB",
        _ => string.Empty,
    };

    /// <summary>Filters this build has no support for, surfaced in the UI.</summary>
    public static IReadOnlyList<string> Unsupported { get; } = new[]
    {
        "pitch / rubberband：本机 libmpv 构建未编译 rubberband，无法提供变调。",
    };
}

public sealed record AudioFilterGraphResult(
    string Graph,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Applied);

/// <summary>Builds the mpv <c>af</c> graph from the EQ and extra audio filters.</summary>
public static class AudioFilterGraph
{
    public static AudioFilterGraphResult Build(
        bool equalizerEnabled,
        IReadOnlyList<double>? equalizerGains,
        IReadOnlyList<AudioFilterSetting>? filters)
    {
        var graph = new List<string>();
        var errors = new List<string>();
        var applied = new List<string>();
        var seen = new HashSet<AudioFilterKind>();

        string eq = EqualizerBands.BuildGraph(equalizerEnabled, equalizerGains);
        if (eq.Length > 0)
        {
            graph.Add(eq);
            applied.Add("10 段均衡器");
        }

        foreach (AudioFilterSetting filter in filters ?? Array.Empty<AudioFilterSetting>())
        {
            if (!filter.Enabled)
            {
                continue;
            }

            string name = AudioFilterCatalog.Info(filter.Kind).Name;
            if (!seen.Add(filter.Kind))
            {
                errors.Add($"{name}：同一滤镜只能添加一次");
                continue;
            }

            string? expression = filter.Kind switch
            {
                AudioFilterKind.Loudnorm => BuildLoudnorm(filter, errors),
                AudioFilterKind.Dynaudnorm => BuildDynaudnorm(filter, errors),
                _ => null,
            };

            if (!string.IsNullOrEmpty(expression))
            {
                graph.Add(expression);
                applied.Add(name);
            }
        }

        return new AudioFilterGraphResult(string.Join(',', graph), errors, applied);
    }

    private static string? BuildLoudnorm(AudioFilterSetting filter, List<string> errors)
    {
        if (filter.TargetLoudness < -70 || filter.TargetLoudness > -5)
        {
            errors.Add("loudnorm：目标响度必须在 -70–-5 LUFS");
            return null;
        }

        if (filter.TruePeak < -9 || filter.TruePeak > 0)
        {
            errors.Add("loudnorm：真峰值必须在 -9–0 dB");
            return null;
        }

        if (filter.LoudnessRange < 1 || filter.LoudnessRange > 50)
        {
            errors.Add("loudnorm：响度范围必须在 1–50 LU");
            return null;
        }

        return $"loudnorm=I={N(filter.TargetLoudness)}:TP={N(filter.TruePeak)}:" +
            $"LRA={N(filter.LoudnessRange)}";
    }

    private static string? BuildDynaudnorm(AudioFilterSetting filter, List<string> errors)
    {
        if (filter.FrameLength < 10 || filter.FrameLength > 8000)
        {
            errors.Add("dynaudnorm：帧长必须在 10–8000 ms");
            return null;
        }

        if (filter.GaussianSize < 3 || filter.GaussianSize > 301)
        {
            errors.Add("dynaudnorm：高斯窗口必须在 3–301");
            return null;
        }

        return $"dynaudnorm=f={filter.FrameLength}:g={filter.GaussianSize}:" +
            $"p={N(filter.Peak)}:m={N(filter.MaxGain)}";
    }

    private static string N(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
