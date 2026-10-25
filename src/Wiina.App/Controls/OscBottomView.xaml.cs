using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.App.Interop;
using Wiina.Core.Settings;

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
        ActualThemeChanged += (_, _) => ApplyBlurVisuals();
    }

    public TimelineControl Timeline => InnerTimeline;

    public OscToolbar Toolbar => ToolbarGroup;

    public VolumeSlider Volume => VolumeGroup;

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

        // Live blurred frame behind the docked OSC. Until the first frame
        // arrives, and whenever the mode is Solid, the opaque strip stays.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 0, out ChromeBlurLayer layer))
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

        // The blurred frame and its tint are children of the panel, so they
        // paint over its opaque ChromeSolidBrush; the Background itself is
        // never touched. Solid therefore cannot lose its paint, and the XAML
        // ThemeResource binding keeps following theme/accent changes.
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
