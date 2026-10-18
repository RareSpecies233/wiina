using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Wiina.App.Interop;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.App.Views;
using Wiina.Core.Input;
using Wiina.Core.Localization;

namespace Wiina.App.Controls;

/// <summary>
/// Key-binding editor shared by the standalone <see cref="Views.KeyBindingWindow"/>.
/// Owns recording, conflict resolution, reset, profile switching and JSON I/O.
/// </summary>
public sealed partial class KeyBindingsEditor : UserControl
{
    private static readonly Color AccentTint = Color.FromArgb(0x2E, 0x4C, 0xC2, 0xFF);
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0, 0, 0);

    private readonly List<(string Action, Button Button)> _rows = new();
    private IReadOnlyList<KeyBindingSetting> _bindings = Array.Empty<KeyBindingSetting>();
    private string? _selectedAction;
    private Window? _host;
    private bool _recording;
    private bool _loading;

    public KeyBindingsEditor()
    {
        InitializeComponent();
        UpdateStaticLabels();
        AddHandler(KeyDownEvent, new KeyEventHandler(OnCaptureKeyDown), handledEventsToo: true);
        App.SettingsChanged += OnSettingsChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        // Populate immediately so the profile selector is never briefly or
        // permanently empty if Loaded is deferred by the hosting window.
        BuildProfileChoices();
        Reload();
        Loaded += (_, _) =>
        {
            BuildProfileChoices();
            Reload();
            Focus(FocusState.Programmatic);
        };
        Unloaded += (_, _) =>
        {
            App.SettingsChanged -= OnSettingsChanged;
            Loc.LanguageChanged -= OnLanguageChanged;
        };
    }

    /// <summary>Supplies the owning window for file pickers.</summary>
    public void AttachWindow(Window window) => _host = window;

    private void InitializePicker(object picker)
    {
        if (_host is not null)
        {
            WindowShell.InitializePicker(_host, picker);
        }
    }

    private bool IsEnglish => string.Equals(Loc.CurrentLanguage, AppLanguage.English, StringComparison.OrdinalIgnoreCase);

    private void BuildProfileChoices()
    {
        _loading = true;
        try
        {
            int selected = IndexOfProfile(App.Settings.KeyBindingProfile);
            ProfileBox.Items.Clear();
            foreach (string profile in KeyBindingProfiles.All)
            {
                ProfileBox.Items.Add(IsEnglish ? KeyBindingProfiles.LabelEnglish(profile) : KeyBindingProfiles.Label(profile));
            }

            ProfileBox.SelectedIndex = selected;
        }
        finally
        {
            _loading = false;
        }
    }

    private static int IndexOfProfile(string? profile)
    {
        for (int i = 0; i < KeyBindingProfiles.All.Count; i++)
        {
            if (string.Equals(KeyBindingProfiles.All[i], KeyBindingProfiles.Normalize(profile), StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void Reload()
    {
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile).ToList();
        BuildActionList();
        if (_selectedAction is not null && PlayerActionCatalog.IsKnown(_selectedAction))
        {
            SelectAction(_selectedAction);
        }
        else
        {
            SelectAction(_rows.FirstOrDefault().Action);
        }
    }

    private void BuildActionList()
    {
        ActionList.Children.Clear();
        _rows.Clear();
        var byAction = _bindings
            .GroupBy(binding => binding.Action, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (PlayerActionInfo info in PlayerActionCatalog.All
                     .OrderBy(action => action.Category, StringComparer.Ordinal)
                     .ThenBy(action => action.Label, StringComparer.Ordinal))
        {
            string action = info.Action;
            List<KeyBindingSetting> chords = byAction.TryGetValue(action, out List<KeyBindingSetting>? list)
                ? list
                : new List<KeyBindingSetting>();
            string chordText = chords.Count == 0
                ? Loc.T("keybindings.unbound")
                : string.Join(" / ", chords.Select(chord => chord.Chord));

            var content = new Grid { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new TextBlock
            {
                Text = info.Label,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var chordBlock = new TextBlock
            {
                Text = chordText,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 11,
                Opacity = chords.Count == 0 ? 0.45 : 0.8,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chordBlock, 1);
            content.Children.Add(chordBlock);

            var button = new Button
            {
                Tag = action,
                Content = content,
                Height = 38,
                Padding = new Thickness(10, 2, 10, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(TransparentColor),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                button,
                $"{info.Label} {chordText}");
            button.Click += (_, _) => SelectAction(action);
            ActionList.Children.Add(button);
            _rows.Add((action, button));
        }
    }

    private void SelectAction(string? action)
    {
        _selectedAction = action;
        foreach ((string id, Button button) in _rows)
        {
            button.Background = new SolidColorBrush(
                string.Equals(id, action, StringComparison.Ordinal) ? AccentTint : TransparentColor);
        }

        if (action is null)
        {
            ActionName.Text = string.Empty;
            ActionCategory.Text = string.Empty;
            ChordText.Text = string.Empty;
            HintText.Text = Loc.T("keybindings.pickFirst");
            return;
        }

        ActionName.Text = PlayerActionCatalog.Label(action);
        ActionCategory.Text = PlayerActionCatalog.CategoryOf(action);
        RowsReflectSelection();
        HintText.Text = Loc.T("keybindings.hint");
        StatusText.Text = string.Empty;
        RecordButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void RowsReflectSelection()
    {
        if (_selectedAction is null)
        {
            ChordText.Text = Loc.T("keybindings.unbound");
            return;
        }

        List<string> chords = _bindings
            .Where(binding => string.Equals(binding.Action, _selectedAction, StringComparison.Ordinal))
            .Select(binding => binding.Chord)
            .ToList();
        ChordText.Text = chords.Count == 0 ? Loc.T("keybindings.unbound") : string.Join(" / ", chords);
    }

    private void OnRecordClick(object sender, RoutedEventArgs e) => StartRecording();

    private void OnCancelClick(object sender, RoutedEventArgs e) => StopRecording(clearStatus: true);

    private void StartRecording()
    {
        if (_selectedAction is null)
        {
            StatusText.Text = Loc.T("keybindings.pickFirst");
            return;
        }

        _recording = true;
        RecordButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        HintText.Text = Loc.F("keybindings.recording", PlayerActionCatalog.Label(_selectedAction));
        StatusText.Text = Loc.T("keybindings.recording");
        Focus(FocusState.Programmatic);
    }

    private void StopRecording(bool clearStatus)
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        RecordButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        HintText.Text = _selectedAction is null
            ? Loc.T("keybindings.hint")
            : Loc.F("keybindings.selectedAfter", PlayerActionCatalog.Label(_selectedAction));
        if (clearStatus)
        {
            StatusText.Text = string.Empty;
        }
    }

    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording || _selectedAction is null)
        {
            return;
        }

        VirtualKey key = e.Key;
        if (key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift or
            VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            e.Handled = true;
            return;
        }

        if (key == VirtualKey.Escape)
        {
            e.Handled = true;
            StopRecording(clearStatus: true);
            return;
        }

        if (key == VirtualKey.Back)
        {
            e.Handled = true;
            _ = ApplyRecordedChordAsync(string.Empty);
            return;
        }

        var modifiers = KeyModifiers.None;
        if (WindowInterop.IsKeyDown(VirtualKey.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.Menu))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.LeftWindows) ||
            WindowInterop.IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= KeyModifiers.Windows;
        }

        if (!KeyChordMapper.TryMap(key, modifiers, out KeyChord chord))
        {
            StatusText.Text = Loc.F("keybindings.invalid", key.ToString());
            e.Handled = true;
            return;
        }

        e.Handled = true;
        _ = ApplyRecordedChordAsync(chord.ToString());
    }

    private async Task ApplyRecordedChordAsync(string chord)
    {
        if (_selectedAction is null)
        {
            return;
        }

        string action = _selectedAction;
        SwitchToCustom();
        string? conflict = KeyBindings.FindConflict(_bindings, chord, action);
        if (conflict is not null)
        {
            bool replace = await Dialogs.ConfirmAsync(
                XamlRoot,
                Loc.T("keybindings.conflictTitle"),
                Loc.F("keybindings.conflictBody", PlayerActionCatalog.Label(conflict)),
                Loc.T("keybindings.replace"));
            if (!replace)
            {
                StopRecording(clearStatus: false);
                return;
            }
        }

        _bindings = string.IsNullOrEmpty(chord)
            ? KeyBindings.ClearAction(_bindings, action)
            : KeyBindings.SetBinding(_bindings, action, chord);
        Persist();
        BuildActionList();
        SelectAction(action);
        StopRecording(clearStatus: false);
        StatusText.Text = string.IsNullOrEmpty(chord)
            ? Loc.F("keybindings.clearedAction", PlayerActionCatalog.Label(action))
            : Loc.F("keybindings.bound", PlayerActionCatalog.Label(action), chord);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null)
        {
            return;
        }

        string action = _selectedAction;
        SwitchToCustom();
        _bindings = KeyBindings.ClearAction(_bindings, action);
        Persist();
        BuildActionList();
        SelectAction(action);
        StatusText.Text = Loc.F("keybindings.clearedAction", PlayerActionCatalog.Label(action));
    }

    private void OnResetOneClick(object sender, RoutedEventArgs e)
    {
        if (_selectedAction is null)
        {
            return;
        }

        string action = _selectedAction;
        string profile = KeyBindingProfiles.Normalize(App.Settings.KeyBindingProfile);
        SwitchToCustom();
        var reset = KeyBindings.ClearAction(_bindings, action).ToList();
        foreach (string chord in KeyBindings.DefaultChords(action, profile))
        {
            if (KeyBindings.FindConflict(reset, chord, action) is null)
            {
                reset.Add(new KeyBindingSetting(action, chord));
            }
        }

        _bindings = reset;
        Persist();
        BuildActionList();
        SelectAction(action);
        StatusText.Text = Loc.F("keybindings.resetOneAction", PlayerActionCatalog.Label(action));
    }

    private async void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        bool reset = await Dialogs.ConfirmAsync(
            XamlRoot,
            Loc.T("keybindings.resetAllTitle"),
            Loc.T("keybindings.resetAllBody"),
            Loc.T("keybindings.resetAll"));
        if (!reset)
        {
            return;
        }

        string profile = KeyBindingProfiles.Normalize(App.Settings.KeyBindingProfile);
        SetProfile(KeyBindingProfiles.Custom, persist: false);
        _bindings = KeyBindings.CreateProfileDefaults(profile);
        Persist();
        Reload();
        StatusText.Text = Loc.T("keybindings.resetAllDone");
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedIndex < 0)
        {
            return;
        }

        string profile = KeyBindingProfiles.All[Math.Clamp(ProfileBox.SelectedIndex, 0, KeyBindingProfiles.All.Count - 1)];
        if (string.Equals(KeyBindingProfiles.Normalize(App.Settings.KeyBindingProfile), profile, StringComparison.Ordinal))
        {
            return;
        }

        StopRecording(clearStatus: false);
        SetProfile(profile, persist: true);
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile).ToList();
        BuildActionList();
        SelectAction(_selectedAction);
        StatusText.Text = Loc.F("keybindings.profileChanged", ProfileBox.Items[ProfileBox.SelectedIndex]);
    }

    private void SwitchToCustom()
    {
        if (string.Equals(KeyBindingProfiles.Normalize(App.Settings.KeyBindingProfile), KeyBindingProfiles.Custom, StringComparison.Ordinal))
        {
            return;
        }

        SetProfile(KeyBindingProfiles.Custom, persist: false);
    }

    private void SetProfile(string profile, bool persist)
    {
        App.Settings.KeyBindingProfile = profile;
        _loading = true;
        try
        {
            ProfileBox.SelectedIndex = IndexOfProfile(profile);
        }
        finally
        {
            _loading = false;
        }

        if (persist)
        {
            App.SaveSettings();
        }
    }

    private void Persist()
    {
        App.Settings.KeyBindings = _bindings
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();
        App.SaveSettings();
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            string json = await FileIO.ReadTextAsync(file);
            if (!KeyBindings.TryImportJson(json, out IReadOnlyList<KeyBindingSetting> imported, out string error))
            {
                StatusText.Text = Loc.F("keybindings.importFailed", error);
                return;
            }

            SetProfile(KeyBindingProfiles.Custom, persist: false);
            _bindings = imported.ToList();
            Persist();
            Reload();
            StatusText.Text = string.IsNullOrWhiteSpace(error)
                ? Loc.F("keybindings.imported", imported.Count)
                : Loc.F("keybindings.importedPartial", imported.Count, error);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("keybindings.importFailed", ex.Message);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "wiina-keybindings",
            };
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitializePicker(picker);
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, KeyBindings.ExportJson(_bindings));
            StatusText.Text = Loc.F("keybindings.exported", file.Name);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.F("keybindings.importFailed", ex.Message);
        }
    }

    private void OnSettingsChanged()
    {
        if (_recording)
        {
            return;
        }

        string? selected = _selectedAction;
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile).ToList();
        BuildActionList();
        SelectAction(selected);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateStaticLabels();
        BuildProfileChoices();
        Reload();
    }

    private void UpdateStaticLabels()
    {
        ProfileLabel.Text = Loc.T("keybindings.profile");
        ProfileHintLabel.Text = Loc.T("keybindings.profileHint");
        SectionLabel.Text = Loc.T("keybindings.section");
    }
}

