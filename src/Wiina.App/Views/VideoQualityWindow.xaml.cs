using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Wiina.App.Services;
using Wiina.Core.Models;
using Wiina.Core.Settings;

namespace Wiina.App.Views;

/// <summary>
/// One selectable scaler option: the human-readable label shown in the ComboBox plus the
/// exact raw mpv property value that is persisted in <see cref="VideoQualityProfile"/>.
/// </summary>
public sealed class ScalerOption
{
    public ScalerOption(string raw, string display, string tooltip)
    {
        Raw = raw;
        Display = display;
        Tooltip = tooltip;
    }

    /// <summary>Raw mpv key persisted in the profile JSON (e.g. "ewa_lanczos").</summary>
    public string Raw { get; }

    /// <summary>Human-readable label, e.g. "Lanczos — 锐利，通用".</summary>
    public string Display { get; }

    /// <summary>Hover text: the raw mpv key plus a one-line explanation.</summary>
    public string Tooltip { get; }

    public override string ToString() => Display;

    /// <summary>Placeholder for a raw key Wiina does not know; the imported value is kept intact.</summary>
    public static ScalerOption Unknown(string raw) => new(
        raw,
        $"{raw} — 未识别的缩放器（保留原值）",
        $"mpv 原始键：{raw}\n该值不在 Wiina 的缩放器列表中（可能来自导入的 JSON）；会原样保存并交给 mpv，若 mpv 不接受则回退其默认值。");
}

/// <summary>
/// One value of a non-scaler choice option (dither, dither-depth, video-sync, tscale,
/// gpu-dumb-mode): display label plus the exact raw mpv value.
/// </summary>
public sealed class ChoiceOption
{
    public ChoiceOption(string raw, string display, string tooltip)
    {
        Raw = raw;
        Display = display;
        Tooltip = tooltip;
    }

    public string Raw { get; }

    public string Display { get; }

    public string Tooltip { get; }

    public override string ToString() => Display;

    /// <summary>Placeholder for an imported value Wiina does not know; kept verbatim.</summary>
    public static ChoiceOption Unknown(string raw) => new(
        raw,
        $"{raw} — 未识别的值（保留原值）",
        $"mpv 原始值：{raw}\n该值不在 Wiina 的选项列表中（可能来自导入的 JSON）；会原样保存并交给 mpv。");
}

/// <summary>One entry of the profile ComboBox: display text plus a "when to use" hint.</summary>
public sealed class ProfileOption
{
    public ProfileOption(string display, string hint)
    {
        Display = display;
        Hint = hint;
    }

    public string Display { get; }

    public string Hint { get; }

    public override string ToString() => Display;
}

/// <summary>
/// Named mpv video-quality profiles: 23 options grouped by purpose, all stored as raw
/// mpv keys, with JSON import/export that keeps unknown imported options intact.
/// </summary>
public sealed partial class VideoQualityWindow : Window
{
    private const int BuiltInCount = 2;

    /// <summary>One scaler in the mpv catalogue with per-purpose descriptions.</summary>
    private sealed record ScalerSpec(
        string Raw,
        string Friendly,
        string UpscaleRole,
        string DownscaleRole,
        string ChromaRole,
        string Note);

