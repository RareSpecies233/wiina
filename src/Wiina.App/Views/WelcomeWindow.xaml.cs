using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core;
using Wiina.Core.History;
using Wiina.Core.Models;
using Wiina.Core.Settings;

namespace Wiina.App.Views;

/// <summary>
/// Stage 7 welcome window (560×440): identity, file/URL entry points, resume
/// row, recent history and drag/drop. Shown on startup when no media was given.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private readonly HistoryStore _history = new();
    private IReadOnlyList<RecentItem> _items = Array.Empty<RecentItem>();
    private HistoryEntry? _resume;

    public WelcomeWindow()
    {
        InitializeComponent();
        Title = Loc.T("welcome.title");
        RootGrid.RequestedTheme = App.ResolveTheme();

        WindowShell.Prepare(
            this,
            (int)Math.Clamp(App.Settings.WelcomeWindowWidth, 480, 1600),
            (int)Math.Clamp(App.Settings.WelcomeWindowHeight, 360, 1200),
            resizable: true,
            extendTitleBar: true,
            titleBar: DragRegion,
            dark: App.IsDarkTheme,
            minWidth: 480,
            minHeight: 360);
        RestoreWindowBounds();

        VersionText.Text = Loc.F("welcome.version", Branding.Version);
        Refresh();
        Closed += OnClosed;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
            App.Settings.WelcomeWindowWidth = Math.Max(480, AppWindow.Size.Width / scale);
            App.Settings.WelcomeWindowHeight = Math.Max(360, AppWindow.Size.Height / scale);
            App.Settings.WelcomeWindowX = AppWindow.Position.X;
            App.Settings.WelcomeWindowY = AppWindow.Position.Y;
            App.SaveSettings();
        }
        catch
        {
            // Bounds persistence must never block closing.
        }

        WindowShell.Detach(this);
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (App.Settings.WelcomeWindowX == int.MinValue ||
                App.Settings.WelcomeWindowY == int.MinValue)
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
                App.Settings.WelcomeWindowX,
                work.X - width + 120,
                work.X + work.Width - 120);
            int y = Math.Clamp(
                App.Settings.WelcomeWindowY,
                work.Y,
                work.Y + work.Height - 80);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
            // Fall back to the default placement.
        }
    }

    private void Refresh()
    {
        IReadOnlyList<HistoryEntry> recent = _history.Recent(10);
        _items = recent
            .Select(entry => new RecentItem(
                entry,
                string.IsNullOrWhiteSpace(entry.Title)
                    ? System.IO.Path.GetFileName(entry.Path)
                    : entry.Title,
                entry.Duration > 0
                    ? TimeSpan.FromSeconds(entry.Duration).ToString(@"mm\:ss")
                    : string.Empty))
            .ToArray();

        RecentList.ItemsSource = _items;
        RecentEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearRecentButton.IsEnabled = _items.Count > 0;

        _resume = recent.FirstOrDefault(entry => entry.Position > 5 && entry.Duration > 30);
        if (_resume is null)
        {
            ResumeCard.Visibility = Visibility.Collapsed;
        }
        else
        {
            ResumeCard.Visibility = Visibility.Visible;
            ResumeTitle.Text = string.IsNullOrWhiteSpace(_resume.Title)
                ? System.IO.Path.GetFileName(_resume.Path)
                : _resume.Title;
            ResumePosition.Text = Loc.F("welcome.resume", TimeSpan.FromSeconds(_resume.Position).ToString("mm\\:ss"), TimeSpan.FromSeconds(_resume.Duration).ToString("mm\\:ss"));
        }
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e) =>
        _ = PickAndOpenAsync();

    private async Task PickAndOpenAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WindowShell.InitializePicker(this, picker);
        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0)
        {
            Open(files.Select(file => file.Path));
        }
    }

    private async void OnOpenUrlClick(object sender, RoutedEventArgs e)
    {
        OpenUrlRequest? request = await Dialogs.OpenUrlAsync(RootGrid.XamlRoot, App.Settings);
        if (request is not null)
        {
            Open(new[] { request.Url }, request.NewWindow);
        }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e) => App.ShowAboutWindow();

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        App.ShowSettingsWindow(SettingsPageId.General, null);

    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e) =>
        App.OpenFolder(App.DataFolder);

    private async void OnClearRecentClick(object sender, RoutedEventArgs e)
    {
        if (!await Dialogs.ConfirmAsync(
                RootGrid.XamlRoot,
                Loc.T("welcome.clearRecentTitle"),
                Loc.T("welcome.clearRecentBody"),
                Loc.T("welcome.clear")))
        {
            return;
        }

        _history.Save(Array.Empty<HistoryEntry>());
        Refresh();
    }

    private void OnResumeTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_resume is not null)
        {
            Open(new[] { _resume.Path });
        }
    }

    private void OnRecentDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentItem item)
        {
            Open(new[] { item.Entry.Path });
        }
    }

    private async void OnDropAsync(object sender, DragEventArgs e)
    {
        try
        {
            var paths = new List<string>();
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
                foreach (IStorageItem item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Path))
                    {
                        paths.Add(item.Path);
                    }
                }
            }

            if (paths.Count == 0 && e.DataView.Contains(StandardDataFormats.WebLink))
            {
                Uri? uri = await e.DataView.GetWebLinkAsync();
                if (uri is not null)
                {
                    paths.Add(uri.AbsoluteUri);
                }
            }

            if (paths.Count == 0 && e.DataView.Contains(StandardDataFormats.Text))
            {
                string text = await e.DataView.GetTextAsync();
                if (Wiina.Core.Text.UrlValidation.TryNormalize(text, out string url))
                {
                    paths.Add(url);
                }
            }

            if (paths.Count > 0)
            {
                Open(paths);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("welcome drop failed: " + ex);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Loc.T("welcome.dragCaption");
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private void Open(IEnumerable<string> paths, bool newWindow = false)
    {
        App.OpenMedia(paths, newWindow);
        App.HideWelcomeWindow();
    }

    public sealed record RecentItem(HistoryEntry Entry, string Title, string Subtitle);
}
