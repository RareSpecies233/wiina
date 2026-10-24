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
using Windows.UI.ViewManagement;
using Wiina.App.Interop;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.App.Views;
using Wiina.Core.Input;
using Wiina.Core.Localization;

namespace Wiina.App.Controls;

/// <summary>
/// Key-binding editor shared by the standalone <see cref="Views.KeyBindingWindow"/>.
/// Offers a searchable, category-grouped action list, IINA-style read-only
/// built-in profiles, recording with conflict resolution, buffered edits with
/// an explicit save, JSON import/export and per-profile reset.
/// </summary>
public sealed partial class KeyBindingsEditor : UserControl
{
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0, 0, 0);
    private static readonly UISettings SystemUiSettings = new();

    private static Color AccentColor =>
        SystemUiSettings.GetColorValue(UIColorType.Accent);

    private static Color AccentTint
    {
        get
        {
            Color accent = AccentColor;
            return Color.FromArgb(0x2E, accent.R, accent.G, accent.B);
        }
    }

    private readonly List<(string Action, Button Button)> _rows = new();
    private IReadOnlyList<KeyBindingSetting> _bindings = Array.Empty<KeyBindingSetting>();
    private string? _selectedAction;
    private Window? _host;
    private bool _recording;
    private bool _loading;
    private bool _saving;
    private bool _dirty;
    private string _search = string.Empty;

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

    /// <summary>True when the editor holds edits that have not been saved yet.</summary>
    public bool HasUnsavedChanges => _dirty;

    /// <summary>
    /// Prompts for unsaved edits before the host window closes. Returns true
    /// when it is safe to close.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!_dirty || XamlRoot is null)
        {
            return true;
        }

        bool? save = await ShowUnsavedDialogAsync();
        return save is not null;
    }

    private void InitializePicker(object picker)
    {
        if (_host is not null)
        {
            WindowShell.InitializePicker(_host, picker);
        }
    }

    private bool IsEnglish => string.Equals(Loc.CurrentLanguage, AppLanguage.English, StringComparison.OrdinalIgnoreCase);

    private static string CurrentProfile =>
        KeyBindingProfiles.Normalize(App.Settings.KeyBindingProfile);

    private static bool IsBuiltInProfile(string? profile) =>
        string.Equals(profile, KeyBindingProfiles.Wiina, StringComparison.Ordinal) ||
        string.Equals(profile, KeyBindingProfiles.Mpv, StringComparison.Ordinal);

    private void BuildProfileChoices()
    {
        _loading = true;
        try
        {
            int selected = IndexOfProfile(App.Settings.KeyBindingProfile);
            ProfileBox.Items.Clear();
            foreach (string profile in ProfileNames())
            {
                ProfileBox.Items.Add(IsEnglish
                    ? KeyBindingProfiles.LabelEnglish(profile)
                    : KeyBindingProfiles.Label(profile));
            }

            ProfileBox.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, ProfileBox.Items.Count - 1));
        }
        finally
        {
            _loading = false;
        }

        UpdateProfileState();
    }

    private static List<string> ProfileNames()
    {
        var names = new List<string>(KeyBindingProfiles.All);
        foreach (KeyBindingUserProfile profile in App.Settings.UserKeyProfiles)
        {
            string name = profile.Name.Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static int IndexOfProfile(string? profile)
    {
        List<string> names = ProfileNames();
        string normalized = KeyBindingProfiles.Normalize(profile);
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], normalized, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private static KeyBindingUserProfile? FindUserProfile(string? name) =>
        App.Settings.UserKeyProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private void Reload(bool resetDirty = true)
    {
        if (resetDirty)
        {
            _dirty = false;
        }

        _bindings = KeyBindings
            .Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile)
            .ToList();
        BuildActionList();
        if (_selectedAction is not null &&
            _rows.Any(row => string.Equals(row.Action, _selectedAction, StringComparison.Ordinal)))
        {
            SelectAction(_selectedAction);
        }
        else
        {
            SelectAction(_rows.FirstOrDefault().Action);
        }

        UpdateProfileState();
    }

    private List<PlayerActionInfo> VisibleActions()
    {
        var infos = new Dictionary<string, PlayerActionInfo>(StringComparer.Ordinal);
        foreach (PlayerActionInfo info in PlayerActionCatalog.All)
        {
            infos[info.Action] = info;
        }

        foreach (KeyBindingSetting binding in _bindings)
        {
            if (!infos.ContainsKey(binding.Action))
            {
                infos[binding.Action] = new PlayerActionInfo(
                    binding.Action,
                    PlayerActionCatalog.Label(binding.Action),
                    PlayerActionCatalog.CategoryOf(binding.Action));
            }
        }

        IEnumerable<PlayerActionInfo> query = infos.Values;
        if (!string.IsNullOrWhiteSpace(_search))
        {
            query = query.Where(info =>
                info.Label.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                info.Action.Contains(_search, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderBy(info => CategoryIndex(info.Category))
            .ThenBy(info => info.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static int CategoryIndex(string category)
    {
        IReadOnlyList<string> categories = PlayerActionCatalog.Categories;
        for (int i = 0; i < categories.Count; i++)
        {
            if (string.Equals(categories[i], category, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private void BuildActionList()
    {
        ActionList.Children.Clear();
        _rows.Clear();
        List<PlayerActionInfo> visible = VisibleActions();
        if (visible.Count == 0)
        {
            ActionList.Children.Add(new TextBlock
            {
                Text = Loc.T("keybindings.searchEmpty"),
                FontSize = 12,
                Opacity = 0.65,
                Margin = new Thickness(10, 12, 10, 12),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var byAction = _bindings
            .GroupBy(binding => binding.Action, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        string? currentCategory = null;
        foreach (PlayerActionInfo info in visible)
        {
            if (!string.Equals(currentCategory, info.Category, StringComparison.Ordinal))
            {
                currentCategory = info.Category;
                ActionList.Children.Add(new TextBlock
                {
                    Text = info.Category,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.6,
                    Margin = new Thickness(10, _rows.Count == 0 ? 2 : 12, 10, 4),
                });
            }

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
            Color accent = AccentColor;
            var chordBlock = new TextBlock
            {
                Text = chordText,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 10,
                Foreground = new SolidColorBrush(chords.Count == 0
                    ? Color.FromArgb(0x88, accent.R, accent.G, accent.B)
                    : accent),
                Opacity = chords.Count == 0 ? 0.7 : 1,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var chordChip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = chordBlock,
            };
            Grid.SetColumn(chordChip, 1);
            content.Children.Add(chordChip);

            var button = new Button
            {
                Tag = action,
                Content = content,
                Height = 36,
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
        HintText.Text = IsBuiltInProfile(CurrentProfile)
            ? Loc.T("keybindings.builtInHint")
            : Loc.T("keybindings.hint");
        StatusText.Text = string.Empty;
        RecordButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        UpdateProfileState();
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

    private void UpdateProfileState()
    {
        bool builtIn = IsBuiltInProfile(CurrentProfile);
        CopyToCustomButton.Visibility = builtIn ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.IsEnabled = !builtIn;
        ClearButton.IsEnabled = !builtIn;
        ResetOneButton.IsEnabled = !builtIn;
        ProfileHintLabel.Text = builtIn
            ? Loc.T("keybindings.builtInHint")
            : Loc.T("keybindings.profileHint");
        SaveButton.IsEnabled = _dirty && !builtIn;
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateProfileState();
    }

    private void OnRecordClick(object sender, RoutedEventArgs e) => StartRecording();

    private void OnCancelClick(object sender, RoutedEventArgs e) => StopRecording(clearStatus: true);

    private void StartRecording()
    {
        if (IsBuiltInProfile(CurrentProfile))
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

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

        if (IsBuiltInProfile(CurrentProfile))
        {
            StopRecording(clearStatus: false);
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        string action = _selectedAction;
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
        MarkDirty();
        BuildActionList();
        SelectAction(action);
        StopRecording(clearStatus: false);
        StatusText.Text = string.IsNullOrEmpty(chord)
            ? Loc.F("keybindings.clearedAction", PlayerActionCatalog.Label(action))
            : Loc.F("keybindings.bound", PlayerActionCatalog.Label(action), chord);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (IsBuiltInProfile(CurrentProfile) || _selectedAction is null)
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        string action = _selectedAction;
        _bindings = KeyBindings.ClearAction(_bindings, action);
        MarkDirty();
        BuildActionList();
        SelectAction(action);
        StatusText.Text = Loc.F("keybindings.clearedAction", PlayerActionCatalog.Label(action));
    }

    private void OnResetOneClick(object sender, RoutedEventArgs e)
    {
        if (IsBuiltInProfile(CurrentProfile) || _selectedAction is null)
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        string action = _selectedAction;
        var reset = KeyBindings.ClearAction(_bindings, action).ToList();
        foreach (string chord in KeyBindings.DefaultChords(action, KeyBindingProfiles.Default))
        {
            if (KeyBindings.FindConflict(reset, chord, action) is null)
            {
                reset.Add(new KeyBindingSetting(action, chord));
            }
        }

        _bindings = reset;
        MarkDirty();
        BuildActionList();
        SelectAction(action);
        StatusText.Text = Loc.F("keybindings.resetOneAction", PlayerActionCatalog.Label(action));
    }

    private async void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        if (IsBuiltInProfile(CurrentProfile))
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        bool reset = await Dialogs.ConfirmAsync(
            XamlRoot,
            Loc.T("keybindings.resetAllTitle"),
            Loc.T("keybindings.resetAllBody"),
            Loc.T("keybindings.resetAll"));
        if (!reset)
        {
            return;
        }

        _bindings = KeyBindings.CreateProfileDefaults(KeyBindingProfiles.Default);
        MarkDirty();
        BuildActionList();
        SelectAction(_selectedAction);
        StatusText.Text = Loc.T("keybindings.resetAllDone");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (IsBuiltInProfile(CurrentProfile))
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        Persist();
        _dirty = false;
        UpdateProfileState();
        StatusText.Text = Loc.T("keybindings.saved");
    }

    private async void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedIndex < 0)
        {
            return;
        }

        List<string> names = ProfileNames();
        int targetIndex = Math.Clamp(ProfileBox.SelectedIndex, 0, names.Count - 1);
        string target = names[targetIndex];
        string current = CurrentProfile;
        if (string.Equals(current, target, StringComparison.Ordinal))
        {
            return;
        }

        if (_dirty)
        {
            bool? save = await ShowUnsavedDialogAsync();
            if (save is null)
            {
                _loading = true;
                try
                {
                    ProfileBox.SelectedIndex = IndexOfProfile(current);
                }
                finally
                {
                    _loading = false;
                }

                return;
            }

            if (save == true)
            {
                Persist();
            }

            _dirty = false;
        }

        _loading = true;
        try
        {
            StopRecording(clearStatus: false);
            SaveCurrentUserProfile();
            SetProfileCore(target);
            KeyBindingUserProfile? saved = FindUserProfile(target);
            App.Settings.KeyBindings = saved is null
                ? new List<KeyBindingSetting>()
                : saved.Bindings
                    .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
                    .ToList();
            App.Settings.KeyBindingProfile = target;
            _saving = true;
            try
            {
                App.SaveSettings();
            }
            finally
            {
                _saving = false;
            }

            _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, target).ToList();
            _dirty = false;
            BuildActionList();
            SelectAction(_selectedAction);
        }
        finally
        {
            _loading = false;
        }

        UpdateProfileState();
        StatusText.Text = Loc.F("keybindings.profileChanged", ProfileBox.Items[targetIndex]);
    }

    private async void OnCopyToCustomClick(object sender, RoutedEventArgs e)
    {
        string source = CurrentProfile;
        if (!IsBuiltInProfile(source))
        {
            return;
        }

        CopyToCustom();
        StatusText.Text = Loc.F(
            "keybindings.copiedToCustom",
            IsEnglish ? KeyBindingProfiles.LabelEnglish(source) : KeyBindingProfiles.Label(source));
    }

    private void OnNewProfileClick(object sender, RoutedEventArgs e) => _ = CreateProfileAsync();

    private async Task CreateProfileAsync()
    {
        string? name = await PromptProfileNameAsync(
            IsEnglish ? "New keyboard profile" : "新建按键方案",
            IsEnglish ? "Custom profile" : "自定义方案");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (ProfileNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusText.Text = IsEnglish ? "A profile with that name already exists." : "已存在同名方案。";
            return;
        }

        if (_dirty)
        {
            Persist();
        }

        var profile = new KeyBindingUserProfile
        {
            Name = name,
            Bindings = _bindings
                .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
                .ToList(),
        };
        App.Settings.UserKeyProfiles.Add(profile);
        App.Settings.KeyBindingProfile = name;
        App.Settings.KeyBindings = profile.Bindings
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();
        _dirty = false;
        App.SaveSettings();
        BuildProfileChoices();
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, name).ToList();
        BuildActionList();
        SelectAction(_selectedAction);
        StatusText.Text = IsEnglish
            ? $"Profile “{name}” created."
            : $"已创建方案“{name}”。";
    }

    private async void OnRenameProfileClick(object sender, RoutedEventArgs e)
    {
        string current = CurrentProfile;
        KeyBindingUserProfile? saved = FindUserProfile(current);
        if (saved is null)
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        string? name = await PromptProfileNameAsync(
            IsEnglish ? "Rename keyboard profile" : "重命名按键方案",
            saved.Name);
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, saved.Name, StringComparison.Ordinal))
        {
            return;
        }

        if (ProfileNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusText.Text = IsEnglish ? "A profile with that name already exists." : "已存在同名方案。";
            return;
        }

        saved.Name = name;
        App.Settings.KeyBindingProfile = name;
        App.SaveSettings();
        BuildProfileChoices();
        StatusText.Text = IsEnglish
            ? $"Profile renamed to “{name}”."
            : $"方案已重命名为“{name}”。";
    }

    private void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        string current = CurrentProfile;
        KeyBindingUserProfile? saved = FindUserProfile(current);
        if (saved is null)
        {
            StatusText.Text = Loc.T("keybindings.builtInHint");
            return;
        }

        App.Settings.UserKeyProfiles.Remove(saved);
        App.Settings.KeyBindingProfile = KeyBindingProfiles.Wiina;
        App.Settings.KeyBindings.Clear();
        _dirty = false;
        App.SaveSettings();
        BuildProfileChoices();
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile).ToList();
        BuildActionList();
        SelectAction(_selectedAction);
        StatusText.Text = IsEnglish
            ? $"Profile “{saved.Name}” deleted."
            : $"已删除方案“{saved.Name}”。";
    }

    private async Task<string?> PromptProfileNameAsync(string title, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 260 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = Loc.T("common.ok"),
            CloseButtonText = Loc.T("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return box.Text.Trim();
    }

    private void CopyToCustom()
    {
        string name = IsEnglish ? "Custom" : "自定义";
        int suffix = 2;
        while (ProfileNames().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            name = IsEnglish ? $"Custom {suffix++}" : $"自定义 {suffix++}";
        }

        var profile = new KeyBindingUserProfile
        {
            Name = name,
            Bindings = _bindings
                .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
                .ToList(),
        };
        App.Settings.UserKeyProfiles.Add(profile);
        App.Settings.KeyBindingProfile = name;
        App.Settings.KeyBindings = profile.Bindings
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();
        _dirty = false;
        App.SaveSettings();
        BuildProfileChoices();
        ProfileBox.SelectedIndex = IndexOfProfile(name);
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, name).ToList();
        BuildActionList();
        SelectAction(_selectedAction);
        UpdateProfileState();
    }

    private void SetProfileCore(string profile)
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
    }

    private void Persist()
    {
        App.Settings.KeyBindings = _bindings
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();
        SaveCurrentUserProfile();
        _saving = true;
        try
        {
            App.SaveSettings();
        }
        finally
        {
            _saving = false;
        }
    }

    private void SaveCurrentUserProfile()
    {
        string current = CurrentProfile;
        if (IsBuiltInProfile(current))
        {
            return;
        }

        KeyBindingUserProfile? saved = FindUserProfile(current);
        if (saved is null)
        {
            saved = new KeyBindingUserProfile { Name = current };
            App.Settings.UserKeyProfiles.Add(saved);
        }

        saved.Bindings = _bindings
            .Select(binding => new KeyBindingSetting(binding.Action, binding.Chord))
            .ToList();
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

            if (IsBuiltInProfile(CurrentProfile))
            {
                CopyToCustom();
            }

            _bindings = imported.ToList();
            MarkDirty();
            BuildActionList();
            SelectAction(_selectedAction);
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

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text.Trim();
        string? selected = _selectedAction;
        BuildActionList();
        if (selected is not null &&
            _rows.Any(row => string.Equals(row.Action, selected, StringComparison.Ordinal)))
        {
            SelectAction(selected);
        }
    }

    private void OnSettingsChanged()
    {
        if (_recording || _saving || _dirty)
        {
            return;
        }

        string? selected = _selectedAction;
        _bindings = KeyBindings.Resolve(App.Settings.KeyBindings, App.Settings.KeyBindingProfile).ToList();
        BuildActionList();
        SelectAction(selected);
        UpdateProfileState();
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
        SectionLabel.Text = Loc.T("keybindings.section");
        SearchBox.PlaceholderText = Loc.T("keybindings.searchPlaceholder");
        UpdateProfileState();
    }

    private async Task<bool?> ShowUnsavedDialogAsync()
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("keybindings.unsavedTitle"),
            Content = Loc.T("keybindings.unsavedBody"),
            PrimaryButtonText = Loc.T("common.save"),
            SecondaryButtonText = Loc.T("keybindings.discard"),
            CloseButtonText = Loc.T("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null,
        };
    }
}