    private static readonly ScalerSpec[] ScalerSpecs =
    {
        new("hermite", "Hermite",
            "平滑，开销低", "平滑、不易闪烁（mpv 默认）", "平滑，开销低",
            "mpv 0.41 的缩小默认值：平滑、低开销，适合大多数缩小场景。"),
        new("bilinear", "Bilinear",
            "最快，仅建议弱 GPU", "最快，缩小略软", "mpv 默认，开销最低（推荐）",
            "最省 GPU 的线性插值，画面偏软；弱 GPU、高帧率或 4K 高刷时选它。"),
        new("bicubic", "Bicubic",
            "平滑，开销低", "略锐，开销低", "色彩边缘更清晰",
            "平滑且开销小，细节略软；日常缩小与色度缩放的稳妥选择。"),
        new("lanczos", "Lanczos",
            "锐利，通用（mpv 默认）", "锐利，缩小可能闪烁", "锐利，开销中等",
            "mpv 0.41 的放大默认值：锐利、开销适中，不确定时可用。"),
        new("ewa_lanczos", "EWA Lanczos",
            "高质量缩放，比默认更锐", "高质量（开销大）", "高质量（开销大）",
            "EWA Lanczos：锐利且抗锯齿，比默认 Lanczos 更吃 GPU；适合大多数放大场景。"),
        new("ewa_lanczossharp", "EWA Lanczos Sharp",
            "更锐利，开销略高", "更锐利（重负载）", "更锐利（重负载）",
            "在 EWA Lanczos 上加强锐度，细节更强、开销略高。"),
        new("ewa_lanczos4sharpest", "EWA Lanczos 4 Sharpest",
            "最锐利（重负载）", "最锐利（重负载）", "最锐利（重负载）",
            "最锐利的 EWA 变体，开销最高；适合高端 GPU 与低分辨率片源。"),
        new("spline36", "Spline36",
            "锐利且稳定", "锐利，振铃少", "锐利，振铃少",
            "Spline36：锐利、振铃少、开销中等，通用替代方案。"),
        new("mitchell", "Mitchell",
            "平滑，略软", "平滑，缩小首选（非默认）", "平滑，开销低",
            "Mitchell-Netravali：平滑不易闪烁；比 mpv 默认的 Hermite 略锐。"),
        new("catmull_rom", "Catmull-Rom",
            "略锐，可能振铃", "略锐，可能振铃", "略锐，可能振铃",
            "比 Mitchell 更锐，边缘可能出现轻微振铃。"),
    };

    // Display order: the mpv default first, then alternative quality picks, then the cheapest.
    private static readonly string[] UpscaleOrder =
    {
        "lanczos", "ewa_lanczos", "spline36", "ewa_lanczossharp", "ewa_lanczos4sharpest",
        "bicubic", "mitchell", "catmull_rom", "bilinear", "hermite",
    };

    private static readonly string[] DownscaleOrder =
    {
        "hermite", "mitchell", "bicubic", "bilinear", "lanczos", "spline36",
        "catmull_rom", "ewa_lanczos", "ewa_lanczossharp", "ewa_lanczos4sharpest",
    };

    private static readonly string[] ChromaOrder =
    {
        "bilinear", "bicubic", "spline36", "lanczos", "mitchell",
        "catmull_rom", "hermite", "ewa_lanczos", "ewa_lanczossharp", "ewa_lanczos4sharpest",
    };

    private static readonly IReadOnlyList<ScalerOption> UpscaleOptions =
        BuildOptions(UpscaleOrder, spec => spec.UpscaleRole);

    private static readonly IReadOnlyList<ScalerOption> DownscaleOptions =
        BuildOptions(DownscaleOrder, spec => spec.DownscaleRole);

    private static readonly IReadOnlyList<ScalerOption> ChromaOptions =
        BuildOptions(ChromaOrder, spec => spec.ChromaRole);

    private static readonly IReadOnlyList<ChoiceOption> DitherOptions = new[]
    {
        new ChoiceOption("fruit", "Fruit — 默认，画质最好",
            "mpv 键：dither\nmpv 默认。用伪随机噪点抖动，渐变最平滑，开销适中。"),
        new ChoiceOption("ordered", "Ordered — 低开销",
            "mpv 键：dither\n有序抖动，开销低，静态渐变上可能出现规则纹理。"),
        new ChoiceOption("error-diffusion", "Error diffusion — 更平滑，较慢",
            "mpv 键：dither\n误差扩散，过渡最平滑，但 GPU 开销更高。"),
        new ChoiceOption("no", "No — 关闭（最省，可能出现色带）",
            "mpv 键：dither\n关闭抖动，最省 GPU；8bit 渐变可能出现可见色带。"),
    };

    private static readonly IReadOnlyList<ChoiceOption> DitherDepthOptions = new[]
    {
        new ChoiceOption("auto", "Auto — 跟随输出格式（默认）",
            "mpv 键：dither-depth\nmpv 默认。按显示输出实际位深自动选择抖动目标。"),
        new ChoiceOption("no", "No — 关闭抖动",
            "mpv 键：dither-depth\n完全关闭抖动，等同于 dither=no。"),
        new ChoiceOption("8", "8 bit",
            "mpv 键：dither-depth\n按 8bit 输出抖动，适合普通 SDR 显示器。"),
        new ChoiceOption("10", "10 bit",
            "mpv 键：dither-depth\n按 10bit 输出抖动，适合 10bit 面板 / HDR。"),
        new ChoiceOption("12", "12 bit",
            "mpv 键：dither-depth\n按 12bit 输出抖动；少数高位深输出使用。"),
    };

