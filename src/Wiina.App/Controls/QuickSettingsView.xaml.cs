using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Wiina.Core.Models;
using Wiina.App.Localization;
using Wiina.Core.Osd;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Core.Subtitles;
using Wiina.Mpv;

namespace Wiina.App.Controls;

public enum QuickSettingKey
{
    Theme,
    Animations,
    Layout,
    AlwaysOnTop,
    LockAspect,
    OscPosition,
    OscAutoHide,
    OsdEnabled,
    OsdPosition,
    OsdSize,
    OsdDuration,
    OsdPersistent,
    FullscreenClock,
    FullscreenBattery,
    FullscreenTitle,
    ThumbnailPreview,
    MpvStats,
    SidebarSide,
    SidebarWidth,
    VideoTrack,
    AudioTrack,
    SubtitleTrack,
    SecondarySubtitle,
    Aspect,
    Crop,
    Rotation,
    FlipHorizontal,
    FlipVertical,
    Deinterlace,
    HardwareDecode,
    Zoom,
    PanX,
    PanY,
    Volume,
    MaxVolume,
    AudioDelay,
    AudioDevice,
    Mute,
    SubtitleVisible,
    SubtitleDelay,
    SubtitlePosition,
    SubtitleScale,
    SubtitleFontSize,
    SubtitleColor,
    SubtitleBorder,
    EqualizerEnabled,
    EqualizerPreset,
    EqualizerGain,
    LoadSubtitle,
    RefreshAudioDevices,
    ResetCard,
}

public sealed class QuickSettingChangedEventArgs : EventArgs
{
    public QuickSettingKey Key { get; init; }

    public string? Text { get; init; }

    public double Number { get; init; }

    public bool Flag { get; init; }

    public IReadOnlyList<double>? Gains { get; init; }
}

/// <summary>
/// The four quick-settings pages (layout/video/audio/subtitles). The view owns
/// only control state and raises typed changes; <see cref="MainWindow"/> applies
/// them to mpv/settings and calls the Set* methods to refresh from caches.
/// </summary>
public sealed partial class QuickSettingsView : UserControl
{
    private readonly List<TrackInfo> _tracks = new();
    private bool _loading;
    private bool _hasMedia;
    private int _videoWidth;
    private int _videoHeight;
    private int? _secondarySubtitleId;
    private QuickSettingsPage _page = QuickSettingsPage.Video;

