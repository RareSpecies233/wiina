using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

public sealed partial class OscFloatingView : UserControl
{
    public OscFloatingView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    public TimelineControl Timeline => InnerTimeline;

    public FrameworkElement DragHandle => DragArea;

    /// <summary>Clamp the floating shell to the window while keeping it usable.</summary>
    public void SetAvailableWidth(double windowWidth)
    {
        Width = Math.Clamp(windowWidth - 24, 220, 460);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Compression priority: toolbar first, then volume; the timeline is
        // the last fragment to shrink and never forces the window width.
        ToolbarGroup.Visibility = e.NewSize.Width < 420
            ? Visibility.Collapsed
            : Visibility.Visible;
        VolumeGroup.Visibility = e.NewSize.Width < 340
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (e.NewSize.Width < 290)
        {
            CurrentTimeLabel.Visibility = Visibility.Collapsed;
            DurationLabel.Visibility = Visibility.Collapsed;
        }
        else
        {
            CurrentTimeLabel.Visibility = Visibility.Visible;
            DurationLabel.Visibility = Visibility.Visible;
        }
    }
}