    private static readonly IReadOnlyList<ChoiceOption> TscaleOptions = new[]
    {
        new ChoiceOption("oversample", "Oversample — 默认，最快",
            "mpv 键：tscale\nmpv 默认。在时间轴上直接重采样，最省 GPU。"),
        new ChoiceOption("linear", "Linear — 平滑，低开销",
            "mpv 键：tscale\n线性时间混合，平滑且便宜。"),
        new ChoiceOption("mitchell", "Mitchell — 平滑",
            "mpv 键：tscale\nMitchell 时间核，平滑、振铃少，开销中等。"),
        new ChoiceOption("lanczos", "Lanczos — 锐利，较重",
            "mpv 键：tscale\nLanczos 时间核，动态更锐利，但更吃 GPU。"),
        new ChoiceOption("spline36", "Spline36 — 锐利",
            "mpv 键：tscale\nSpline36 时间核，锐利、振铃少。"),
        new ChoiceOption("bicubic", "Bicubic — 折中",
            "mpv 键：tscale\nBicubic 时间核，平滑与锐度的折中。"),
    };

    private static readonly IReadOnlyList<ChoiceOption> VideoSyncOptions = new[]
    {
        new ChoiceOption("audio", "Audio — 默认，以音轨为准",
            "mpv 键：video-sync\nmpv 默认。按音频时钟播放，最省资源，本地播放首选。"),
        new ChoiceOption("display-resample", "Display resample — 跟随刷新率",
            "mpv 键：video-sync\n让视频贴合屏幕刷新率并重采样音频；更顺滑，但增加 GPU 与音频开销。"),
        new ChoiceOption("display-resample-vdrop", "Display resample + vdrop — 更稳，可能丢帧",
            "mpv 键：video-sync\n在 display-resample 基础上允许丢帧，避免音画不同步。"),
        new ChoiceOption("display-tempo", "Display tempo — 调速同步",
            "mpv 键：video-sync\n通过改变播放速度贴合显示；速度变化时音调可能变化。"),
        new ChoiceOption("desync", "Desync — 不强制同步，最省",
            "mpv 键：video-sync\n不强制音画同步，最省资源；长时间播放可能漂移。"),
    };

    private static readonly IReadOnlyList<ChoiceOption> GpuDumbModeOptions = new[]
    {
        new ChoiceOption("auto", "Auto — 默认",
            "mpv 键：gpu-dumb-mode\nmpv 默认，按后端自动决定。"),
        new ChoiceOption("yes", "Yes — 精简，省 GPU",
            "mpv 键：gpu-dumb-mode\n关闭高级渲染路径以省 GPU；部分着色器与字幕特效会失效。"),
        new ChoiceOption("no", "No — 完整渲染",
            "mpv 键：gpu-dumb-mode\n强制完整渲染路径，画质功能齐全，开销更高。"),
    };

    // Two built-ins: mpv's own defaults (also IINA's, since IINA does not override
    // video scaler defaults) and the cheapest coherent all-bilinear profile.
    private static readonly (VideoQualityProfile Profile, string Hint)[] BuiltIns =
    {
        (VideoQualityProfiles.CreateDefault(),
            "与 mpv / IINA 的默认值一致：Lanczos 放大、Hermite 缩小、Bilinear 色度，其余均为 mpv 默认。内置只读，点“应用”会另存为自定义副本。"),
        (VideoQualityProfiles.CreatePerformance(),
            "最省 GPU 的一组：三个平面都用 Bilinear，关闭抗振铃 / 正确缩小 / 插帧 / 去色带，并关闭抖动。内置只读，点“应用”会另存为自定义副本。"),
    };

    private readonly AppSettings _settings = App.Settings;
    private List<VideoQualityProfile> _profiles = new();
    private List<ProfileOption> _profileEntries = new();
    private VideoQualityProfile _editing = new();
    private bool _loading;

