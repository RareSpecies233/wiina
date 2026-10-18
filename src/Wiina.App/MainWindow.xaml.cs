using System.IO;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Wiina.App.Interop;
using Wiina.Mpv;

namespace Wiina.App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _resizeTimer;
    private MpvPlayer? _player;
    private nint _swapChain;
    private int _clickCount;
    private bool _started;

    private static readonly string TracePath =
        Path.Combine(Path.GetTempPath(), "wiina-trace.log");

    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(
                TracePath,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Tracing must never break the app.
        }
    }

    public MainWindow()
    {
        Trace("MainWindow ctor begin");
        InitializeComponent();
        Title = "Wiina — Stage 0";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(1280, 720));
        ConfigureCaptionButtons();

        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(80);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => ApplyVideoLayout();

        VideoHost.Loaded += OnVideoHostLoaded;
        RootGrid.SizeChanged += OnRootSizeChanged;
        RootGrid.Loaded += OnRootLoaded;
        Closed += OnClosed;
        Trace("MainWindow ctor end");
    }

    private void ConfigureCaptionButtons()
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.ExtendsContentIntoTitleBar = true;
        bar.BackgroundColor = Colors.Transparent;
        bar.InactiveBackgroundColor = Colors.Transparent;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Colors.White;
        bar.ButtonInactiveForegroundColor = Colors.Gray;
        bar.ButtonHoverBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        bar.ButtonPressedBackgroundColor = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureCaptionButtons();
        if (RootGrid.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }
    }

    private void OnVideoHostLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Trace("VideoHost loaded");
        try
        {
            ApplyVideoLayout();
            (int width, int height) = GetPixelSize();
            if (width < 16 || height < 16)
            {
                width = 1280;
                height = 720;
            }

            Trace($"creating mpv {width}x{height}");
            _player = new MpvPlayer();
            _player.Log += message => Trace("mpv: " + message);
            _player.DisplaySwapChainChanged += OnDisplaySwapChainChanged;
            _player.Start(width, height);
            Trace("mpv started");
            _player.ObserveProperty("display-swapchain", MpvFormat.Int64);
            Trace("display-swapchain observed");

            long existing = _player.GetPropertyInt64("display-swapchain");
            Trace($"existing display-swapchain = 0x{existing:x}");
            if (existing > 0)
            {
                OnDisplaySwapChainChanged(existing);
            }

            // A moving test pattern; no media file needed.
            _player.Command("loadfile", "av://lavfi:testsrc=size=1280x720:rate=30");
            Trace("loadfile issued");
            StatusText.Text = "Stage 0 — mpv running; F11 toggles fullscreen";
        }
        catch (Exception ex)
        {
            Trace("mpv start failed: " + ex);
            StatusText.Text = "mpv failed: " + ex.Message;
            OverlayText.Text = "mpv failed to start.\n" + ex.Message;
        }
    }

    private void OnDisplaySwapChainChanged(long swapChain)
    {
        Trace($"DisplaySwapChainChanged 0x{swapChain:x}");
        DispatcherQueue.TryEnqueue(() =>
        {
            if (swapChain <= 0 || _swapChain == (nint)swapChain)
            {
                return;
            }

            _swapChain = (nint)swapChain;
            try
            {
                Trace("SetSwapChain begin");
                SwapChainPanelInterop.SetSwapChain(VideoHost, _swapChain);
                Trace("SetSwapChain ok");
                StatusText.Text = "Stage 0 — swapchain bound; XAML overlay is above the video";
            }
            catch (Exception ex)
            {
                Trace("swapchain bind failed: " + ex);
                StatusText.Text = "swapchain bind failed: " + ex.Message;
            }
        });
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    /// <summary>
    /// mpv renders at physical pixels; the panel is laid out at that pixel
    /// size and scaled by 1/dpi so it fills the window. This avoids
    /// IDXGISwapChain2::SetMatrixTransform, which some drivers reject on
    /// composition swapchains (DXGI_ERROR_INVALID_CALL).
    /// </summary>
    private void ApplyVideoLayout()
    {
        double dpi = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        double dipWidth = RootGrid.ActualWidth;
        double dipHeight = RootGrid.ActualHeight;
        if (dipWidth < 16 || dipHeight < 16)
        {
            return;
        }

        int pixelWidth = Math.Max(1, (int)Math.Round(dipWidth * dpi));
        int pixelHeight = Math.Max(1, (int)Math.Round(dipHeight * dpi));

        VideoHost.Width = pixelWidth;
        VideoHost.Height = pixelHeight;

        float inverse = (float)(1.0 / dpi);
        VideoTransform.ScaleX = inverse;
        VideoTransform.ScaleY = inverse;

        RootGrid.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, dipWidth, dipHeight),
        };

        if (_player is { IsRunning: true })
        {
            bool ok = _player.TrySetPropertyString(
                "d3d11-composition-size",
                $"{pixelWidth}x{pixelHeight}");
            if (!ok)
            {
                Trace($"resize {pixelWidth}x{pixelHeight} failed");
            }
        }
    }

    private (int Width, int Height) GetPixelSize()
    {
        double dpi = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        return (
            Math.Max(1, (int)Math.Round(RootGrid.ActualWidth * dpi)),
            Math.Max(1, (int)Math.Round(RootGrid.ActualHeight * dpi)));
    }

    private void OnToggleFullscreen(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
    }

    private void OnOverlayClick(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        OverlayText.Text = $"XAML overlay clicked {_clickCount}× — input works above the video";
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Trace("Window closed");
        _resizeTimer.Stop();
        if (_swapChain != 0)
        {
            try
            {
                SwapChainPanelInterop.SetSwapChain(VideoHost, 0);
            }
            catch
            {
                // The window is going away; nothing useful to do here.
            }

            _swapChain = 0;
        }

        _player?.Dispose();
        _player = null;
    }
}
