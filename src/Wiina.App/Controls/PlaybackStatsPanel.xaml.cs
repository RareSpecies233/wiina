using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

/// <summary>One label/value pair in a statistics group.</summary>
public sealed record PlaybackStatRow(string Group, string Label, string Value);

/// <summary>
/// PotPlayer-style playback statistics card (mpv "Tab" panel). Rows are created
/// once per label set and only their values are replaced on each tick, so the
/// one-second refresh does not rebuild the visual tree.
/// </summary>
public sealed partial class PlaybackStatsPanel : UserControl
{
    private readonly List<TextBlock> _valueBlocks = new();
    private string _layoutSignature = string.Empty;

    public PlaybackStatsPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public void Show(string? subtitle = null)
    {
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            SubtitleText.Text = subtitle;
        }

        Visibility = Visibility.Visible;
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    public bool IsPanelVisible => Visibility == Visibility.Visible;

    public void SetRows(IReadOnlyList<PlaybackStatRow> rows)
    {
        string signature = string.Join(
            '\u001f',
            rows.Select(row => row.Group + '\u001e' + row.Label));
        if (!string.Equals(signature, _layoutSignature, StringComparison.Ordinal))
        {
            Rebuild(rows);
            _layoutSignature = signature;
            return;
        }

        for (int i = 0; i < _valueBlocks.Count && i < rows.Count; i++)
        {
            if (!string.Equals(_valueBlocks[i].Text, rows[i].Value, StringComparison.Ordinal))
            {
                _valueBlocks[i].Text = rows[i].Value;
            }
        }
    }

    private void Rebuild(IReadOnlyList<PlaybackStatRow> rows)
    {
        RowsHost.Children.Clear();
        _valueBlocks.Clear();
        var groupStyle = (Style)Resources["StatGroupTextStyle"];
        var labelStyle = (Style)Resources["StatLabelTextStyle"];
        var valueStyle = (Style)Resources["StatValueTextStyle"];

        string? group = null;
        foreach (PlaybackStatRow row in rows)
        {
            if (!string.Equals(group, row.Group, StringComparison.Ordinal))
            {
                group = row.Group;
                RowsHost.Children.Add(new TextBlock
                {
                    Text = group,
                    Style = groupStyle,
                    Margin = new Thickness(0, _valueBlocks.Count == 0 ? 0 : 10, 0, 3),
                });
            }

            var grid = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(0, 2, 0, 2),
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = row.Label,
                Style = labelStyle,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var value = new TextBlock
            {
                Text = row.Value,
                Style = valueStyle,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Top,
            };

            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
            RowsHost.Children.Add(grid);
            _valueBlocks.Add(value);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
