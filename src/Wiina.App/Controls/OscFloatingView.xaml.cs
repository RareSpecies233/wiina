using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.App.Interop;
using Wiina.Core.Settings;

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
        ActualThemeChanged += (_, _) => ApplyBlurVisuals();
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.ModeChanged += OnBlurModeChanged;
        EnsureAttached();
        ApplyBlurVisuals();
    }

    private void EnsureAttached()
    {
        if (_blur is not null || XamlRoot is null)
        {
            return;
        }

        // Live blurred frame behind the floating OSC. Until the first frame
        // arrives, and whenever the mode is Solid, the opaque shell stays.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 8, out ChromeBlurLayer layer))
        {
            return;
        }

        _blur = layer;
        _blur.FirstFrame += OnFirstBlurFrame;
        _blur.RaiseFirstFrameIfReady();
    }

    private void OnBlurModeChanged(object? sender, EventArgs e)
    {
        EnsureAttached();
        ApplyBlurVisuals();
    }

    private void OnFirstBlurFrame(object? sender, EventArgs e) => ApplyBlurVisuals();

    private void ApplyBlurVisuals()
    {
        bool live = ChromeBlur.Current.Mode != ChromeBlurMode.Solid;
        if (_blur is not null)
        {
            _blur.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        }

        // Blur and tint are children of the drag surface, so they paint above
        // its opaque ChromeSolidBrush while it stays the pointer target for
        // dragging. Never assigning Background keeps the ThemeResource bound.
        bool blurred = live && _blur is { HasFrame: true };
        OscBlurTintLayer.Visibility = blurred ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.ModeChanged -= OnBlurModeChanged;
        if (_blur is not null)
        {
            _blur.FirstFrame -= OnFirstBlurFrame;
        }

        _blur = null;
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
