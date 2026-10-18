using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Wiina.App.Interop;

namespace Wiina.App.Controls;

public sealed partial class OscBottomView : UserControl
{
    private ChromeBlurLayer? _blur;

    public OscBottomView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_blur is not null || XamlRoot is null)
        {
            return;
        }

        // Live blurred frame behind the docked OSC; the acrylic strip stays
        // until the first blurred frame arrives.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 0, out ChromeBlurLayer layer))
        {
            return;
        }

        _blur = layer;
        _blur.FirstFrame += OnFirstBlurFrame;
        _blur.RaiseFirstFrameIfReady();
    }

    private void OnFirstBlurFrame(object? sender, EventArgs e)
    {
        OscAcrylicPanel.Background = null;
        OscBlurTintLayer.Visibility = Visibility.Visible;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_blur is not null)
        {
            _blur.FirstFrame -= OnFirstBlurFrame;
        }

        _blur = null;
    }

    public TimelineControl Timeline => InnerTimeline;

    public OscToolbar Toolbar => ToolbarGroup;

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
