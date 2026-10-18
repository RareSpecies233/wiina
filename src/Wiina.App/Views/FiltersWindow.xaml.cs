using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Wiina.App.Controls;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core.Filters;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wiina.App.Views;

/// <summary>
/// Video/audio filter editor: ordered chains, parameter editing, named presets
/// and the 10-band equalizer. Every change is written to the shared settings and
/// pushed into the active mpv window immediately.
/// </summary>
public sealed partial class FiltersWindow : Window
{
    private readonly AppSettings _settings = App.Settings;
    private readonly DispatcherQueueTimer _saveTimer;
    private List<FilterRow> _videoRows = new();
    private List<FilterRow> _audioRows = new();
    private string? _selectedVideoId;
    private string? _selectedAudioId;
    private bool _loading;

    public FiltersWindow()
    {
        InitializeComponent();
        Title = Loc.T("filters.windowTitle");
        RootGrid.RequestedTheme = _settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        WindowShell.Prepare(
            this,
            940,
            680,
            resizable: true,
            dark: _settings.Theme != ThemeMode.Light,
            minWidth: 720,
            minHeight: 480);

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        EqualizerPresetBox.ItemsSource = EqualizerBands.PresetNames
            .Concat(new[] { EqualizerBands.CustomPreset })
            .ToArray();
        UnsupportedText.Text = string.Join(Environment.NewLine, AudioFilterCatalog.Unsupported);

        LoadEqualizer();
        RefreshVideoPresets();
        RefreshAudioPresets();
        RefreshVideoList();
        RefreshAudioList();
        SetStatus(Loc.T("filters.ready"));

        Closed += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
            WindowShell.Detach(this);
        };
    }

    // --------------------------------------------------------------- tabs

    public void SelectTab(bool audio)
    {
        VideoPanel.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
        AudioPanel.Visibility = audio ? Visibility.Visible : Visibility.Collapsed;
        VideoTabButton.IsChecked = !audio;
        AudioTabButton.IsChecked = audio;
        TitleText.Text = audio ? Loc.T("filters.audioTitle") : Loc.T("filters.videoTitle");
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        SelectTab(sender is ToggleButton { Tag: "audio" });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // -------------------------------------------------------------- video

    private void RefreshVideoList(string? selectId = null)
    {
        selectId ??= _selectedVideoId;
        _videoRows = _settings.VideoFilters
            .Select(filter => new FilterRow(
                filter.Id,
                VideoFilterCatalog.Info(filter.Kind).Name,
                VideoFilterCatalog.Describe(filter),
                filter.Enabled ? string.Empty : Loc.T("filters.disabled")))
            .ToList();
        VideoFilterList.ItemsSource = _videoRows;
        VideoCountText.Text = Loc.F("filters.count", _videoRows.Count);

        if (_videoRows.Count == 0)
        {
            _selectedVideoId = null;
            VideoFilterList.SelectedIndex = -1;
            BuildVideoEditor();
            return;
        }

        int index = _videoRows.FindIndex(row => row.Id == selectId);
        if (index < 0)
        {
            index = 0;
        }

        VideoFilterList.SelectedIndex = index;
        _selectedVideoId = _videoRows[index].Id;
    }

    private VideoFilterSetting? SelectedVideo =>
        _settings.VideoFilters.FirstOrDefault(filter => filter.Id == _selectedVideoId);

    private void OnVideoSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoFilterList.SelectedItem is FilterRow row)
        {
            _selectedVideoId = row.Id;
        }

        BuildVideoEditor();
    }

    private void BuildVideoEditor()
    {
        VideoEditorPanel.Children.Clear();
        VideoFilterSetting? filter = SelectedVideo;
        if (filter is null)
        {
            VideoEditorPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("filters.pickFilter"),
                FontSize = 12,
                Opacity = 0.6,
            });
            return;
        }

        VideoFilterCatalog.KindInfo info = VideoFilterCatalog.Info(filter.Kind);
        VideoEditorPanel.Children.Add(new TextBlock
        {
            Text = info.Name,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        VideoEditorPanel.Children.Add(new TextBlock
        {
            Text = info.Description,
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (FilterParameter parameter in VideoFilterCatalog.Parameters(filter.Kind))
        {
            var box = new NumberBox
            {
                Header = parameter.Label + parameter.Suffix,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                SmallChange = parameter.Step,
                LargeChange = Math.Max(parameter.Step, parameter.Step * 5),
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = VideoFilterCatalog.GetParameter(filter, parameter.Key),
            };
            box.ValueChanged += (_, args) =>
            {
                if (_loading || double.IsNaN(args.NewValue))
                {
                    return;
                }

                VideoFilterCatalog.SetParameter(filter, parameter.Key, args.NewValue);
                UpdateVideoRow(filter);
                ApplyNow();
            };
            VideoEditorPanel.Children.Add(box);
        }

        if (filter.Kind == VideoFilterKind.Format)
        {
            var format = new ComboBox
            {
                Header = Loc.T("filters.paramFormat"),
                ItemsSource = VideoFilterCatalog.PixelFormats,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            format.SelectedItem = VideoFilterCatalog.PixelFormats
                .FirstOrDefault(value => value == filter.PixelFormat) ?? "yuv420p";
            format.SelectionChanged += (_, _) =>
            {
                if (_loading || format.SelectedItem is not string value)
                {
                    return;
                }

                filter.PixelFormat = value;
                UpdateVideoRow(filter);
                ApplyNow();
            };
            VideoEditorPanel.Children.Add(format);
        }

        var enable = new ToggleSwitch
        {
            Header = Loc.T("filters.enableFilter"),
            IsOn = filter.Enabled,
            MinWidth = 0,
            OnContent = Loc.T("filters.enable"),
            OffContent = Loc.T("filters.disable"),
        };
        enable.Toggled += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            filter.Enabled = enable.IsOn;
            UpdateVideoRow(filter);
            ApplyNow();
        };
        VideoEditorPanel.Children.Add(enable);
    }

    private void UpdateVideoRow(VideoFilterSetting filter)
    {
        FilterRow? row = _videoRows.FirstOrDefault(item => item.Id == filter.Id);
        if (row is not null)
        {
            row.Summary = VideoFilterCatalog.Describe(filter);
            row.State = filter.Enabled ? string.Empty : Loc.T("filters.disabled");
        }
    }

    private void OnVideoAddClick(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (VideoFilterCatalog.KindInfo info in VideoFilterCatalog.All)
        {
            var item = new MenuFlyoutItem { Text = info.Name, Tag = info.Kind };
            item.Click += (_, _) =>
            {
                VideoFilterSetting filter = VideoFilterCatalog.Create(info.Kind);
                _settings.VideoFilters.Add(filter);
                RefreshVideoList(filter.Id);
                BuildVideoEditor();
                ApplyNow();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(VideoAddButton);
    }

    private void OnVideoRemoveClick(object sender, RoutedEventArgs e)
    {
        VideoFilterSetting? filter = SelectedVideo;
        if (filter is null)
        {
            return;
        }

        int index = _settings.VideoFilters.IndexOf(filter);
        _settings.VideoFilters.Remove(filter);
        _selectedVideoId = _settings.VideoFilters.Count == 0
            ? null
            : _settings.VideoFilters[Math.Clamp(index, 0, _settings.VideoFilters.Count - 1)].Id;
        RefreshVideoList();
        BuildVideoEditor();
        ApplyNow();
    }

    private void OnVideoToggleClick(object sender, RoutedEventArgs e)
    {
        VideoFilterSetting? filter = SelectedVideo;
        if (filter is null)
        {
            return;
        }

        filter.Enabled = !filter.Enabled;
        RefreshVideoList(filter.Id);
        BuildVideoEditor();
        ApplyNow();
    }

    private void OnVideoUpClick(object sender, RoutedEventArgs e) => MoveVideo(-1);

    private void OnVideoDownClick(object sender, RoutedEventArgs e) => MoveVideo(1);

    private void MoveVideo(int delta)
    {
        VideoFilterSetting? filter = SelectedVideo;
        if (filter is null)
        {
            return;
        }

        int index = _settings.VideoFilters.IndexOf(filter);
        int target = index + delta;
        if (target < 0 || target >= _settings.VideoFilters.Count)
        {
            return;
        }

        (_settings.VideoFilters[index], _settings.VideoFilters[target]) =
            (_settings.VideoFilters[target], _settings.VideoFilters[index]);
        RefreshVideoList(filter.Id);
        BuildVideoEditor();
        ApplyNow();
    }

    private void OnVideoClearClick(object sender, RoutedEventArgs e)
    {
        if (_settings.VideoFilters.Count == 0)
        {
            return;
        }

        _settings.VideoFilters.Clear();
        _selectedVideoId = null;
        RefreshVideoList();
        BuildVideoEditor();
        ApplyNow();
    }

    // -------------------------------------------------------------- audio

    private void RefreshAudioList(string? selectId = null)
    {
        selectId ??= _selectedAudioId;
        _audioRows = _settings.AudioFilters
            .Select(filter => new FilterRow(
                filter.Id,
                AudioFilterCatalog.Info(filter.Kind).Name,
                AudioFilterCatalog.Describe(filter),
                filter.Enabled ? string.Empty : Loc.T("filters.disabled")))
            .ToList();
        AudioFilterList.ItemsSource = _audioRows;
        AudioCountText.Text = Loc.F("filters.count", _audioRows.Count);

        if (_audioRows.Count == 0)
        {
            _selectedAudioId = null;
            AudioFilterList.SelectedIndex = -1;
            BuildAudioEditor();
            return;
        }

        int index = _audioRows.FindIndex(row => row.Id == selectId);
        if (index < 0)
        {
            index = 0;
        }

        AudioFilterList.SelectedIndex = index;
        _selectedAudioId = _audioRows[index].Id;
    }

    private AudioFilterSetting? SelectedAudio =>
        _settings.AudioFilters.FirstOrDefault(filter => filter.Id == _selectedAudioId);

    private void OnAudioSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AudioFilterList.SelectedItem is FilterRow row)
        {
            _selectedAudioId = row.Id;
        }

        BuildAudioEditor();
    }

    private void BuildAudioEditor()
    {
        AudioEditorPanel.Children.Clear();
        AudioFilterSetting? filter = SelectedAudio;
        if (filter is null)
        {
            AudioEditorPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("filters.pickAudioFilter"),
                FontSize = 12,
                Opacity = 0.6,
            });
            return;
        }

        AudioFilterCatalog.KindInfo info = AudioFilterCatalog.Info(filter.Kind);
        AudioEditorPanel.Children.Add(new TextBlock
        {
            Text = info.Name,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        AudioEditorPanel.Children.Add(new TextBlock
        {
            Text = info.Description,
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        });

        var parameters = new List<FilterParameter>();
        if (filter.Kind == AudioFilterKind.Loudnorm)
        {
            parameters.Add(new FilterParameter("I", Loc.T("filters.paramLoudness"), -70, -5, 0.5, " LUFS"));
            parameters.Add(new FilterParameter("TP", Loc.T("filters.paramTruePeak"), -9, 0, 0.1, " dB"));
            parameters.Add(new FilterParameter("LRA", Loc.T("filters.paramLoudnessRange"), 1, 50, 1, " LU"));
        }
        else
        {
            parameters.Add(new FilterParameter("f", Loc.T("filters.paramFrameLength"), 10, 8000, 10, " ms"));
            parameters.Add(new FilterParameter("g", Loc.T("filters.paramGaussianWindow"), 3, 301, 2, string.Empty));
            parameters.Add(new FilterParameter("p", Loc.T("filters.paramPeak"), 0, 1, 0.01, string.Empty));
            parameters.Add(new FilterParameter("m", Loc.T("filters.paramMaxGain"), 0, 30, 0.5, " dB"));
        }

        foreach (FilterParameter parameter in parameters)
        {
            double value = parameter.Key switch
            {
                "I" => filter.TargetLoudness,
                "TP" => filter.TruePeak,
                "LRA" => filter.LoudnessRange,
                "f" => filter.FrameLength,
                "g" => filter.GaussianSize,
                "p" => filter.Peak,
                "m" => filter.MaxGain,
                _ => 0,
            };
            var box = new NumberBox
            {
                Header = parameter.Label + parameter.Suffix,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                SmallChange = parameter.Step,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = value,
            };
            box.ValueChanged += (_, args) =>
            {
                if (_loading || double.IsNaN(args.NewValue))
                {
                    return;
                }

                switch (parameter.Key)
                {
                    case "I":
                        filter.TargetLoudness = args.NewValue;
                        break;
                    case "TP":
                        filter.TruePeak = args.NewValue;
                        break;
                    case "LRA":
                        filter.LoudnessRange = args.NewValue;
                        break;
                    case "f":
                        filter.FrameLength = (int)Math.Round(args.NewValue);
                        break;
                    case "g":
                        filter.GaussianSize = (int)Math.Round(args.NewValue);
                        break;
                    case "p":
                        filter.Peak = args.NewValue;
                        break;
                    case "m":
                        filter.MaxGain = args.NewValue;
                        break;
                }

                UpdateAudioRow(filter);
                ApplyNow();
            };
            AudioEditorPanel.Children.Add(box);
        }

        var enable = new ToggleSwitch
        {
            Header = Loc.T("filters.enableFilter"),
            IsOn = filter.Enabled,
            MinWidth = 0,
            OnContent = Loc.T("filters.enable"),
            OffContent = Loc.T("filters.disable"),
        };
        enable.Toggled += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            filter.Enabled = enable.IsOn;
            UpdateAudioRow(filter);
            ApplyNow();
        };
        AudioEditorPanel.Children.Add(enable);
    }

    private void UpdateAudioRow(AudioFilterSetting filter)
    {
        FilterRow? row = _audioRows.FirstOrDefault(item => item.Id == filter.Id);
        if (row is not null)
        {
            row.Summary = AudioFilterCatalog.Describe(filter);
            row.State = filter.Enabled ? string.Empty : Loc.T("filters.disabled");
        }
    }

    private void OnAudioAddClick(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (AudioFilterCatalog.KindInfo info in AudioFilterCatalog.All)
        {
            var item = new MenuFlyoutItem { Text = info.Name, Tag = info.Kind };
            item.Click += (_, _) =>
            {
                AudioFilterSetting filter = AudioFilterCatalog.Create(info.Kind);
                _settings.AudioFilters.Add(filter);
                RefreshAudioList(filter.Id);
                BuildAudioEditor();
                ApplyNow();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt((FrameworkElement)sender);
    }

    private void OnAudioRemoveClick(object sender, RoutedEventArgs e)
    {
        AudioFilterSetting? filter = SelectedAudio;
        if (filter is null)
        {
            return;
        }

        _settings.AudioFilters.Remove(filter);
        RefreshAudioList();
        BuildAudioEditor();
        ApplyNow();
    }

    private void OnAudioToggleClick(object sender, RoutedEventArgs e)
    {
        AudioFilterSetting? filter = SelectedAudio;
        if (filter is null)
        {
            return;
        }

        filter.Enabled = !filter.Enabled;
        RefreshAudioList(filter.Id);
        BuildAudioEditor();
        ApplyNow();
    }

    // ---------------------------------------------------------- equalizer

    private void LoadEqualizer()
    {
        _loading = true;
        try
        {
            _settings.EqualizerGains = EqualizerBands.NormalizeGains(_settings.EqualizerGains);
            EqualizerSwitch.IsOn = _settings.EqualizerEnabled;
            EqView.SetGains(_settings.EqualizerGains);
            EqView.SetEnabled(_settings.EqualizerEnabled);
            EqualizerPresetBox.SelectedIndex = PresetIndex(_settings.EqualizerPreset);
        }
        finally
        {
            _loading = false;
        }
    }

    private static int PresetIndex(string? name)
    {
        if (string.Equals(name, EqualizerBands.CustomPreset, StringComparison.Ordinal))
        {
            return EqualizerBands.PresetNames.Count;
        }

        int index = EqualizerBands.PresetNames
            .ToList()
            .FindIndex(preset => string.Equals(preset, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }

    private void OnEqualizerToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.EqualizerEnabled = EqualizerSwitch.IsOn;
        EqView.SetEnabled(_settings.EqualizerEnabled);
        ApplyNow();
    }

    private void OnEqualizerPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EqualizerPresetBox.SelectedIndex < 0)
        {
            return;
        }

        int index = EqualizerPresetBox.SelectedIndex;
        if (index >= EqualizerBands.PresetNames.Count)
        {
            _settings.EqualizerPreset = EqualizerBands.CustomPreset;
            return;
        }

        string name = EqualizerBands.PresetNames[index];
        _settings.EqualizerPreset = name;
        _settings.EqualizerGains = EqualizerBands.CreatePreset(name).ToList();
        _loading = true;
        try
        {
            EqView.SetGains(_settings.EqualizerGains);
        }
        finally
        {
            _loading = false;
        }

        ApplyNow();
    }

    private void OnEqualizerGainsChanged(
        object? sender,
        EqualizerGainsChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.EqualizerGains = EqualizerBands.NormalizeGains(e.Gains);
        _settings.EqualizerPreset = EqualizerBands.DetectPreset(_settings.EqualizerGains);
        _loading = true;
        try
        {
            EqualizerPresetBox.SelectedIndex = PresetIndex(_settings.EqualizerPreset);
        }
        finally
        {
            _loading = false;
        }

        ApplyNow();
    }

    private void OnEqualizerResetClick(object sender, RoutedEventArgs e)
    {
        _settings.EqualizerGains = EqualizerBands.CreatePreset("Flat").ToList();
        _settings.EqualizerPreset = "Flat";
        LoadEqualizer();
        ApplyNow();
    }

    // ------------------------------------------------------------- presets

    private void RefreshVideoPresets()
    {
        string? selected = VideoPresetBox.SelectedItem as string;
        VideoPresetBox.ItemsSource = _settings.VideoFilterPresets
            .Select(preset => preset.Name)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();
        if (selected is not null)
        {
            VideoPresetBox.SelectedItem = selected;
        }
    }

    private void RefreshAudioPresets()
    {
        string? selected = AudioPresetBox.SelectedItem as string;
        AudioPresetBox.ItemsSource = _settings.AudioFilterPresets
            .Select(preset => preset.Name)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray();
        if (selected is not null)
        {
            AudioPresetBox.SelectedItem = selected;
        }
    }

    private void OnVideoPresetSaveClick(object sender, RoutedEventArgs e)
    {
        string name = VideoPresetName.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus(Loc.T("filters.enterPreset"), error: true);
            return;
        }

        VideoFilterPreset? preset = _settings.VideoFilterPresets
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            preset = new VideoFilterPreset { Name = name };
            _settings.VideoFilterPresets.Add(preset);
        }

        preset.Filters = _settings.VideoFilters.Select(filter => filter.Clone()).ToList();
        RefreshVideoPresets();
        VideoPresetBox.SelectedItem = preset.Name;
        ScheduleSave();
        SetStatus(Loc.F("filters.videoPresetSaved", name));
    }

    private void OnVideoPresetLoadClick(object sender, RoutedEventArgs e)
    {
        if (VideoPresetBox.SelectedItem is not string name)
        {
            SetStatus(Loc.T("filters.pickVideoPreset"), error: true);
            return;
        }

        VideoFilterPreset? preset = _settings.VideoFilterPresets
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (preset is null)
        {
            return;
        }

        _settings.VideoFilters = preset.Filters.Select(filter => filter.Clone()).ToList();
        _selectedVideoId = _settings.VideoFilters.FirstOrDefault()?.Id;
        RefreshVideoList();
        BuildVideoEditor();
        ApplyNow();
        SetStatus(Loc.F("filters.videoPresetLoaded", name));
    }

    private void OnVideoPresetDeleteClick(object sender, RoutedEventArgs e)
    {
        if (VideoPresetBox.SelectedItem is not string name)
        {
            return;
        }

        _settings.VideoFilterPresets.RemoveAll(
            preset => string.Equals(preset.Name, name, StringComparison.Ordinal));
        RefreshVideoPresets();
        ScheduleSave();
        SetStatus(Loc.F("filters.videoPresetDeleted", name));
    }

    private void OnAudioPresetSaveClick(object sender, RoutedEventArgs e)
    {
        string name = AudioPresetName.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus(Loc.T("filters.enterPreset"), error: true);
            return;
        }

        AudioFilterPreset? preset = _settings.AudioFilterPresets
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            preset = new AudioFilterPreset { Name = name };
            _settings.AudioFilterPresets.Add(preset);
        }

        preset.EqualizerGains = EqualizerBands.NormalizeGains(_settings.EqualizerGains);
        preset.EqualizerEnabled = _settings.EqualizerEnabled;
        preset.Filters = _settings.AudioFilters.Select(filter => filter.Clone()).ToList();
        RefreshAudioPresets();
        AudioPresetBox.SelectedItem = preset.Name;
        ScheduleSave();
        SetStatus(Loc.F("filters.audioPresetSaved", name));
    }

    private void OnAudioPresetLoadClick(object sender, RoutedEventArgs e)
    {
        if (AudioPresetBox.SelectedItem is not string name)
        {
            SetStatus(Loc.T("filters.pickAudioPreset"), error: true);
            return;
        }

        AudioFilterPreset? preset = _settings.AudioFilterPresets
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (preset is null)
        {
            return;
        }

        _settings.EqualizerGains = EqualizerBands.NormalizeGains(preset.EqualizerGains);
        _settings.EqualizerEnabled = preset.EqualizerEnabled;
        _settings.EqualizerPreset = EqualizerBands.DetectPreset(_settings.EqualizerGains);
        _settings.AudioFilters = preset.Filters.Select(filter => filter.Clone()).ToList();
        _selectedAudioId = _settings.AudioFilters.FirstOrDefault()?.Id;
        LoadEqualizer();
        RefreshAudioList();
        BuildAudioEditor();
        ApplyNow();
        SetStatus(Loc.F("filters.audioPresetLoaded", name));
    }

    private void OnAudioPresetDeleteClick(object sender, RoutedEventArgs e)
    {
        if (AudioPresetBox.SelectedItem is not string name)
        {
            return;
        }

        _settings.AudioFilterPresets.RemoveAll(
            preset => string.Equals(preset.Name, name, StringComparison.Ordinal));
        RefreshAudioPresets();
        ScheduleSave();
        SetStatus(Loc.F("filters.audioPresetDeleted", name));
    }

    // ------------------------------------------------------------- plumbing

    private void ApplyNow()
    {
        IReadOnlyList<string> errors = App.ActiveWindow?.ApplyStage8Filters()
            ?? Array.Empty<string>();
        App.ActiveWindow?.SyncStage8Views();
        ScheduleSave();
        int videoCount = _settings.VideoFilters.Count(filter => filter.Enabled);
        int audioCount = _settings.AudioFilters.Count(filter => filter.Enabled);
        string summary = Loc.F("filters.appliedSummary", videoCount, _settings.EqualizerEnabled ? Loc.T("filters.on") : Loc.T("filters.off"), audioCount);
        SetStatus(errors.Count == 0 ? summary : summary + "；" + string.Join("；", errors), errors.Count > 0);
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        App.SaveSettings();
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        if (error)
        {
            StatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0xE8, 0x7A, 0x5A));
        }
        else
        {
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    private sealed class FilterRow : INotifyPropertyChanged
    {
        private string _summary;
        private string _state;

        public FilterRow(string id, string title, string summary, string state)
        {
            Id = id;
            Title = title;
            _summary = summary;
            _state = state;
        }

        public string Id { get; }

        public string Title { get; }

        public string Summary
        {
            get => _summary;
            set
            {
                _summary = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            }
        }

        public string State
        {
            get => _state;
            set
            {
                _state = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