    public VideoQualityWindow()
    {
        InitializeComponent();
        Title = "画质预设";
        RootGrid.RequestedTheme = App.ResolveTheme();
        WindowShell.Prepare(this, 840, 780, resizable: true, dark: App.IsDarkTheme,
            minWidth: 720, minHeight: 480, extendTitleBar: true, titleBar: DragRegion);
        BuildProfiles();
        Closed += (_, _) => WindowShell.Detach(this);
    }

    private static IReadOnlyList<ScalerOption> BuildOptions(
        string[] order,
        Func<ScalerSpec, string> role)
    {
        return order.Select(raw =>
        {
            ScalerSpec spec = ScalerSpecs.First(item =>
                string.Equals(item.Raw, raw, StringComparison.Ordinal));
            return new ScalerOption(
                spec.Raw,
                $"{spec.Friendly} — {role(spec)}",
                $"{spec.Friendly}（mpv 键：{spec.Raw}）\n{spec.Note}");
        }).ToArray();
    }

    private static string NormalizeRaw(string? raw, string fallback) =>
        string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();

    private static bool IsKnownScaler(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ||
        ScalerSpecs.Any(spec => string.Equals(spec.Raw, raw.Trim(), StringComparison.OrdinalIgnoreCase));

    private void BuildProfiles()
    {
        _loading = true;
        _profiles = BuiltIns.Select(builtIn => Clone(builtIn.Profile)).ToList();
        _profiles.AddRange(_settings.UserQualityProfiles.Select(Clone));
        _profileEntries = _profiles
            .Select((profile, index) => index < BuiltInCount
                ? new ProfileOption($"{profile.Name} · 内置", BuiltIns[index].Hint)
                : new ProfileOption(profile.Name, "自定义预设：修改下方选项后点“应用”，保存并立即生效。"))
            .ToList();
        ProfileBox.ItemsSource = _profileEntries;
        string active = _settings.ActiveQualityProfile;
        int index = _profiles.FindIndex(profile =>
            string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase));
        ProfileBox.SelectedIndex = index >= 0 ? index : 0;
        _loading = false;
        LoadEditor(_profiles[ProfileBox.SelectedIndex >= 0 ? ProfileBox.SelectedIndex : 0]);
        UpdateProfileHint();
    }

    private static VideoQualityProfile Clone(VideoQualityProfile profile) => new()
    {
        Name = profile.Name,
        Scale = profile.Scale,
        Dscale = profile.Dscale,
        Cscale = profile.Cscale,
        ScaleAntiring = profile.ScaleAntiring,
        DscaleAntiring = profile.DscaleAntiring,
        CorrectDownscaling = profile.CorrectDownscaling,
        LinearDownscaling = profile.LinearDownscaling,
        SigmoidUpscaling = profile.SigmoidUpscaling,
        CscaleAntiring = profile.CscaleAntiring,
        Dither = profile.Dither,
        DitherDepth = profile.DitherDepth,
        DitherSizeFruit = profile.DitherSizeFruit,
        TemporalDither = profile.TemporalDither,
        Deband = profile.Deband,
        DebandIterations = profile.DebandIterations,
        DebandThreshold = profile.DebandThreshold,
        DebandRange = profile.DebandRange,
        DebandGrain = profile.DebandGrain,
        Interpolation = profile.Interpolation,
        Tscale = profile.Tscale,
        VideoSync = profile.VideoSync,
        VideoSyncMaxVideoChange = profile.VideoSyncMaxVideoChange,
        ScalerResizesOnly = profile.ScalerResizesOnly,
        GpuDumbMode = profile.GpuDumbMode,
        ExtraOptions = profile.ExtraOptions is null
            ? null
            : new Dictionary<string, JsonElement>(profile.ExtraOptions),
    };

