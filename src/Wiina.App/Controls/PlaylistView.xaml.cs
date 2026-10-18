using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wiina.Mpv;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;

namespace Wiina.App.Controls;

/// <summary>
/// Playlist page backed by mpv's cached <c>playlist</c> node. No UI-thread
/// mpv_get_property(NODE) calls: the shell pushes the cache in and every
/// action is a queued mpv command.
/// </summary>
public sealed partial class PlaylistView : UserControl
{
    private readonly ObservableCollection<PlaylistRow> _rows = new();
    private int _currentIndex = -1;
    private int _dragFrom = -1;

    public PlaylistView()
    {
        InitializeComponent();
        ItemList.ItemsSource = _rows;
    }

    public event EventHandler? AddRequested;

    public event EventHandler? SaveRequested;

    public event EventHandler<int>? PlayRequested;

    public event EventHandler<IReadOnlyList<int>>? RemoveRequested;

    public event EventHandler<(int From, int To)>? MoveRequested;

    public void SetItems(IReadOnlyList<MpvPlaylistItem> items, int currentIndex)
    {
        _rows.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            MpvPlaylistItem item = items[i];
            _rows.Add(new PlaylistRow
            {
                Index = i,
                Title = item.DisplayName,
                Meta = item.Filename ?? string.Empty,
                Path = item.Filename ?? string.Empty,
                IsCurrent = i == currentIndex || item.IsCurrent,
            });
        }

        _currentIndex = currentIndex;
        CountText.Text = items.Count == 0
            ? "0 项"
            : $"{items.Count} 项 · 第 {Math.Clamp(currentIndex + 1, 0, items.Count)} 项";
        EmptyHint.Visibility = items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void UpdateCurrent(int currentIndex)
    {
        _currentIndex = currentIndex;
        foreach (PlaylistRow row in _rows)
        {
            row.IsCurrent = row.Index == currentIndex;
        }

        CountText.Text = _rows.Count == 0
            ? "0 项"
            : $"{_rows.Count} 项 · 第 {Math.Clamp(currentIndex + 1, 0, _rows.Count)} 项";
    }

    public void SetProgress(double position, double duration)
    {
        double ratio = duration <= 0 ? 0 : Math.Clamp(position / duration, 0, 1);
        foreach (PlaylistRow row in _rows)
        {
            row.Progress = row.IsCurrent ? ratio : 0;
            row.ShowProgress = row.IsCurrent && duration > 0;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) =>
        AddRequested?.Invoke(this, EventArgs.Empty);

    private void OnSaveClick(object sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnRemoveClick(object sender, RoutedEventArgs e) =>
        RemoveSelected();

    private void OnMoveUpClick(object sender, RoutedEventArgs e) =>
        MoveFirstSelected(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) =>
        MoveFirstSelected(1);

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject) is { } row)
        {
            PlayRequested?.Invoke(this, row.Index);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PlaylistRow? row = SelectedRows().FirstOrDefault();
            if (row is not null)
            {
                PlayRequested?.Invoke(this, row.Index);
                e.Handled = true;
            }
        }
    }

    private void OnListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        PlaylistRow? row = FindRow(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        if (!ItemList.SelectedItems.Contains(row))
        {
            ItemList.SelectedItems.Clear();
            ItemList.SelectedItems.Add(row);
        }

        var flyout = new MenuFlyout();
        MenuFlyoutItem Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
            return item;
        }

        Add("播放", () => PlayRequested?.Invoke(this, row.Index));
        Add("上移", () => MoveFirstSelected(-1));
        Add("下移", () => MoveFirstSelected(1));
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add("移除", RemoveSelected);
        Add("复制路径", () => CopyPath(row.Path));
        flyout.ShowAt(ItemList, new FlyoutShowOptions { Position = e.GetPosition(ItemList) });
        e.Handled = true;
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is PlaylistRow row)
        {
            _dragFrom = _rows.IndexOf(row);
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        int from = _dragFrom;
        _dragFrom = -1;
        if (from < 0 || e.Items.FirstOrDefault() is not PlaylistRow row)
        {
            return;
        }

        int to = _rows.IndexOf(row);
        if (to >= 0 && to != from)
        {
            MoveRequested?.Invoke(this, (from, to));
        }
    }

    private IEnumerable<PlaylistRow> SelectedRows() =>
        ItemList.SelectedItems
            .OfType<PlaylistRow>()
            .OrderBy(row => row.Index);

    private void RemoveSelected()
    {
        int[] indexes = SelectedRows().Select(row => row.Index).ToArray();
        if (indexes.Length > 0)
        {
            RemoveRequested?.Invoke(this, indexes);
        }
    }

    private void MoveFirstSelected(int delta)
    {
        PlaylistRow? row = SelectedRows().FirstOrDefault();
        if (row is null)
        {
            return;
        }

        int to = row.Index + delta;
        if (to < 0 || to >= _rows.Count)
        {
            return;
        }

        MoveRequested?.Invoke(this, (row.Index, to));
    }

    private static void CopyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
        }
        catch
        {
            // Clipboard can be busy; copying is a convenience only.
        }
    }

    private static PlaylistRow? FindRow(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null and not ListViewItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return (current as ListViewItem)?.Content as PlaylistRow;
    }
}

public sealed class PlaylistRow : INotifyPropertyChanged
{
    private bool _isCurrent;
    private double _progress;
    private bool _showProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string IndexText => (Index + 1).ToString();

    public FontWeight TitleWeight =>
        _isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
    public string IndicatorGlyph => _isCurrent ? "\uE768" : string.Empty;

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndicatorGlyph));
            OnPropertyChanged(nameof(TitleWeight));
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(_progress - clamped) < 0.0001)
            {
                return;
            }

            _progress = clamped;
            OnPropertyChanged();
        }
    }

    public bool ShowProgress
    {
        get => _showProgress;
        set
        {
            if (_showProgress == value)
            {
                return;
            }

            _showProgress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressVisibility));
        }
    }

    public Visibility ProgressVisibility =>
        _showProgress ? Visibility.Visible : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
