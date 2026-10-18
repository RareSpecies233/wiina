using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

public sealed partial class OscBottomView : UserControl
{
    public OscBottomView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    public TimelineControl Timeline => InnerTimeline;

    public VolumeSlider Volume => VolumeGroup;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Hide toolbar first, then volume, then the duration label; the
        // transport and timeline always survive.
        ToolbarGroup.Visibility = e.NewSize.Width < 720
            ? Visibility.Collapsed
            : Visibility.Visible;
        VolumeGroup.Visibility = e.NewSize.Width < 560
            ? Visibility.Collapsed
            : Visibility.Visible;
        DurationLabel.Visibility = e.NewSize.Width < 520
            ? Visibility.Collapsed
            : Visibility.Visible;
        CurrentTimeLabel.Visibility = e.NewSize.Width < 380
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