    private void LoadEditor(VideoQualityProfile profile)
    {
        bool previous = _loading;
        _loading = true;
        try
        {
            _editing = Clone(profile);
            SelectScaler(ScaleBox, UpscaleOptions, profile.Scale, VideoQualityProfiles.DefaultScale);
            SelectScaler(DscaleBox, DownscaleOptions, profile.Dscale, VideoQualityProfiles.DefaultDscale);
            SelectScaler(CscaleBox, ChromaOptions, profile.Cscale, VideoQualityProfiles.DefaultCscale);
            SelectChoice(DitherBox, DitherOptions, profile.Dither, VideoQualityProfiles.DefaultDither);
            SelectChoice(DitherDepthBox, DitherDepthOptions, profile.DitherDepth,
                VideoQualityProfiles.DefaultDitherDepth);
            SelectChoice(TscaleBox, TscaleOptions, profile.Tscale, VideoQualityProfiles.DefaultTscale);
            SelectChoice(VideoSyncBox, VideoSyncOptions, profile.VideoSync,
                VideoQualityProfiles.DefaultVideoSync);
            SelectChoice(GpuDumbModeBox, GpuDumbModeOptions, profile.GpuDumbMode,
                VideoQualityProfiles.DefaultGpuDumbMode);

            SetNumber(ScaleAntiringBox, profile.ScaleAntiring);
            SetNumber(DscaleAntiringBox, profile.DscaleAntiring);
            SetNumber(CscaleAntiringBox, profile.CscaleAntiring);
            SetNumber(DitherSizeFruitBox, profile.DitherSizeFruit);
            SetNumber(DebandIterationsBox, profile.DebandIterations);
            SetNumber(DebandThresholdBox, profile.DebandThreshold);
            SetNumber(DebandRangeBox, profile.DebandRange);
            SetNumber(DebandGrainBox, profile.DebandGrain);
            SetNumber(VideoSyncMaxVideoChangeBox, profile.VideoSyncMaxVideoChange);

            CorrectDownscalingCheck.IsChecked = profile.CorrectDownscaling;
            LinearDownscalingCheck.IsChecked = profile.LinearDownscaling;
            SigmoidUpscalingCheck.IsChecked = profile.SigmoidUpscaling;
            TemporalDitherCheck.IsChecked = profile.TemporalDither;
            InterpolationCheck.IsChecked = profile.Interpolation;
            ScalerResizesOnlyCheck.IsChecked = profile.ScalerResizesOnly;
        }
        finally
        {
            _loading = previous;
        }

        UpdateUnknownOptionsText();
    }

