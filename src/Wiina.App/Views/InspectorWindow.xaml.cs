using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core.Models;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wiina.App.Views;

/// <summary>One read-only inspector row (section + property name + value).</summary>
public sealed record InspectorRow(string Section, string Name, string Value);

/// <summary>
/// Technical media inspector backed by live mpv properties of the active player
/// window (file/video/audio/subtitle/playback groups).
/// </summary>
public sealed partial class InspectorWindow : Window
{
    private readonly DispatcherQueueTimer _timer;

    public InspectorWindow()
    {
        InitializeComponent();
        Title = Loc.T("inspector.title");
        RootGrid.RequestedTheme = App.ResolveTheme();
        WindowShell.Prepare(
            this,
            660,
            600,
            resizable: true,
            dark: App.IsDarkTheme,
            minWidth: 480,
            minHeight: 360);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.5);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            WindowShell.Detach(this);
        };
    }

    private bool _refreshing;
    private bool _closed;

    private async void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        MainWindow? window = App.ActiveWindow;
        if (window is null)
        {
            TitleText.Text = Loc.T("inspector.noWindow");
            ContentPanel.Children.Clear();
            ContentPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("inspector.noWindowBody"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            return;
        }

        _refreshing = true;
        try
        {
            IReadOnlyList<InspectorRow> rows = await window.GetInspectorSnapshotAsync();
            if (_closed)
            {
                return;
            }

            TitleText.Text = Loc.T("inspector.title") + " — " + (window.CurrentTitle ?? Loc.T("inspector.untitled"));
            ContentPanel.Children.Clear();
            if (rows.Count == 0)
            {
                ContentPanel.Children.Add(new TextBlock
                {
                    Text = Loc.T("inspector.noMedia"),
                    FontSize = 12,
                    Opacity = 0.7,
                });
                return;
            }

            RenderRows(rows);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderRows(IReadOnlyList<InspectorRow> rows)
    {
        foreach (IGrouping<string, InspectorRow> group in rows.GroupBy(row => row.Section))
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 2),
            });

            var card = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x22, 0x80, 0x80, 0x80)),
                Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x10, 0x80, 0x80, 0x80)),
            };
            var stack = new StackPanel { Spacing = 4 };
            foreach (InspectorRow row in group)
            {
                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });
                grid.Children.Add(new TextBlock
                {
                    Text = row.Name,
                    FontSize = 11,
                    Opacity = 0.62,
                    TextWrapping = TextWrapping.Wrap,
                });
                var value = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                };
                Grid.SetColumn(value, 1);
                grid.Children.Add(value);
                stack.Children.Add(grid);
            }

            card.Child = stack;
            ContentPanel.Children.Add(card);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Refresh();

    private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshSwitch.IsOn)
        {
            Refresh();
        }
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        MainWindow? window = App.ActiveWindow;
        if (window is null)
        {
            return;
        }

        IReadOnlyList<InspectorRow> rows = await window.GetInspectorSnapshotAsync();
        if (_closed)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (InspectorRow row in rows)
        {
            builder.AppendLine($"[{row.Section}] {row.Name}: {row.Value}");
        }

        var package = new DataPackage();
        package.SetText(builder.ToString());
        Clipboard.SetContent(package);
    }
}
