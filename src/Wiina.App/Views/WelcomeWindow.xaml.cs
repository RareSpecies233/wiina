using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
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
        Title = "欢迎使用 Wiina";
        bool dark = App.Settings.Theme != ThemeMode.Light;
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        WindowShell.Prepare(
            this,
            560,
            440,
            resizable: false,
            extendTitleBar: true,
            titleBar: DragRegion,
            dark: dark,
            minWidth: 560,
            minHeight: 440);

        VersionText.Text = "版本 " + Branding.Version;
        Refresh();
        Closed += (_, _) => WindowShell.Detach(this);
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
            ResumePosition.Text = $"上次看到 {TimeSpan.FromSeconds(_resume.Position):mm\\:ss} / {TimeSpan.FromSeconds(_resume.Duration):mm\\:ss}";
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
                "清除最近记录",
                "将从欢迎窗口和“打开最近”菜单中移除全部记录。",
                "清除"))
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
        e.DragUIOverride.Caption = "用 Wiina 打开";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private void Open(IEnumerable<string> paths, bool newWindow = false)
    {
        App.OpenMedia(paths, newWindow);
        App.HideWelcomeWindow();
    }

    public sealed record RecentItem(HistoryEntry Entry, string Title, string Subtitle);
}
