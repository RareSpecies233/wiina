using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.App.Interop;

namespace Wiina.App.Controls;

public sealed partial class OscFloatingView : UserControl
{
    private ChromeBlurLayer? _blur;

    public OscFloatingView()
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

        // Real blurred frame behind the floating OSC, clipped by its rounded
        // border. The acrylic panel stays until the first frame arrives.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 8.0, out ChromeBlurLayer layer))
        {
            return;
        }

        _blur = layer;
        _blur.FirstFrame += OnFirstBlurFrame;
        _blur.RaiseFirstFrameIfReady();
    }

    private void OnFirstBlurFrame(object? sender, EventArgs e)
    {
        DragArea.Background = null;
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
