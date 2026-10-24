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

/// <summary>Named mpv scaler/deband profiles with JSON import/export.</summary>
public sealed partial class VideoQualityWindow : Window
{
    private const int BuiltInCount = 2;
    private const string DefaultScaler = "bilinear";

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
        new("bilinear", "Bilinear",
            "最快，仅建议弱 GPU", "最快，缩小略软", "默认，开销最低（推荐）",
            "最省 GPU 的线性插值，画面偏软；弱 GPU、高帧率或 4K 高刷时选它。"),
        new("bicubic", "Bicubic",
            "平滑，开销低", "略锐，开销低", "色彩边缘更清晰",
            "平滑且开销小，细节略软；日常缩小与色度缩放的稳妥选择。"),
        new("lanczos", "Lanczos",
            "锐利，通用", "锐利，缩小可能闪烁", "锐利，开销中等",
            "经典 Lanczos：锐利、开销适中，不确定时可用。"),
        new("ewa_lanczos", "EWA Lanczos",
            "高质量缩放（推荐）", "高质量（开销大）", "高质量（开销大）",
            "EWA Lanczos：锐利且抗锯齿，IINA 默认；适合大多数放大场景。"),
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
            "平滑，略软", "平滑，缩小首选（推荐）", "平滑，开销低",
            "Mitchell-Netravali：平滑不易闪烁，缩小的常用默认。"),
        new("catmull_rom", "Catmull-Rom",
            "略锐，可能振铃", "略锐，可能振铃", "略锐，可能振铃",
            "比 Mitchell 更锐，边缘可能出现轻微振铃。"),
    };

    // Display order: recommended first, then alternative quality picks, then the cheapest.
    private static readonly string[] UpscaleOrder =
    {
        "ewa_lanczos", "lanczos", "spline36", "ewa_lanczossharp", "ewa_lanczos4sharpest",
        "bicubic", "mitchell", "catmull_rom", "bilinear",
    };

    private static readonly string[] DownscaleOrder =
    {
        "mitchell", "bicubic", "bilinear", "lanczos", "spline36",
        "catmull_rom", "ewa_lanczos", "ewa_lanczossharp", "ewa_lanczos4sharpest",
    };

    private static readonly string[] ChromaOrder =
    {
        "bilinear", "bicubic", "spline36", "lanczos", "mitchell",
        "catmull_rom", "ewa_lanczos", "ewa_lanczossharp", "ewa_lanczos4sharpest",
    };

    private static readonly IReadOnlyList<ScalerOption> UpscaleOptions =
        BuildOptions(UpscaleOrder, spec => spec.UpscaleRole);

    private static readonly IReadOnlyList<ScalerOption> DownscaleOptions =
        BuildOptions(DownscaleOrder, spec => spec.DownscaleRole);

    private static readonly IReadOnlyList<ScalerOption> ChromaOptions =
        BuildOptions(ChromaOrder, spec => spec.ChromaRole);

    // Two built-ins only (1.2.4): the default IINA/libmpv combination and the
    // cheapest all-bilinear profile. Deband is an independent toggle on the
    // Video settings page, so profiles no longer carry it at runtime.
    private static readonly (VideoQualityProfile Profile, string Hint)[] BuiltIns =
    {
        (new VideoQualityProfile
            {
                Name = "默认",
                Scale = "ewa_lanczos",
                Dscale = "mitchell",
                Cscale = "bilinear",
            },
            "对齐 IINA/libmpv 默认：EWA Lanczos 放大 + Mitchell 缩小，清晰且开销适中，适合大多数片源。内置只读，点“应用”会另存为自定义副本。"),
        (new VideoQualityProfile
            {
                Name = "性能优先",
                Scale = "bilinear",
                Dscale = "bilinear",
                Cscale = "bilinear",
            },
            "全部使用 Bilinear：开销最低，适合弱 GPU、低功耗设备或高帧率片源。内置只读，点“应用”会另存为自定义副本。"),
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
        RootGrid.RequestedTheme = _settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        WindowShell.Prepare(this, 780, 620, resizable: true, dark: _settings.Theme != ThemeMode.Light,
            minWidth: 620, minHeight: 420, extendTitleBar: true, titleBar: DragRegion);
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

    private static string NormalizeRaw(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? DefaultScaler : raw.Trim();

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
                : new ProfileOption(profile.Name, "自定义预设：修改下方缩放器后点“应用”，保存并立即生效。"))
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
        Deband = profile.Deband,
    };

    /// <summary>
    /// Maps the names the 1.2.3 build used for its "automatic" preset onto the
    /// 1.2.4 "默认" name, so old exported JSON keeps working after import.
    /// </summary>
    private static string NormalizeProfileName(string name)
    {
        string trimmed = name.Trim();
        return trimmed is "自动（IINA 默认）" or "自动" or "IINA 默认" or "IINA默认"
            or "Auto" or "Auto (IINA default)" or "IINA (default)"
            ? "默认"
            : trimmed;
    }

    private void LoadEditor(VideoQualityProfile profile)
    {
        bool previous = _loading;
        _loading = true;
        try
        {
            _editing = Clone(profile);
            SelectScaler(ScaleBox, UpscaleOptions, profile.Scale);
            SelectScaler(DscaleBox, DownscaleOptions, profile.Dscale);
            SelectScaler(CscaleBox, ChromaOptions, profile.Cscale);
        }
        finally
        {
            _loading = previous;
        }

        UpdateRawKeyLine();
    }

    /// <summary>
    /// Fills a ComboBox with the purpose-specific options and selects the option whose raw key
    /// matches <paramref name="raw"/>. Unknown keys (e.g. from an imported JSON) get a placeholder
    /// entry that keeps the original value instead of silently replacing it.
    /// </summary>
    private static void SelectScaler(ComboBox box, IReadOnlyList<ScalerOption> options, string? raw)
    {
        string value = NormalizeRaw(raw);
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

    private static string SelectedRaw(ComboBox box) =>
        box.SelectedItem is ScalerOption option ? option.Raw : DefaultScaler;

    private void UpdateRawKeyLine()
    {
        RawKeyText.Text =
            $"mpv 原始键：scale={SelectedRaw(ScaleBox)} · dscale={SelectedRaw(DscaleBox)} · " +
            $"cscale={SelectedRaw(CscaleBox)}（点“应用”后写入 mpv）";
    }

    private void UpdateProfileHint()
    {
        int index = ProfileBox.SelectedIndex;
        ProfileHintText.Text = index >= 0 && index < _profileEntries.Count
            ? _profileEntries[index].Hint
            : string.Empty;
    }

    private void OnScalerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateRawKeyLine();
        }
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

        _settings.UserQualityProfiles.Add(new VideoQualityProfile
        {
            Name = name,
            Scale = SelectedRaw(ScaleBox),
            Dscale = SelectedRaw(DscaleBox),
            Cscale = SelectedRaw(CscaleBox),
            // Kept false so exported JSON never claims to control deband;
            // AppSettings.DebandEnabled is the single runtime source of truth.
            Deband = false,
        });
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
            List<VideoQualityProfile>? imported = JsonSerializer.Deserialize<List<VideoQualityProfile>>(json);
            if (imported is null || imported.Count == 0)
            {
                StatusText.Text = "文件中没有可导入的画质预设。";
                return;
            }

            foreach (VideoQualityProfile profile in imported.Where(profile => !string.IsNullOrWhiteSpace(profile.Name)))
            {
                // Pre-1.2.4 exports may still use the old names of the built-in
                // profiles; map them onto the current two so "apply" keeps
                // working, and avoid shadowing a built-in display name.
                profile.Name = NormalizeProfileName(profile.Name);
                if (BuiltIns.Any(builtIn => string.Equals(
                        builtIn.Profile.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.Name = $"{profile.Name}（导入）";
                }

                _settings.UserQualityProfiles.RemoveAll(existing =>
                    string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                _settings.UserQualityProfiles.Add(Clone(profile));
            }

            App.SaveSettings();
            BuildProfiles();
            int unknown = imported.Count(profile =>
                !IsKnownScaler(profile.Scale) ||
                !IsKnownScaler(profile.Dscale) ||
                !IsKnownScaler(profile.Cscale));
            StatusText.Text = unknown > 0
                ? $"已导入 {imported.Count} 个画质预设；其中 {unknown} 个含未知缩放器，原始键已保留，可在下拉框中查看。"
                : $"已导入 {imported.Count} 个画质预设。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导入失败：{ex.Message}";
        }
    }

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

        VideoQualityProfile profile = _profiles[ProfileBox.SelectedIndex];
        string name = profile.Name;
        var value = new VideoQualityProfile
        {
            Name = name,
            Scale = SelectedRaw(ScaleBox),
            Dscale = SelectedRaw(DscaleBox),
            Cscale = SelectedRaw(CscaleBox),
            // Preserve the flag an old JSON profile carried so a re-export
            // still round-trips, but never apply it from here: deband is the
            // independent Video-page toggle now.
            Deband = _editing.Deband,
        };

        if (ProfileBox.SelectedIndex < BuiltInCount)
        {
            name = $"{name}（自定义）";
            value.Name = name;
        }

        _settings.UserQualityProfiles.RemoveAll(existing =>
            string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        _settings.UserQualityProfiles.Add(value);
        _settings.ActiveQualityProfile = name;
        _settings.VideoQualityPreset = VideoQualityPreset.Auto;
        App.SaveSettings();
        App.ActiveWindow?.ApplyCurrentQualityProfile();
        BuildProfiles();
        bool unknown = !IsKnownScaler(value.Scale) ||
                       !IsKnownScaler(value.Dscale) ||
                       !IsKnownScaler(value.Cscale);
        StatusText.Text = unknown
            ? $"已应用“{name}”；其中包含未知缩放器，已按原始键保存。"
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
