using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Wiina.App.Interop;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core.Localization;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.Text;
using Wiina.Mpv;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wiina.App.Views;

/// <summary>
/// Stage 7 settings window: eleven catalog-driven pages, live search with jump +
/// highlight, section dots and the key-binding editor.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly Color AccentColor = Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF);
    private static readonly Color AccentTint = Color.FromArgb(0x22, 0x4C, 0xC2, 0xFF);
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0, 0, 0);

    private readonly List<(SettingsPageId Page, Button Button)> _navButtons = new();
    private readonly List<(SettingsRowDefinition Row, Action Refresh, Border Container)> _rowWidgets = new();
    private readonly List<(string Section, FrameworkElement Element)> _sectionHeaders = new();

    private SettingsPageId _currentPage = SettingsPageId.General;
    private DispatcherQueueTimer? _saveTimer;
    private bool _loading;
    private bool _saving;
    private bool _applying;

    public SettingsWindow()
        : this(ParsePage(App.Settings.SettingsPage), null)
    {
    }

    public SettingsWindow(SettingsPageId page, string? rowId)
    {
        InitializeComponent();
        Title = Loc.T("ui.wiinaSettings");
        _currentPage = page;

        bool dark = App.Settings.Theme != ThemeMode.Light;
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        WindowShell.Prepare(
            this,
            (int)Math.Clamp(App.Settings.SettingsWindowWidth, 600, 2400),
            (int)Math.Clamp(App.Settings.SettingsWindowHeight, 440, 1800),
            resizable: true,
            extendTitleBar: true,
            titleBar: DragRegion,
            dark: dark,
            minWidth: 600,
            minHeight: 440);
        RestoreWindowBounds();

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => SaveNow();

        RootGrid.Loaded += (_, _) => RootGrid.Focus(FocusState.Programmatic);

        App.SettingsChanged += OnSettingsChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;

        BuildNavigation();
        Navigate(page, rowId);
    }

    /// <summary>Activates an existing settings window and jumps to a page/row.</summary>
    public void NavigateTo(SettingsPageId page, string? rowId)
    {
        Navigate(page, rowId);
        Activate();
    }

    private static SettingsPageId ParsePage(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out SettingsPageId page)
            ? page
            : SettingsPageId.General;

    // ------------------------------------------------------------- navigation

    private void BuildNavigation()
    {
        NavPanel.Children.Clear();
        _navButtons.Clear();
        foreach (SettingsPageInfo page in SettingsCatalog.Pages)
        {
            if (page.Id == SettingsPageId.KeyBindings)
            {
                // Key bindings live in a dedicated window; no left-rail entry.
                continue;
            }
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
            };
            content.Children.Add(new FontIcon
            {
                Glyph = page.Glyph,
                FontSize = 14,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
            });
            string pageTitle = SettingsLocalization.PageTitle(page, Loc.CurrentLanguage);
            content.Children.Add(new TextBlock
            {
                Text = pageTitle,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button
            {
                Tag = page.Id,
                Content = content,
                Style = (Style)RootGrid.Resources["SettingsNavButtonStyle"],
            };
            SettingsPageId id = page.Id;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, pageTitle);
            button.Click += (_, _) => Navigate(id);
            NavPanel.Children.Add(button);
            _navButtons.Add((id, button));
        }
    }

    private void Navigate(SettingsPageId page, string? rowId = null)
    {
        if (page == SettingsPageId.KeyBindings)
        {
            // The standalone page was removed from navigation; keep stale
            // persisted page ids and search deep links working via the
            // Control → 键盘方案 row that opens the dedicated window.
            page = SettingsPageId.Control;
            rowId = "control.keybindings";
        }

        _currentPage = page;
        App.Settings.SettingsPage = page.ToString();

        foreach ((SettingsPageId id, Button button) in _navButtons)
        {
            button.Style = (Style)RootGrid.Resources[
                id == page ? "SettingsNavButtonSelectedStyle" : "SettingsNavButtonStyle"];
        }

        SettingsPageInfo info = SettingsCatalog.Page(page);
        PageTitle.Text = SettingsLocalization.PageTitle(info, Loc.CurrentLanguage);
        PageSummary.Text = SettingsLocalization.PageSummary(info, Loc.CurrentLanguage);

        PagePanel.Children.Clear();
        _rowWidgets.Clear();
        _sectionHeaders.Clear();
        SectionPanel.Children.Clear();

        string? section = null;
        SettingsRowDefinition? keyBindingsRow = null;
        foreach (SettingsRowDefinition row in SettingsCatalog.RowsFor(page))
        {
            if (row.Id == "control.keybindings")
            {
                // Ordered below the keyboard profile row.
                keyBindingsRow = row;
                continue;
            }

            if (!string.Equals(section, row.Section, StringComparison.Ordinal))
            {
                section = row.Section;
                AddSectionHeader(row.Section);
            }

            AddRow(row);

            if (keyBindingsRow is not null && row.Id == "control.key-profile")
            {
                AddRow(keyBindingsRow);
                keyBindingsRow = null;
            }
        }

        if (keyBindingsRow is not null)
        {
            AddRow(keyBindingsRow);
        }

        BuildSectionDots(page);
        if (rowId is not null)
        {
            DispatcherQueue.TryEnqueue(() => HighlightRow(rowId));
        }
    }

    private void AddSectionHeader(string section)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 14, 0, 4),
            ColumnSpacing = 8,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = new SolidColorBrush(Color.FromArgb(0x88, 0x4C, 0xC2, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var text = new TextBlock
        {
            Text = SettingsLocalization.Section(section, Loc.CurrentLanguage),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        PagePanel.Children.Add(grid);
        _sectionHeaders.Add((section, grid));
    }

    private void BuildSectionDots(SettingsPageId page)
    {
        SectionPanel.Children.Clear();
        foreach (string section in SettingsCatalog.Sections(page))
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            content.Children.Add(new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = SettingsLocalization.Section(section, Loc.CurrentLanguage),
                FontSize = 11,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var button = new Button
            {
                Content = content,
                Style = (Style)RootGrid.Resources["SettingsSectionButtonStyle"],
            };
            string target = section;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                button,
                SettingsLocalization.Section(section, Loc.CurrentLanguage));
            button.Click += (_, _) => ScrollToSection(target);
            SectionPanel.Children.Add(button);
        }
    }

    private void ScrollToSection(string section)
    {
        foreach ((string name, FrameworkElement element) in _sectionHeaders)
        {
            if (string.Equals(name, section, StringComparison.Ordinal))
            {
                element.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.1,
                });
                return;
            }
        }
    }

    private void HighlightRow(string rowId)
    {
        foreach ((SettingsRowDefinition row, Action refresh, Border container) in _rowWidgets)
        {
            if (!string.Equals(row.Id, rowId, StringComparison.Ordinal))
            {
                continue;
            }

            container.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.33,
            });
            container.BorderBrush = new SolidColorBrush(AccentColor);
            container.Background = new SolidColorBrush(AccentTint);
            _ = ClearHighlightAsync(container);
            return;
        }
    }

    private static async Task ClearHighlightAsync(Border container)
    {
        await Task.Delay(1000);
        container.BorderBrush = new SolidColorBrush(TransparentColor);
        container.Background = new SolidColorBrush(TransparentColor);
    }

    // ------------------------------------------------------------------- rows

    private void AddRow(SettingsRowDefinition row)
    {
        var container = new Border
        {
            Padding = new Thickness(10, 8, 8, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(TransparentColor),
            Background = new SolidColorBrush(TransparentColor),
            Margin = new Thickness(0, 1, 0, 1),
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = SettingsCatalog.Page(row.Page).Glyph,
            FontSize = 14,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };
        grid.Children.Add(icon);

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        string rowTitle = SettingsLocalization.Title(row, Loc.CurrentLanguage);
        textStack.Children.Add(new TextBlock
        {
            Text = rowTitle,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        string description = row.Id == "control.keybindings"
            ? Loc.T("settings.keyBindingsHint")
            : SettingsLocalization.Description(row, Loc.CurrentLanguage);
        if (!row.IsAvailable)
        {
            description += "\n" + Loc.F(
                "settings.unavailable",
                SettingsLocalization.UnavailableReason(row.UnavailableReason ?? string.Empty, Loc.CurrentLanguage));
        }

        textStack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        FrameworkElement control = BuildControl(row, out Action refresh);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);

        if (!row.IsAvailable)
        {
            container.Opacity = 0.55;
            if (control is Control disabled)
            {
                disabled.IsEnabled = false;
            }
            else
            {
                control.IsHitTestVisible = false;
            }
        }

        container.Child = grid;
        PagePanel.Children.Add(container);
        _rowWidgets.Add((row, refresh, container));
    }

    private FrameworkElement BuildControl(SettingsRowDefinition row, out Action refresh)
    {
        object? value = row.Value(App.Settings);
        refresh = () => { };

        switch (row.Kind)
        {
            case SettingsRowKind.Toggle:
            {
                var toggle = new ToggleSwitch
                {
                    IsOn = value is true,
                    OnContent = string.Empty,
                    OffContent = string.Empty,
                    MinWidth = 0,
                };
                toggle.Toggled += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    Apply(row, toggle.IsOn, immediate: true);
                };
                refresh = () =>
                {
                    _loading = true;
                    toggle.IsOn = row.Value(App.Settings) is true;
                    _loading = false;
                };
                return toggle;
            }

            case SettingsRowKind.Choice:
            {
                var combo = new ComboBox
                {
                    ItemsSource = SettingsLocalization.Choices(row, Loc.CurrentLanguage),
                    SelectedIndex = Math.Clamp(
                        Convert.ToInt32(value ?? 0),
                        0,
                        Math.Max(0, row.Choices.Count - 1)),
                    MinWidth = 160,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    Apply(row, combo.SelectedIndex, immediate: true);
                };
                refresh = () =>
                {
                    _loading = true;
                    combo.SelectedIndex = Math.Clamp(
                        Convert.ToInt32(row.Value(App.Settings) ?? 0),
                        0,
                        Math.Max(0, row.Choices.Count - 1));
                    _loading = false;
                };
                return combo;
            }

            case SettingsRowKind.Number:
            {
                var box = new NumberBox
                {
                    Minimum = row.Minimum,
                    Maximum = row.Maximum,
                    SmallChange = row.Step,
                    LargeChange = Math.Max(row.Step, row.Step * 5),
                    Value = Convert.ToDouble(value ?? 0),
                    Width = 108,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    AcceptsExpression = false,
                };
                var wrap = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                wrap.Children.Add(box);
                if (!string.IsNullOrEmpty(row.Suffix))
                {
                    wrap.Children.Add(new TextBlock
                    {
                        Text = row.Suffix,
                        FontSize = 11,
                        Opacity = 0.65,
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }

                box.ValueChanged += (_, args) =>
                {
                    if (_loading || double.IsNaN(args.NewValue))
                    {
                        return;
                    }

                    Apply(row, args.NewValue, immediate: false);
                };
                refresh = () =>
                {
                    _loading = true;
                    box.Value = Convert.ToDouble(row.Value(App.Settings) ?? 0);
                    _loading = false;
                };
                return wrap;
            }

            case SettingsRowKind.Text:
            {
                var box = new TextBox
                {
                    Text = value as string ?? string.Empty,
                    Width = 220,
                    FontSize = 12,
                };
                box.TextChanged += (_, _) =>
                {
                    if (_loading)
                    {
                        return;
                    }

                    Apply(row, box.Text, immediate: false);
                };
                refresh = () =>
                {
                    _loading = true;
                    box.Text = row.Value(App.Settings) as string ?? string.Empty;
                    _loading = false;
                };
                return box;
            }

            case SettingsRowKind.Action:
            {
                var button = new Button
                {
                    Content = ActionVerb(row.ActionId),
                    Style = (Style)RootGrid.Resources["SettingsActionButtonStyle"],
                };
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    ToolTipService.SetToolTip(button, text);
                }

                button.Click += async (_, _) => await RunActionAsync(row);
                refresh = () =>
                {
                    if (row.Value(App.Settings) is string current &&
                        !string.IsNullOrWhiteSpace(current))
                    {
                        ToolTipService.SetToolTip(button, current);
                    }
                };
                return button;
            }

            case SettingsRowKind.Info:
            {
                var info = new TextBlock
                {
                    Text = InfoValue(row),
                    FontSize = 12,
                    MaxWidth = 240,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Right,
                    Opacity = 0.8,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                refresh = () => info.Text = InfoValue(row);
                return info;
            }

            case SettingsRowKind.Font:
            {
                var button = new Button
                {
                    Content = value as string ?? "Segoe UI",
                    Style = (Style)RootGrid.Resources["SettingsActionButtonStyle"],
                };
                button.Click += async (_, _) =>
                {
                    string? picked = await Dialogs.PickFontAsync(
                        RootGrid.XamlRoot,
                        row.Value(App.Settings) as string ?? "Segoe UI");
                    if (picked is null)
                    {
                        return;
                    }

                    row.Apply(App.Settings, picked);
                    button.Content = picked;
                    App.ActiveWindow?.ApplySettingsRow(row.Id);
                    SaveNow();
                };
                refresh = () => button.Content =
                    row.Value(App.Settings) as string ?? "Segoe UI";
                return button;
            }

            case SettingsRowKind.KeyBindings:
            {
                var button = new Button
                {
                    Content = Loc.T("settings.openKeyBindings"),
                    Style = (Style)RootGrid.Resources["SettingsActionButtonStyle"],
                };
                button.Click += (_, _) => App.ShowKeyBindingWindow();
                return button;
            }

            case SettingsRowKind.PluginList:
            {
                var panel = new StackPanel { Spacing = 4, MaxWidth = 240 };
                panel.Children.Add(new TextBlock
                {
                    Text = Loc.T("settings.noPlugins"),
                    FontSize = 12,
                    Opacity = 0.7,
                });
                panel.Children.Add(new Button
                {
                    Content = Loc.T("settings.installPlugin"),
                    IsEnabled = false,
                    Style = (Style)RootGrid.Resources["SettingsActionButtonStyle"],
                });
                return panel;
            }

            default:
                return new TextBlock { Text = "—", Opacity = 0.6 };
        }
    }

    private static string ActionVerb(string? actionId) => actionId switch
    {
        "choose-screenshot-folder" => Loc.T("settings.choose"),
        "open-data-folder" or "open-config-folder" or "open-log-folder" or
            "open-plugin-folder" => Loc.T("settings.open"),
        "reset-video" or "reset-audio" or "reset-subtitles" or "reset-control" or
            "reset-all-settings" => Loc.T("settings.reset"),
        "clear-history" or "clear-thumbnails" => Loc.T("settings.clear"),
        "export-diagnostics" => Loc.T("settings.export"),
        "open-keybindings" => Loc.T("settings.openKeyBindings"),
        "install-plugin" => Loc.T("settings.install"),
        "open-url-dialog" or "open-log-viewer" or "open-inspector" or "open-about" or
            "open-video-filters" or "open-audio-filters" => Loc.T("settings.open"),
        "reset-equalizer" => Loc.T("settings.reset"),
        "toggle-technical-osd" => Loc.T("settings.toggle"),
        _ => Loc.T("settings.run"),
    };

    private string InfoValue(SettingsRowDefinition row) => row.Id switch
    {
        "advanced.renderer" => App.ActiveWindow?.GetRendererInfo() ?? Loc.T("settings.noActiveWindow"),
        "advanced.libmpv" => $"libmpv {MpvRuntime.ClientApiVersion}",
        _ => row.Value(App.Settings) as string ?? string.Empty,
    };

    private void Apply(SettingsRowDefinition row, object? value, bool immediate)
    {
        _applying = true;
        try
        {
            row.Apply(App.Settings, value);
            if (row.Id == "general.theme")
            {
                ApplyTheme();
            }

            App.ActiveWindow?.ApplySettingsRow(row.Id);
        }
        finally
        {
            _applying = false;
        }

        if (row.Id == "audio.max-volume")
        {
            // Clamping the current volume can change another row's value.
            RefreshRows();
        }

        if (immediate)
        {
            SaveNow();
            RefreshRows();
        }
        else
        {
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        WindowShell.ConfigureCaptionButtons(this, App.Settings.Theme != ThemeMode.Light);
    }

    private void RefreshRows()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            foreach ((SettingsRowDefinition _, Action refresh, Border _) in _rowWidgets)
            {
                refresh();
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveNow()
    {
        _saveTimer?.Stop();
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            App.SaveSettings();
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(Loc.T("ui.wiinaSettings"), Loc.F("settings.saveFailed", ex.Message));
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnSettingsChanged()
    {
        if (!_applying)
        {
            RefreshRows();
        }
    }

    // -------------------------------------------------------------- search

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchList.ItemsSource = null;
            return;
        }

        IReadOnlyList<SettingsSearchHit> hits =
            SettingsCatalog.SearchIndex.Search(query, 12, Loc.CurrentLanguage);
        SearchList.ItemsSource = hits;
        SearchPanel.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            SearchBox.Text = string.Empty;
            SearchPanel.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Enter && SearchList.Items.Count > 0)
        {
            CommitSearchHit(SearchList.Items[0] as SettingsSearchHit);
            e.Handled = true;
        }
    }

    private void OnSearchResultClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SettingsSearchHit hit)
        {
            CommitSearchHit(hit);
        }
    }

    private void CommitSearchHit(SettingsSearchHit? hit)
    {
        if (hit is null)
        {
            return;
        }

        SearchBox.Text = string.Empty;
        SearchPanel.Visibility = Visibility.Collapsed;
        Navigate(hit.Page.Id, hit.RowId);
    }

    // ------------------------------------------------------------- actions

    private async Task RunActionAsync(SettingsRowDefinition row)
    {
        try
        {
            switch (row.ActionId)
            {
                case "choose-screenshot-folder":
                {
                    var picker = new FolderPicker
                    {
                        SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    };
                    picker.FileTypeFilter.Add("*");
                    WindowShell.InitializePicker(this, picker);
                    StorageFolder? folder = await picker.PickSingleFolderAsync();
                    if (folder is null)
                    {
                        return;
                    }

                    App.Settings.ScreenshotFolder = folder.Path;
                    SaveNow();
                    RefreshRows();
                    break;
                }

                case "open-data-folder":
                case "open-config-folder":
                    App.OpenFolder(App.DataFolder);
                    break;

                case "open-log-folder":
                    App.OpenFolder(App.LogsFolder);
                    break;

                case "open-plugin-folder":
                    App.OpenFolder(App.PluginsFolder);
                    break;

                case "open-keybindings":
                    App.ShowKeyBindingWindow();
                    break;

                case "reset-video":
                    ResetVideoSettings();
                    SaveNow();
                    RefreshRows();
                    break;

                case "reset-audio":
                    ResetAudioSettings();
                    SaveNow();
                    RefreshRows();
                    break;

                case "reset-subtitles":
                    ResetSubtitleSettings();
                    SaveNow();
                    RefreshRows();
                    break;

                case "reset-control":
                    App.Settings.SingleClickAction = SingleClickAction.HideOsc;
                    App.Settings.WheelVolumeStep = 5;
                    App.Settings.WheelSeekSeconds = 5;
                    App.Settings.InvertWheelDirection = false;
                    SaveNow();
                    RefreshRows();
                    break;

                case "open-url-dialog":
                {
                    OpenUrlRequest? request = await Dialogs.OpenUrlAsync(
                        RootGrid.XamlRoot,
                        App.Settings);
                    if (request is not null)
                    {
                        App.OpenMedia(new[] { request.Url }, request.NewWindow);
                    }

                    break;
                }

                case "open-log-viewer":
                    App.ShowLogViewerWindow();
                    break;

                case "open-inspector":
                    App.ShowInspectorWindow();
                    break;

                case "open-video-filters":
                    App.ShowFiltersWindow(audioTab: false);
                    break;

                case "open-audio-filters":
                    App.ShowFiltersWindow(audioTab: true);
                    break;

                case "reset-equalizer":
                    App.Settings.EqualizerGains = Wiina.Core.Filters.EqualizerBands
                        .CreatePreset("Flat")
                        .ToList();
                    App.Settings.EqualizerPreset = "Flat";
                    App.ActiveWindow?.ApplyStage8Filters();
                    App.ActiveWindow?.SyncStage8Views();
                    SaveNow();
                    RefreshRows();
                    break;

                case "open-about":
                    App.ShowAboutWindow();
                    break;

                case "toggle-technical-osd":
                    App.ActiveWindow?.ToggleTechnicalOsd();
                    break;

                case "export-diagnostics":
                {
                    App.ExportDiagnostics();
                    break;
                }

                case "clear-history":
                    if (await Dialogs.ConfirmAsync(
                            RootGrid.XamlRoot,
                            Loc.T("settings.clearHistoryTitle"),
                            Loc.T("settings.clearHistoryBody"),
                            Loc.T("settings.clear")))
                    {
                        App.ClearHistory();
                    }

                    break;

                case "clear-thumbnails":
                    App.ActiveWindow?.ClearThumbnailCache();
                    break;

                case "reset-all-settings":
                    if (await Dialogs.ConfirmAsync(
                            RootGrid.XamlRoot,
                            Loc.T("settings.resetAllTitle"),
                            Loc.T("settings.resetAllBody"),
                            Loc.T("settings.reset")))
                    {
                        App.ResetSettings();
                        Navigate(SettingsPageId.General);
                    }

                    break;

                case "install-plugin":
                    // Disabled row: kept for completeness if the reason changes.
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(Loc.T("settings.actionFailed"), ex.Message);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = Loc.T("ui.close"),
            };
            await dialog.ShowAsync();
        }
        catch
        {
            // A dialog is already open or the window is closing.
        }
    }

    private void ResetVideoSettings()
    {
        App.Settings.VideoAspect = "no";
        App.Settings.VideoCropAspect = 0;
        App.Settings.VideoRotation = 0;
        App.Settings.VideoFlipHorizontal = false;
        App.Settings.VideoFlipVertical = false;
        App.Settings.VideoZoom = 0;
        App.Settings.VideoPanX = 0;
        App.Settings.VideoPanY = 0;
        App.Settings.VideoDeinterlace = false;
        App.Settings.VideoHardwareDecode = true;
        App.Settings.VideoHwdecMode = "auto-safe";
        App.Settings.VideoToneMapping = "auto";
        App.Settings.VideoHdrComputePeak = "auto";
        App.Settings.VideoTargetPeak = 0;
        App.Settings.VideoOutputLevels = "auto";
        App.Settings.VideoDelogoEnabled = false;
        App.Settings.VideoDelogoX = 0;
        App.Settings.VideoDelogoY = 0;
        App.Settings.VideoDelogoWidth = 320;
        App.Settings.VideoDelogoHeight = 80;
        App.Settings.VideoFilters.Clear();
    }

    private void ResetAudioSettings()
    {
        App.Settings.Volume = 100;
        App.Settings.MaxVolume = 130;
        App.Settings.Muted = false;
        App.Settings.AudioDelay = 0;
        App.Settings.AudioDevice = "auto";
        App.Settings.EqualizerEnabled = false;
        App.Settings.EqualizerGains = Wiina.Core.Filters.EqualizerBands
            .CreatePreset("Flat")
            .ToList();
        App.Settings.EqualizerPreset = "Flat";
        App.Settings.AudioFilters.Clear();
    }

    private void ResetSubtitleSettings()
    {
        App.Settings.SubtitleVisible = true;
        App.Settings.SubtitleDelay = 0;
        App.Settings.SubtitlePosition = 100;
        App.Settings.SubtitleScale = 1;
        App.Settings.SubtitleFontSize = 55;
        App.Settings.SubtitleFont = "Segoe UI";
        App.Settings.SubtitleColor = "#FFFFFFFF";
        App.Settings.SubtitleBorderSize = 3;
        App.Settings.SubtitleAssOverride = "scale";
        App.Settings.SubtitleBold = false;
        App.Settings.SubtitleItalic = false;
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Title = Loc.T("ui.wiinaSettings");
            BuildNavigation();
            Navigate(_currentPage);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
            App.Settings.SettingsWindowWidth = Math.Max(600, AppWindow.Size.Width / scale);
            App.Settings.SettingsWindowHeight = Math.Max(440, AppWindow.Size.Height / scale);
            App.Settings.SettingsWindowX = AppWindow.Position.X;
            App.Settings.SettingsWindowY = AppWindow.Position.Y;
            App.SaveSettings();
        }
        catch
        {
            // Bounds persistence must never block closing.
        }

        App.SettingsChanged -= OnSettingsChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        _saveTimer?.Stop();
        WindowShell.Detach(this);
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (App.Settings.SettingsWindowX == int.MinValue ||
                App.Settings.SettingsWindowY == int.MinValue)
            {
                return;
            }

            DisplayArea area = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Primary);
            RectInt32 work = area.WorkArea;
            int width = AppWindow.Size.Width;
            int height = AppWindow.Size.Height;
            int x = Math.Clamp(
                App.Settings.SettingsWindowX,
                work.X - width + 120,
                work.X + work.Width - 120);
            int y = Math.Clamp(
                App.Settings.SettingsWindowY,
                work.Y,
                work.Y + work.Height - 80);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
            // Fall back to the default placement.
        }
    }
}
