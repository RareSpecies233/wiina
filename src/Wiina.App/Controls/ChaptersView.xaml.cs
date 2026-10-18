using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wiina.Core.Models;
using Wiina.Core.Text;
using Wiina.App.Localization;

namespace Wiina.App.Controls;

/// <summary>Chapter list from mpv's cached <c>chapter-list</c> node.</summary>
public sealed partial class ChaptersView : UserControl
{
    private readonly ObservableCollection<ChapterRow> _rows = new();
    private int _currentIndex = -1;

    public ChaptersView()
    {
        InitializeComponent();
        ChapterList.ItemsSource = _rows;
    }

    public event EventHandler<int>? JumpRequested;

    public void SetChapters(IReadOnlyList<ChapterInfo> chapters, int currentIndex)
    {
        _rows.Clear();
        for (int i = 0; i < chapters.Count; i++)
        {
            ChapterInfo chapter = chapters[i];
            _rows.Add(new ChapterRow
            {
                Index = i,
                TimeText = TimeFormatter.Format(chapter.Time),
                Title = chapter.DisplayName,
                IsCurrent = i == currentIndex,
            });
        }

        _currentIndex = currentIndex;
        CountText.Text = Loc.F("chapters.count", chapters.Count);
        EmptyHint.Visibility = chapters.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetCurrent(int index)
    {
        _currentIndex = index;
        foreach (ChapterRow row in _rows)
        {
            row.IsCurrent = row.Index == index;
        }
    }

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject) is { } row)
        {
            JumpRequested?.Invoke(this, row.Index);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        if (ChapterList.SelectedItem is ChapterRow row)
        {
            JumpRequested?.Invoke(this, row.Index);
            e.Handled = true;
        }
        else if (_currentIndex >= 0 && _currentIndex < _rows.Count)
        {
            JumpRequested?.Invoke(this, _currentIndex);
            e.Handled = true;
        }
    }

    private static ChapterRow? FindRow(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null and not ListViewItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return (current as ListViewItem)?.Content as ChapterRow;
    }
}

public sealed class ChapterRow : INotifyPropertyChanged
{
    private bool _isCurrent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; init; }

    public string TimeText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndicatorGlyph)));
        }
    }
}
