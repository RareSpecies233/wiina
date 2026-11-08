using Wiina.App.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wiina.Core.Files;
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

    /// <summary>
    /// Single-row drag reorder. <c>From</c>/<c>To</c> are final playlist
    /// indexes; the shell maps them through
    /// <see cref="PlaylistMove.ResolveTarget"/>.
    /// </summary>
    public event EventHandler<(int From, int To)>? MoveRequested;

    /// <summary>
    /// Multi-row up/down move. Each pair is already the exact
    /// <c>playlist-move</c> argument sequence, in execution order; the shell
    /// sends them verbatim.
    /// </summary>
    public event EventHandler<IReadOnlyList<(int From, int To)>>? MoveBatchRequested;

    public void SetItems(IReadOnlyList<MpvPlaylistItem> items, int currentIndex)
    {
        // mpv pushes a whole new playlist on every change; the view rebuilds the
        // row collection. Capture selection (by path, falling back to index) and
        // the scroll offset first so a move keeps the edited row selected and the
        // list does not jump back to the top.
        List<(int Index, string Path)> selection = CaptureSelection();
        double verticalOffset = GetVerticalOffset();

        ItemList.SelectedItems.Clear();
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
            ? Loc.T("playlist.items0")
            : Loc.F("playlist.items", items.Count, Math.Clamp(currentIndex + 1, 0, items.Count));
        EmptyHint.Visibility = items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        RestoreSelection(selection, items.Count);
        UpdateMoveButtons();

        if (verticalOffset > 0.5)
        {
            DispatcherQueue.TryEnqueue(() => RestoreScrollOffset(verticalOffset));
        }
    }

    public void UpdateCurrent(int currentIndex)
    {
        _currentIndex = currentIndex;
        foreach (PlaylistRow row in _rows)
        {
            row.IsCurrent = row.Index == currentIndex;
        }

        CountText.Text = _rows.Count == 0
            ? Loc.T("playlist.items0")
            : Loc.F("playlist.items", _rows.Count, Math.Clamp(currentIndex + 1, 0, _rows.Count));
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
        MoveSelectedBatch(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) =>
        MoveSelectedBatch(1);

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateMoveButtons();

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

        Add(Loc.T("playlist.play"), () => PlayRequested?.Invoke(this, row.Index));
        MenuFlyoutItem moveUp = Add(Loc.T("playlist.moveUp"), () => MoveSelectedBatch(-1));
        moveUp.IsEnabled = CanMoveSelection(-1);
        MenuFlyoutItem moveDown = Add(Loc.T("playlist.moveDown"), () => MoveSelectedBatch(1));
        moveDown.IsEnabled = CanMoveSelection(1);
        flyout.Items.Add(new MenuFlyoutSeparator());
        Add(Loc.T("playlist.remove"), RemoveSelected);
        Add(Loc.T("playlist.copyPath"), () => CopyPath(row.Path));
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

    /// <summary>
    /// Moves the whole selection one slot up/down. Contiguous runs move as a
    /// block; a run already touching the edge is skipped as a unit.
    /// </summary>
    private void MoveSelectedBatch(int delta)
    {
        IReadOnlyList<(int From, int To)> moves =
            PlaylistMove.ResolveBatch(SelectedIndexes(), _rows.Count, delta);
        if (moves.Count > 0)
        {
            MoveBatchRequested?.Invoke(this, moves);
        }
    }

    private int[] SelectedIndexes() =>
        SelectedRows().Select(row => row.Index).ToArray();

    private bool CanMoveSelection(int delta) =>
        PlaylistMove.CanMoveBatch(SelectedIndexes(), _rows.Count, delta);

    private void UpdateMoveButtons()
    {
        MoveUpButton.IsEnabled = CanMoveSelection(-1);
        MoveDownButton.IsEnabled = CanMoveSelection(1);
    }

    private List<(int Index, string Path)> CaptureSelection() =>
        ItemList.SelectedItems
            .OfType<PlaylistRow>()
            .Select(row => (row.Index, row.Path))
            .OrderBy(entry => entry.Index)
            .ToList();

    /// <summary>
    /// Re-selects the same files after a rebuild. Paths are stable across
    /// reorders; the old index is only a fallback for empty/duplicate paths.
    /// </summary>
    private void RestoreSelection(
        IReadOnlyList<(int Index, string Path)> selection,
        int count)
    {
        if (selection.Count == 0 || count == 0)
        {
            return;
        }

        var indexesByPath = new Dictionary<string, Queue<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (PlaylistRow row in _rows)
        {
            if (string.IsNullOrEmpty(row.Path))
            {
                continue;
            }

            if (!indexesByPath.TryGetValue(row.Path, out Queue<int>? indexes))
            {
                indexes = new Queue<int>();
                indexesByPath[row.Path] = indexes;
            }

            indexes.Enqueue(row.Index);
        }

        var restored = new HashSet<int>();
        foreach ((int oldIndex, string path) in selection)
        {
            int index = -1;
            if (!string.IsNullOrEmpty(path)
                && indexesByPath.TryGetValue(path, out Queue<int>? candidates))
            {
                while (candidates.Count > 0 && restored.Contains(candidates.Peek()))
                {
                    candidates.Dequeue();
                }

                if (candidates.Count > 0)
                {
                    index = candidates.Dequeue();
                }
            }

            if (index < 0 && oldIndex >= 0 && oldIndex < count)
            {
                index = oldIndex;
            }

            if (index >= 0 && index < _rows.Count && restored.Add(index))
            {
                ItemList.SelectedItems.Add(_rows[index]);
            }
        }
    }

    private double GetVerticalOffset() =>
        FindScrollViewer(ItemList)?.VerticalOffset ?? 0;

    private void RestoreScrollOffset(double offset)
    {
        ItemList.UpdateLayout();
        FindScrollViewer(ItemList)?.ChangeView(null, offset, null, disableAnimation: true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
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
