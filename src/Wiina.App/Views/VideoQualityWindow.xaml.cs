using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Wiina.App.Services;
using Wiina.Core.Models;
using Wiina.Core.Settings;

namespace Wiina.App.Views;

/// <summary>Named mpv scaler/deband profiles with JSON import/export.</summary>
public sealed partial class VideoQualityWindow : Window
{
    private static readonly string[] Scalers =
    {
        "bilinear", "bicubic", "lanczos", "ewa_lanczos", "ewa_lanczossharp",
        "ewa_lanczos4sharpest", "spline36", "mitchell", "catmull_rom",
    };

    private readonly AppSettings _settings = App.Settings;
    private List<VideoQualityProfile> _profiles = new();
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
        WindowShell.Prepare(this, 760, 520, resizable: true, dark: _settings.Theme != ThemeMode.Light,
            minWidth: 620, minHeight: 420, extendTitleBar: true, titleBar: DragRegion);
        ScaleBox.ItemsSource = Scalers;
        DscaleBox.ItemsSource = Scalers;
        CscaleBox.ItemsSource = Scalers;
        BuildProfiles();
        Closed += (_, _) => WindowShell.Detach(this);
    }

    private void BuildProfiles()
    {
        _loading = true;
        _profiles = new List<VideoQualityProfile>
        {
            new() { Name = "自动（IINA 默认）", Scale = "ewa_lanczos", Dscale = "mitchell", Cscale = "bilinear", Deband = true },
            new() { Name = "均衡", Scale = "ewa_lanczos", Dscale = "bilinear", Cscale = "bilinear", Deband = true },
            new() { Name = "高质量", Scale = "ewa_lanczossharp", Dscale = "mitchell", Cscale = "bilinear", Deband = true },
            new() { Name = "性能优先", Scale = "bilinear", Dscale = "bilinear", Cscale = "bilinear", Deband = false },
        };
        _profiles.AddRange(_settings.UserQualityProfiles.Select(Clone));
        ProfileBox.ItemsSource = _profiles.Select(profile => profile.Name).ToList();
        string active = _settings.ActiveQualityProfile;
        int index = _profiles.FindIndex(profile =>
            string.Equals(profile.Name, active, StringComparison.OrdinalIgnoreCase));
        ProfileBox.SelectedIndex = index >= 0 ? index : 0;
        _loading = false;
        LoadEditor(_profiles[ProfileBox.SelectedIndex]);
    }

    private static VideoQualityProfile Clone(VideoQualityProfile profile) => new()
    {
        Name = profile.Name,
        Scale = profile.Scale,
        Dscale = profile.Dscale,
        Cscale = profile.Cscale,
        Deband = profile.Deband,
    };

    private void LoadEditor(VideoQualityProfile profile)
    {
        _editing = Clone(profile);
        ScaleBox.SelectedItem = Scalers.Contains(profile.Scale) ? profile.Scale : Scalers[0];
        DscaleBox.SelectedItem = Scalers.Contains(profile.Dscale) ? profile.Dscale : Scalers[0];
        CscaleBox.SelectedItem = Scalers.Contains(profile.Cscale) ? profile.Cscale : Scalers[0];
        DebandSwitch.IsOn = profile.Deband;
    }

    private bool IsBuiltIn(string name) => _profiles
        .Take(4)
        .Any(profile => string.Equals(profile.Name, name, StringComparison.Ordinal));

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedIndex < 0 || ProfileBox.SelectedIndex >= _profiles.Count)
        {
            return;
        }

        LoadEditor(_profiles[ProfileBox.SelectedIndex]);
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
            Scale = ScaleBox.SelectedItem as string ?? "bilinear",
            Dscale = DscaleBox.SelectedItem as string ?? "bilinear",
            Cscale = CscaleBox.SelectedItem as string ?? "bilinear",
            Deband = DebandSwitch.IsOn,
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
        if (IsBuiltIn(oldName))
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
        if (ProfileBox.SelectedIndex < 0 || IsBuiltIn(_profiles[ProfileBox.SelectedIndex].Name))
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
                _settings.UserQualityProfiles.RemoveAll(existing =>
                    string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                _settings.UserQualityProfiles.Add(Clone(profile));
            }

            App.SaveSettings();
            BuildProfiles();
            StatusText.Text = $"已导入 {imported.Count} 个画质预设。";
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

            List<VideoQualityProfile> userProfiles = _profiles.Skip(4).Select(Clone).ToList();
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
            Scale = ScaleBox.SelectedItem as string ?? "bilinear",
            Dscale = DscaleBox.SelectedItem as string ?? "bilinear",
            Cscale = CscaleBox.SelectedItem as string ?? "bilinear",
            Deband = DebandSwitch.IsOn,
        };

        if (IsBuiltIn(name))
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
        StatusText.Text = $"已应用“{name}”。";
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