    public QuickSettingsView()
    {
        InitializeComponent();
        SetupStaticOptions();
        ShowPage(QuickSettingsPage.Video);
        UpdateRangeLabels();
        Loc.LanguageChanged += OnLanguageChanged;
        // Static event: unhook when this instance goes away so a closed window
        // is not kept alive by the localization layer.
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    public event EventHandler<QuickSettingChangedEventArgs>? SettingChanged;

    public QuickSettingsPage CurrentPage => _page;

    private void SetupStaticOptions()
    {
        // Set the range in code so Minimum is never applied while Maximum is
        // still at its default (WinUI rejects Minimum > Maximum at parse time).
        SidebarWidthSlider.Maximum = 500;
        SidebarWidthSlider.Minimum = 300;
        OsdSizeSlider.Maximum = 500;
        OsdSizeSlider.Minimum = 1;

        ApplyLocalizedChoices();

        AspectBox.ItemsSource = QuickSettings.AspectRatios;
        AspectBox.DisplayMemberPath = "Label";
        AspectBox.SelectedValuePath = "Value";

        CropBox.ItemsSource = QuickSettings.CropOptions;
        CropBox.DisplayMemberPath = "Label";
        CropBox.SelectedValuePath = "Aspect";

        RotationBox.ItemsSource = QuickSettings.Rotations;

        SubtitleColorBox.ItemsSource = QuickSettings.SubtitleColors;
        SubtitleColorBox.DisplayMemberPath = "Label";
        SubtitleColorBox.SelectedValuePath = "Value";

        EqualizerPresetBox.ItemsSource = Wiina.Core.Filters.EqualizerBands.PresetNames
            .Concat(new[] { Wiina.Core.Filters.EqualizerBands.CustomPreset })
            .ToArray();
    }

    /// <summary>
    /// Rebuilds the localized choice lists in place, preserving the current
    /// selection, so switching language does not send a spurious change event.
    /// </summary>
    private void ApplyLocalizedChoices()
    {
        SetLocalizedItems(ThemeBox,
            Loc.T("quick.themeSystem"), Loc.T("quick.themeDark"), Loc.T("quick.themeLight"));
        SetLocalizedItems(LayoutBox,
            Loc.T("quick.layoutEdge"), Loc.T("quick.layoutDocked"));
        SetLocalizedItems(OscPositionBox,
            Loc.T("quick.oscFloating"), Loc.T("quick.oscBottom"), Loc.T("quick.oscTop"));
        SetLocalizedItems(OsdPositionBox,
            Loc.T("quick.osdBottomRight"), Loc.T("quick.osdBottomLeft"),
            Loc.T("quick.osdTopRight"), Loc.T("quick.osdTopLeft"),
            Loc.T("quick.osdTopCenter"), Loc.T("quick.osdBottomCenter"));
        SetLocalizedItems(SidebarSideBox,
            Loc.T("quick.sidebarLeft"), Loc.T("quick.sidebarRight"));
    }

    private static void SetLocalizedItems(ComboBox box, params string[] items)
    {
        int selected = box.SelectedIndex;
        box.ItemsSource = items;
        if (selected >= 0 && selected < items.Length)
        {
            box.SelectedIndex = selected;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(ApplyLocalizedChoices);
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _loading = true;
        try
        {
            ThemeBox.SelectedIndex = Math.Clamp((int)settings.Theme, 0, 2);
            AnimationsSwitch.IsOn = settings.AnimationsEnabled;
            LayoutBox.SelectedIndex = settings.Layout == WindowLayoutMode.Docked ? 1 : 0;
            AlwaysOnTopSwitch.IsOn = settings.AlwaysOnTop;
            LockAspectSwitch.IsOn = settings.LockAspectRatio;
            MpvStatsSwitch.IsOn = settings.ShowMpvStats;
            OscPositionBox.SelectedIndex = Math.Clamp((int)settings.OscPosition, 0, 2);
            OscAutoHideSwitch.IsOn = settings.OscAutoHide;
            OsdEnabledSwitch.IsOn = settings.OsdEnabled;
            OsdPositionBox.SelectedIndex = OsdPositions.ToIndex(settings.OsdPosition);
            OsdSizeSlider.Value = OsdSettings.ClampSize(settings.OsdSizePercent);
            OsdDurationBox.Value = OsdSettings.ClampDuration(settings.OsdDurationSeconds);
            OsdPersistentSwitch.IsOn = settings.OsdPersistent;
            FullscreenClockSwitch.IsOn = settings.FullscreenClockEnabled;
            FullscreenBatterySwitch.IsOn = settings.FullscreenBatteryEnabled;
            FullscreenTitleSwitch.IsOn = settings.FullscreenTitleEnabled;
            ThumbnailPreviewSwitch.IsOn = settings.ThumbnailPreviewEnabled;
            SidebarSideBox.SelectedIndex = settings.SidebarSide == SidebarSide.Leading ? 0 : 1;
            SidebarWidthSlider.Value = SidebarStateWidth(settings.SidebarWidth);

            AspectBox.SelectedIndex = IndexOfAspect(QuickSettings.NormalizeAspect(settings.VideoAspect));
            CropBox.SelectedIndex = IndexOfCrop(settings.VideoCropAspect);
            RotationBox.SelectedItem = QuickSettings.NormalizeRotation(settings.VideoRotation);
            FlipHorizontalSwitch.IsOn = settings.VideoFlipHorizontal;
            FlipVerticalSwitch.IsOn = settings.VideoFlipVertical;
            DeinterlaceSwitch.IsOn = settings.VideoDeinterlace;
            HardwareDecodeSwitch.IsOn = settings.VideoHardwareDecode;
            ZoomSlider.Value = QuickSettings.ClampZoom(settings.VideoZoom);
            PanXSlider.Value = QuickSettings.ClampPan(settings.VideoPanX);
            PanYSlider.Value = QuickSettings.ClampPan(settings.VideoPanY);

            MaxVolumeBox.Value = QuickSettings.ClampMaxVolume(settings.MaxVolume);
            VolumeSlider.Maximum = VolumeMapping.UiMaximum;
            VolumeSlider.Value = VolumeMapping.ToUi(settings.Volume);
            MuteSwitch.IsOn = settings.Muted;
            AudioDelaySlider.Value = QuickSettings.ClampAudioDelay(settings.AudioDelay);
            EqualizerSwitch.IsOn = settings.EqualizerEnabled;
            settings.EqualizerGains = Wiina.Core.Filters.EqualizerBands
                .NormalizeGains(settings.EqualizerGains);
            EqView.SetGains(settings.EqualizerGains);
            EqView.SetEnabled(settings.EqualizerEnabled);
            EqualizerPresetBox.SelectedIndex = EqualizerPresetIndex(settings.EqualizerPreset);

            SubtitleVisibleSwitch.IsOn = settings.SubtitleVisible;
            SubtitleDelaySlider.Value = QuickSettings.ClampSubtitleDelay(settings.SubtitleDelay);
            SubtitlePositionSlider.Value = QuickSettings.ClampSubtitlePosition(settings.SubtitlePosition);
            SubtitleScaleSlider.Value = QuickSettings.ClampSubtitleScale(settings.SubtitleScale);
            SubtitleFontSizeBox.Value = QuickSettings.ClampSubtitleFontSize(settings.SubtitleFontSize);
            SubtitleColorBox.SelectedIndex = IndexOfColor(
                QuickSettings.NormalizeColor(settings.SubtitleColor));
            SubtitleBorderBox.Value = QuickSettings.ClampSubtitleBorder(settings.SubtitleBorderSize);
        }
        finally
        {
            _loading = false;
        }

        UpdateRangeLabels();
    }

    public void SetMediaState(bool hasMedia)
    {
        _hasMedia = hasMedia;
        VideoHint.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        AudioHint.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;
        SubtitleHint.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;

        foreach (Control control in new Control[]
                 {
                     VideoTrackBox,
                     AspectBox,
                     CropBox,
                     RotationBox,
                     FlipHorizontalSwitch,
                     FlipVerticalSwitch,
                     DeinterlaceSwitch,
                     HardwareDecodeSwitch,
                     ZoomSlider,
                     PanXSlider,
                     PanYSlider,
                     AudioTrackBox,
                     AudioDelaySlider,
                     AudioDeviceBox,
                     SubtitleTrackBox,
                     SecondarySubtitleBox,
                     SubtitleVisibleSwitch,
                     SubtitleDelaySlider,
                     SubtitlePositionSlider,
                     SubtitleScaleSlider,
                     SubtitleFontSizeBox,
                     SubtitleColorBox,
                     SubtitleBorderBox,
                     LoadSubtitleButton,
                 })
        {
            control.IsEnabled = hasMedia;
        }

        // Volume/mute and layout settings stay usable while the player is idle.
        VolumeSlider.IsEnabled = true;
        MaxVolumeBox.IsEnabled = true;
        MuteSwitch.IsEnabled = true;
        UpdateSubtitleWarning();
    }

    public void SetTracks(IReadOnlyList<TrackInfo> tracks)
    {
        _tracks.Clear();
        _tracks.AddRange(tracks);

        _loading = true;
        try
        {
            PopulateTrackBox(VideoTrackBox, TrackType.Video);
            PopulateTrackBox(AudioTrackBox, TrackType.Audio);
            PopulateTrackBox(SubtitleTrackBox, TrackType.Subtitle);
            PopulateSecondarySubtitleBox();
        }
        finally
        {
            _loading = false;
        }

        UpdateSubtitleWarning();
    }

    public void SetSecondarySubtitle(int? id)
    {
        _secondarySubtitleId = id;
        _loading = true;
        try
        {
            PopulateSecondarySubtitleBox();
        }
        finally
        {
            _loading = false;
        }
    }

    public void SetVideoSize(int width, int height)
    {
        _videoWidth = width;
        _videoHeight = height;
    }

    public void SetAudioDevices(IReadOnlyList<MpvAudioDevice> devices, string? current)
    {
        _loading = true;
        try
        {
            AudioDeviceBox.ItemsSource = devices;
            AudioDeviceBox.DisplayMemberPath = nameof(MpvAudioDevice.DisplayName);
            AudioDeviceBox.SelectedValuePath = nameof(MpvAudioDevice.Name);
            AudioDeviceBox.SelectedValue = devices.Any(device => device.Name == current)
                ? current
                : devices.FirstOrDefault()?.Name;
            AudioDeviceBox.IsEnabled = _hasMedia && devices.Count > 0;
            AudioDeviceHint.Text = devices.Count == 0
                ? Loc.T("quick.noAudioDevices")
                : Loc.F("quick.audioDevices", devices.Count);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Keeps the width slider in sync while the sidebar grip drags.</summary>
    public void SetSidebarWidthValue(double width)
    {
        _loading = true;
        try
        {
            SidebarWidthSlider.Value = Math.Clamp(
                width,
                SidebarWidthSlider.Minimum,
                SidebarWidthSlider.Maximum);
        }
        finally
        {
            _loading = false;
        }

        SidebarWidthText.Text = $"{Math.Round(SidebarWidthSlider.Value)} DIP";
    }

    /// <summary>Mirrors mpv/OSC volume changes without echoing commands.</summary>
    public void SetVolumeState(double volume, bool muted)
    {
        _loading = true;
        try
        {
            VolumeSlider.Value = VolumeMapping.ToUi(volume);
            MuteSwitch.IsOn = muted;
        }
        finally
        {
            _loading = false;
        }

        VolumeValueText.Text = VolumeMapping.FormatUi(volume);
    }

    public void SetSubtitleVisibility(bool visible)
    {
        _loading = true;
        try
        {
            SubtitleVisibleSwitch.IsOn = visible;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Mirrors filter-window/quick-setting EQ changes without echoing events.</summary>
    public void SetEqualizerState(AppSettings settings)
    {
        _loading = true;
        try
        {
            settings.EqualizerGains = Wiina.Core.Filters.EqualizerBands
                .NormalizeGains(settings.EqualizerGains);
            EqualizerSwitch.IsOn = settings.EqualizerEnabled;
            EqView.SetGains(settings.EqualizerGains);
            EqView.SetEnabled(settings.EqualizerEnabled);
            EqualizerPresetBox.SelectedIndex = EqualizerPresetIndex(settings.EqualizerPreset);
        }
        finally
        {
            _loading = false;
        }
    }

    public void SetAudioDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _loading = true;
        try
        {
            if (AudioDeviceBox.ItemsSource is IEnumerable<MpvAudioDevice> devices &&
                devices.Any(device => device.Name == name))
            {
                AudioDeviceBox.SelectedValue = name;
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void ShowPage(QuickSettingsPage page)
    {
        _page = page;
        LayoutPage.Visibility = page == QuickSettingsPage.Layout
            ? Visibility.Visible
            : Visibility.Collapsed;
        VideoPage.Visibility = page == QuickSettingsPage.Video
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioPage.Visibility = page == QuickSettingsPage.Audio
            ? Visibility.Visible
            : Visibility.Collapsed;
        SubtitlesPage.Visibility = page == QuickSettingsPage.Subtitles
            ? Visibility.Visible
            : Visibility.Collapsed;

        AnimatePageChange();
    }

    /// <summary>Same compact vertical flow used by the settings window pages.</summary>
    private void AnimatePageChange()
    {
        if (!App.Settings.AnimationsEnabled || PageContent.XamlRoot is null)
        {
            PageContent.Opacity = 1;
            PageSlide.Y = 0;
            return;
        }

        PageContent.Opacity = 0;
        PageSlide.Y = 10;
        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(fade, PageContent);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation
        {
            From = 10,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(slide, PageSlide);
        Storyboard.SetTargetProperty(slide, "Y");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void PopulateTrackBox(ComboBox box, TrackType type)
    {
        var choices = new List<TrackChoice>
        {
            new(-2, Loc.T("quick.autoDefault")),
            new(-1, "关闭"),
        };
        choices.AddRange(_tracks
            .Where(track => track.Type == type)
            .Select(track => new TrackChoice(track.Id, TrackLabel(track))));

        TrackInfo? selected = _tracks.FirstOrDefault(
            track => track.Type == type && track.IsSelected);
        int selectedId = selected?.Id ?? (type == TrackType.Subtitle ? -1 : -2);

        box.ItemsSource = choices;
        box.DisplayMemberPath = nameof(TrackChoice.Name);
        box.SelectedValuePath = nameof(TrackChoice.Id);
        box.SelectedValue = choices.Any(choice => choice.Id == selectedId)
            ? selectedId
            : -2;
        box.IsEnabled = _hasMedia;
    }

    private void PopulateSecondarySubtitleBox()
    {
        var choices = new List<TrackChoice> { new(-1, Loc.T("menu.off")) };
        choices.AddRange(_tracks
            .Where(track => track.Type == TrackType.Subtitle)
            .Select(track => new TrackChoice(track.Id, TrackLabel(track))));

        int selectedId = _secondarySubtitleId ?? -1;
        SecondarySubtitleBox.ItemsSource = choices;
        SecondarySubtitleBox.DisplayMemberPath = nameof(TrackChoice.Name);
        SecondarySubtitleBox.SelectedValuePath = nameof(TrackChoice.Id);
        SecondarySubtitleBox.SelectedValue = choices.Any(choice => choice.Id == selectedId)
            ? selectedId
            : -1;
        SecondarySubtitleBox.IsEnabled = _hasMedia;
    }

    private static string TrackLabel(TrackInfo track)
    {
        string label = track.DisplayName;
        if (!string.IsNullOrWhiteSpace(track.Language) &&
            !label.Contains(track.Language, StringComparison.OrdinalIgnoreCase))
        {
            label += $" · {track.Language}";
        }

        if (!string.IsNullOrWhiteSpace(track.Codec))
        {
            label += $" · {track.Codec}";
        }

        return label;
    }

    private void UpdateSubtitleWarning()
    {
        TrackInfo? selected = _tracks.FirstOrDefault(
            track => track.Type == TrackType.Subtitle && track.IsSelected);
        SubtitleStyleAdvice advice = SubtitleStyleAdvisory.Analyze(selected?.Codec);

        if (advice.Issue != SubtitleStyleIssue.None)
        {
            SubtitleStyleWarning.Text = advice.Message;
            SubtitleStyleWarning.Visibility = Visibility.Visible;
        }
        else
        {
            SubtitleStyleWarning.Visibility = Visibility.Collapsed;
        }

        bool styleEnabled = _hasMedia && advice.AllowStyleOverride;
        SubtitleFontSizeBox.IsEnabled = styleEnabled;
        SubtitleColorBox.IsEnabled = styleEnabled;
        SubtitleBorderBox.IsEnabled = styleEnabled;
    }

    private void UpdateRangeLabels()
    {
        SidebarWidthText.Text = $"{Math.Round(SidebarWidthSlider.Value)} DIP";
        OsdSizeText.Text = $"{Math.Round(OsdSizeSlider.Value)}%";
        ZoomValueText.Text = $"{ZoomSlider.Value:0.00}×";
        PanXValueText.Text = $"{PanXSlider.Value:0.00}";
        PanYValueText.Text = $"{PanYSlider.Value:0.00}";
        VolumeValueText.Text = $"{Math.Round(VolumeSlider.Value)}%";
        AudioDelayText.Text = Loc.F("quick.seconds", $"{AudioDelaySlider.Value:+0.0;-0.0;0.0}");
        SubtitleDelayText.Text = Loc.F("quick.seconds", $"{SubtitleDelaySlider.Value:+0.0;-0.0;0.0}");
        SubtitlePositionText.Text = $"{Math.Round(SubtitlePositionSlider.Value)}";
        SubtitleScaleText.Text = $"{SubtitleScaleSlider.Value:0.00}×";
    }

    private static double SidebarStateWidth(double width) =>
        Math.Clamp(width, 300, 500);

    private static int IndexOfAspect(string value)
    {
        for (int i = 0; i < QuickSettings.AspectRatios.Count; i++)
        {
            if (QuickSettings.AspectRatios[i].Value == value)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOfCrop(double aspect)
    {
        for (int i = 0; i < QuickSettings.CropOptions.Count; i++)
        {
            if (Math.Abs(QuickSettings.CropOptions[i].Aspect - aspect) < 0.0001)
            {
                return i;
            }
        }

        return 0;
    }

    private static int IndexOfColor(string value)
    {
        for (int i = 0; i < QuickSettings.SubtitleColors.Count; i++)
        {
            if (string.Equals(
                    QuickSettings.SubtitleColors[i].Value,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int EqualizerPresetIndex(string? name)
    {
        var names = Wiina.Core.Filters.EqualizerBands.PresetNames;
        if (string.Equals(
                name,
                Wiina.Core.Filters.EqualizerBands.CustomPreset,
                StringComparison.Ordinal))
        {
            return names.Count;
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.Name)
        {
            case nameof(ThemeBox):
                Raise(QuickSettingKey.Theme, number: ThemeBox.SelectedIndex);
                break;
            case nameof(LayoutBox):
                Raise(QuickSettingKey.Layout, number: LayoutBox.SelectedIndex);
                break;
            case nameof(OscPositionBox):
                Raise(QuickSettingKey.OscPosition, number: OscPositionBox.SelectedIndex);
                break;
            case nameof(OsdPositionBox):
                Raise(QuickSettingKey.OsdPosition, number: OsdPositionBox.SelectedIndex);
                break;
            case nameof(SidebarSideBox):
                Raise(QuickSettingKey.SidebarSide, number: SidebarSideBox.SelectedIndex);
                break;
            case nameof(AspectBox):
                if (AspectBox.SelectedIndex >= 0 &&
                    AspectBox.SelectedIndex < QuickSettings.AspectRatios.Count)
                {
                    Raise(
                        QuickSettingKey.Aspect,
                        text: QuickSettings.AspectRatios[AspectBox.SelectedIndex].Value);
                }

                break;
            case nameof(CropBox):
                if (CropBox.SelectedIndex >= 0 &&
                    CropBox.SelectedIndex < QuickSettings.CropOptions.Count)
                {
                    Raise(
                        QuickSettingKey.Crop,
                        number: QuickSettings.CropOptions[CropBox.SelectedIndex].Aspect);
                }

                break;
            case nameof(RotationBox):
                Raise(
                    QuickSettingKey.Rotation,
                    number: RotationBox.SelectedItem is int degrees ? degrees : 0);
                break;
            case nameof(SubtitleColorBox):
                if (SubtitleColorBox.SelectedIndex >= 0 &&
                    SubtitleColorBox.SelectedIndex < QuickSettings.SubtitleColors.Count)
                {
                    Raise(
                        QuickSettingKey.SubtitleColor,
                        text: QuickSettings.SubtitleColors[SubtitleColorBox.SelectedIndex].Value);
                }

                break;
            case nameof(EqualizerPresetBox):
                if (EqualizerPresetBox.SelectedItem is string preset)
                {
                    Raise(QuickSettingKey.EqualizerPreset, text: preset);
                }

                break;
            case nameof(VideoTrackBox):
                RaiseTrack(QuickSettingKey.VideoTrack, VideoTrackBox);
                break;
            case nameof(AudioTrackBox):
                RaiseTrack(QuickSettingKey.AudioTrack, AudioTrackBox);
                break;
            case nameof(SubtitleTrackBox):
                RaiseTrack(QuickSettingKey.SubtitleTrack, SubtitleTrackBox);
                break;
            case nameof(SecondarySubtitleBox):
                RaiseTrack(QuickSettingKey.SecondarySubtitle, SecondarySubtitleBox);
                break;
            case nameof(AudioDeviceBox):
                if (AudioDeviceBox.SelectedValue is string device)
                {
                    Raise(QuickSettingKey.AudioDevice, text: device);
                }

                break;
        }
    }

    private void RaiseTrack(QuickSettingKey key, ComboBox box)
    {
        if (box.SelectedItem is TrackChoice choice)
        {
            Raise(key, number: choice.Id);
        }
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.Name)
        {
            case nameof(AnimationsSwitch):
                Raise(QuickSettingKey.Animations, flag: AnimationsSwitch.IsOn);
                break;
            case nameof(AlwaysOnTopSwitch):
                Raise(QuickSettingKey.AlwaysOnTop, flag: AlwaysOnTopSwitch.IsOn);
                break;
            case nameof(LockAspectSwitch):
                Raise(QuickSettingKey.LockAspect, flag: LockAspectSwitch.IsOn);
                break;
            case nameof(MpvStatsSwitch):
                Raise(QuickSettingKey.MpvStats, flag: MpvStatsSwitch.IsOn);
                break;
            case nameof(OscAutoHideSwitch):
                Raise(QuickSettingKey.OscAutoHide, flag: OscAutoHideSwitch.IsOn);
                break;
            case nameof(OsdEnabledSwitch):
                Raise(QuickSettingKey.OsdEnabled, flag: OsdEnabledSwitch.IsOn);
                break;
            case nameof(OsdPersistentSwitch):
                Raise(QuickSettingKey.OsdPersistent, flag: OsdPersistentSwitch.IsOn);
                break;
            case nameof(FullscreenClockSwitch):
                Raise(QuickSettingKey.FullscreenClock, flag: FullscreenClockSwitch.IsOn);
                break;
            case nameof(FullscreenBatterySwitch):
                Raise(QuickSettingKey.FullscreenBattery, flag: FullscreenBatterySwitch.IsOn);
                break;
            case nameof(FullscreenTitleSwitch):
                Raise(QuickSettingKey.FullscreenTitle, flag: FullscreenTitleSwitch.IsOn);
                break;
            case nameof(ThumbnailPreviewSwitch):
                Raise(QuickSettingKey.ThumbnailPreview, flag: ThumbnailPreviewSwitch.IsOn);
                break;
            case nameof(FlipHorizontalSwitch):
                Raise(QuickSettingKey.FlipHorizontal, flag: FlipHorizontalSwitch.IsOn);
                break;
            case nameof(FlipVerticalSwitch):
                Raise(QuickSettingKey.FlipVertical, flag: FlipVerticalSwitch.IsOn);
                break;
            case nameof(DeinterlaceSwitch):
                Raise(QuickSettingKey.Deinterlace, flag: DeinterlaceSwitch.IsOn);
                break;
            case nameof(HardwareDecodeSwitch):
                Raise(QuickSettingKey.HardwareDecode, flag: HardwareDecodeSwitch.IsOn);
                break;
            case nameof(MuteSwitch):
                Raise(QuickSettingKey.Mute, flag: MuteSwitch.IsOn);
                break;
            case nameof(SubtitleVisibleSwitch):
                Raise(QuickSettingKey.SubtitleVisible, flag: SubtitleVisibleSwitch.IsOn);
                break;
            case nameof(EqualizerSwitch):
                EqView.SetEnabled(EqualizerSwitch.IsOn);
                Raise(QuickSettingKey.EqualizerEnabled, flag: EqualizerSwitch.IsOn);
                break;
        }
    }

    private void OnEqualizerGainsChanged(
        object? sender,
        EqualizerGainsChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        string detected = Wiina.Core.Filters.EqualizerBands.DetectPreset(e.Gains);
        _loading = true;
        try
        {
            EqualizerPresetBox.SelectedIndex = EqualizerPresetIndex(detected);
        }
        finally
        {
            _loading = false;
        }

        Raise(QuickSettingKey.EqualizerGain, gains: e.Gains);
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateRangeLabels();
        if (sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.Name)
        {
            case nameof(SidebarWidthSlider):
                Raise(QuickSettingKey.SidebarWidth, number: SidebarWidthSlider.Value);
                break;
            case nameof(OsdSizeSlider):
                Raise(QuickSettingKey.OsdSize, number: OsdSizeSlider.Value);
                break;
            case nameof(ZoomSlider):
                Raise(QuickSettingKey.Zoom, number: ZoomSlider.Value);
                break;
            case nameof(PanXSlider):
                Raise(QuickSettingKey.PanX, number: PanXSlider.Value);
                break;
            case nameof(PanYSlider):
                Raise(QuickSettingKey.PanY, number: PanYSlider.Value);
                break;
            case nameof(VolumeSlider):
                Raise(QuickSettingKey.Volume, number: VolumeSlider.Value);
                break;
            case nameof(AudioDelaySlider):
                Raise(QuickSettingKey.AudioDelay, number: AudioDelaySlider.Value);
                break;
            case nameof(SubtitleDelaySlider):
                Raise(QuickSettingKey.SubtitleDelay, number: SubtitleDelaySlider.Value);
                break;
            case nameof(SubtitlePositionSlider):
                Raise(QuickSettingKey.SubtitlePosition, number: SubtitlePositionSlider.Value);
                break;
            case nameof(SubtitleScaleSlider):
                Raise(QuickSettingKey.SubtitleScale, number: SubtitleScaleSlider.Value);
                break;
        }
    }

    private void OnNumberBoxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value))
        {
            return;
        }

        switch (sender.Name)
        {
            case nameof(MaxVolumeBox):
                Raise(QuickSettingKey.MaxVolume, number: sender.Value);
                break;
            case nameof(OsdDurationBox):
                Raise(QuickSettingKey.OsdDuration, number: sender.Value);
                break;
            case nameof(SubtitleFontSizeBox):
                Raise(QuickSettingKey.SubtitleFontSize, number: sender.Value);
                break;
            case nameof(SubtitleBorderBox):
                Raise(QuickSettingKey.SubtitleBorder, number: sender.Value);
                break;
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string card })
        {
            Raise(QuickSettingKey.ResetCard, text: card);
        }
    }

    private void OnLoadSubtitleClick(object sender, RoutedEventArgs e) =>
        Raise(QuickSettingKey.LoadSubtitle);

    private void OnRefreshDevicesClick(object sender, RoutedEventArgs e) =>
        Raise(QuickSettingKey.RefreshAudioDevices);

    private void Raise(
        QuickSettingKey key,
        string? text = null,
        double number = 0,
        bool flag = false,
        IReadOnlyList<double>? gains = null)
    {
        if (_loading)
        {
            return;
        }

        SettingChanged?.Invoke(this, new QuickSettingChangedEventArgs
        {
            Key = key,
            Text = text,
            Number = number,
            Flag = flag,
            Gains = gains,
        });
    }

    private sealed record TrackChoice(int Id, string Name);
}