    /// <summary>
    /// Fills a ComboBox with the purpose-specific options and selects the option whose raw key
    /// matches <paramref name="raw"/>. Unknown keys (e.g. from an imported JSON) get a placeholder
    /// entry that keeps the original value instead of silently replacing it.
    /// </summary>
    private static void SelectScaler(
        ComboBox box,
        IReadOnlyList<ScalerOption> options,
        string? raw,
        string fallback)
    {
        string value = NormalizeRaw(raw, fallback);
        List<ScalerOption> items = options.ToList();
        int index = items.FindIndex(option =>
            string.Equals(option.Raw, value, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            items.Add(ScalerOption.Unknown(value));
            index = items.Count - 1;
        }

        box.ItemsSource = items;
        box.SelectedIndex = index;
    }

    private static void SelectChoice(
        ComboBox box,
        IReadOnlyList<ChoiceOption> options,
        string? raw,
        string fallback)
    {
        string value = NormalizeRaw(raw, fallback);
        List<ChoiceOption> items = options.ToList();
        int index = items.FindIndex(option =>
            string.Equals(option.Raw, value, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            items.Add(ChoiceOption.Unknown(value));
            index = items.Count - 1;
        }

        box.ItemsSource = items;
        box.SelectedIndex = index;
    }

    private static string SelectedRaw(ComboBox box, string fallback) => box.SelectedItem switch
    {
        ScalerOption scaler => scaler.Raw,
        ChoiceOption choice => choice.Raw,
        _ => fallback,
    };

    private static void SetNumber(NumberBox box, double value) => box.Value = value;

    private static double GetNumber(NumberBox box, double fallback) =>
        double.IsNaN(box.Value) ? fallback : box.Value;

    private static int GetInt(NumberBox box, int fallback) =>
        double.IsNaN(box.Value) ? fallback : (int)Math.Round(box.Value);

    private static bool GetFlag(CheckBox box) => box.IsChecked == true;

    private VideoQualityProfile CollectProfile()
    {
        VideoQualityProfile defaults = VideoQualityProfiles.CreateDefault();
        return new VideoQualityProfile
        {
            Name = _editing.Name,
            Scale = SelectedRaw(ScaleBox, defaults.Scale),
            Dscale = SelectedRaw(DscaleBox, defaults.Dscale),
            Cscale = SelectedRaw(CscaleBox, defaults.Cscale),
            ScaleAntiring = GetNumber(ScaleAntiringBox, defaults.ScaleAntiring),
            DscaleAntiring = GetNumber(DscaleAntiringBox, defaults.DscaleAntiring),
            CorrectDownscaling = GetFlag(CorrectDownscalingCheck),
            LinearDownscaling = GetFlag(LinearDownscalingCheck),
            SigmoidUpscaling = GetFlag(SigmoidUpscalingCheck),
            CscaleAntiring = GetNumber(CscaleAntiringBox, defaults.CscaleAntiring),
            Dither = SelectedRaw(DitherBox, defaults.Dither),
            DitherDepth = SelectedRaw(DitherDepthBox, defaults.DitherDepth),
            DitherSizeFruit = GetInt(DitherSizeFruitBox, defaults.DitherSizeFruit),
            TemporalDither = GetFlag(TemporalDitherCheck),
            // Preserve the flag an old JSON profile carried so a re-export still
            // round-trips, but never apply it from here: deband is the independent
            // Video-page toggle now.
            Deband = _editing.Deband,
            DebandIterations = GetInt(DebandIterationsBox, defaults.DebandIterations),
            DebandThreshold = GetNumber(DebandThresholdBox, defaults.DebandThreshold),
            DebandRange = GetNumber(DebandRangeBox, defaults.DebandRange),
            DebandGrain = GetNumber(DebandGrainBox, defaults.DebandGrain),
            Interpolation = GetFlag(InterpolationCheck),
            Tscale = SelectedRaw(TscaleBox, defaults.Tscale),
            VideoSync = SelectedRaw(VideoSyncBox, defaults.VideoSync),
            VideoSyncMaxVideoChange = GetNumber(VideoSyncMaxVideoChangeBox, defaults.VideoSyncMaxVideoChange),
            ScalerResizesOnly = GetFlag(ScalerResizesOnlyCheck),
            GpuDumbMode = SelectedRaw(GpuDumbModeBox, defaults.GpuDumbMode),
            ExtraOptions = _editing.ExtraOptions is null
                ? null
                : new Dictionary<string, JsonElement>(_editing.ExtraOptions),
        };
    }

    private void UpdateUnknownOptionsText()
    {
        if (_editing.ExtraOptions is { Count: > 0 } extra)
        {
            string list = string.Join("，", extra.Select(pair => $"{pair.Key}={pair.Value.GetRawText()}"));
            UnknownOptionsText.Text =
                $"未识别的 mpv 选项（来自导入 JSON，已保留原值但不会应用）：{list}";
            UnknownOptionsText.Visibility = Visibility.Visible;
        }
        else
        {
            UnknownOptionsText.Text = string.Empty;
            UnknownOptionsText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateProfileHint()
    {
        int index = ProfileBox.SelectedIndex;
        ProfileHintText.Text = index >= 0 && index < _profileEntries.Count
            ? _profileEntries[index].Hint
            : string.Empty;
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedIndex < 0 || ProfileBox.SelectedIndex >= _profiles.Count)
        {
            return;
        }

        LoadEditor(_profiles[ProfileBox.SelectedIndex]);
        UpdateProfileHint();
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        string? name = await PromptAsync("新建画质预设", "我的画质");
        if (string.IsNullOrWhiteSpace(name) ||
            _profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        VideoQualityProfile value = CollectProfile();
        value.Name = name;
        _settings.UserQualityProfiles.Add(value);
        App.SaveSettings();
        BuildProfiles();
        ProfileBox.SelectedIndex = _profiles.FindIndex(profile => profile.Name == name);
        StatusText.Text = $"已创建“{name}”。";
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedIndex < 0)
        {
            return;
        }

        string oldName = _profiles[ProfileBox.SelectedIndex].Name;
        if (ProfileBox.SelectedIndex < BuiltInCount)
        {
            StatusText.Text = "内置预设不能重命名，请先用“新建”保存一份。";
            return;
        }

        string? name = await PromptAsync("重命名画质预设", oldName);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, oldName, StringComparison.Ordinal))
        {
            return;
        }

        VideoQualityProfile? saved = _settings.UserQualityProfiles.Find(profile =>
            string.Equals(profile.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            saved.Name = name;
        }

        if (string.Equals(_settings.ActiveQualityProfile, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ActiveQualityProfile = name;
        }

        App.SaveSettings();
        BuildProfiles();
        ProfileBox.SelectedIndex = _profiles.FindIndex(profile => profile.Name == name);
        StatusText.Text = $"已重命名为“{name}”。";
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedIndex < 0 || ProfileBox.SelectedIndex < BuiltInCount)
        {
            StatusText.Text = "内置预设不能删除。";
            return;
        }

        string name = _profiles[ProfileBox.SelectedIndex].Name;
        _settings.UserQualityProfiles.RemoveAll(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(_settings.ActiveQualityProfile, name, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ActiveQualityProfile = string.Empty;
        }

        App.SaveSettings();
        BuildProfiles();
        StatusText.Text = $"已删除“{name}”。";
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WindowShell.InitializePicker(this, picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            string json = await FileIO.ReadTextAsync(file);
            // Exported profiles use PascalCase property names; case-insensitive parsing
            // also accepts hand-written lower-case mpv option names without turning
            // every field into an "unknown" extension entry.
            List<VideoQualityProfile>? imported = JsonSerializer.Deserialize<List<VideoQualityProfile>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported is null || imported.Count == 0)
            {
                StatusText.Text = "文件中没有可导入的画质预设。";
                return;
            }

            int accepted = 0;
            int unknown = 0;
            // A hand-edited file may contain null entries; skip those instead of crashing.
            foreach (VideoQualityProfile profile in imported.Where(profile =>
                         profile is not null && !string.IsNullOrWhiteSpace(profile.Name)))
            {
                // Pre-1.2.4 exports may still use the old names of the built-in
                // profiles; map them onto the current two so "apply" keeps
                // working, and avoid shadowing a built-in display name.
                profile.Name = VideoQualityProfiles.NormalizeName(profile.Name);
                if (BuiltIns.Any(builtIn => string.Equals(
                        builtIn.Profile.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.Name = $"{profile.Name}（导入）";
                }

                _settings.UserQualityProfiles.RemoveAll(existing =>
                    string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                _settings.UserQualityProfiles.Add(Clone(profile));
                accepted++;
                if (HasUnknownOptions(profile))
                {
                    unknown++;
                }
            }

            App.SaveSettings();
            BuildProfiles();
            StatusText.Text = unknown > 0
                ? $"已导入 {accepted} 个画质预设；其中 {unknown} 个含未识别选项或缩放器，原始键已保留，可在编辑器中查看。"
                : $"已导入 {accepted} 个画质预设。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导入失败：{ex.Message}";
        }
    }

    private static bool HasUnknownOptions(VideoQualityProfile profile) =>
        profile.ExtraOptions is { Count: > 0 } ||
        !IsKnownScaler(profile.Scale) ||
        !IsKnownScaler(profile.Dscale) ||
        !IsKnownScaler(profile.Cscale);

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            picker.SuggestedFileName = "wiina-quality-profiles";
            WindowShell.InitializePicker(this, picker);
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            List<VideoQualityProfile> userProfiles = _profiles.Skip(BuiltInCount).Select(Clone).ToList();
            await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(
                userProfiles,
                new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"已导出 {userProfiles.Count} 个自定义预设。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导出失败：{ex.Message}";
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedIndex < 0)
        {
            return;
        }

        VideoQualityProfile profile = CollectProfile();
        string name = profile.Name;

        if (ProfileBox.SelectedIndex < BuiltInCount)
        {
            name = $"{name}（自定义）";
            profile.Name = name;
        }

        _settings.UserQualityProfiles.RemoveAll(existing =>
            string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.UserQualityProfiles.Add(profile);
        _settings.ActiveQualityProfile = name;
        _settings.VideoQualityPreset = VideoQualityPreset.Auto;
        App.SaveSettings();
        App.ActiveWindow?.ApplyCurrentQualityProfile();
        BuildProfiles();
        bool unknown = !IsKnownScaler(profile.Scale) ||
                       !IsKnownScaler(profile.Dscale) ||
                       !IsKnownScaler(profile.Cscale) ||
                       profile.ExtraOptions is { Count: > 0 };
        StatusText.Text = unknown
            ? $"已应用“{name}”；其中包含未识别选项，已按原始键保存。"
            : $"已应用“{name}”。";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async Task<string?> PromptAsync(string title, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 260 };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }
}
