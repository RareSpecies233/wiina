using System.IO;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Wiina.App.Interop;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Mpv;

namespace Wiina.App;

public sealed partial class MainWindow : Window
{
    private static readonly (string Name, string Glyph)[] IconSamples =
    {
        ("Play", "\uE768"),
        ("Pause", "\uE769"),
        ("Previous", "\uE892"),
        ("Next", "\uE893"),
        ("Volume", "\uE767"),
        ("Mute", "\uE74F"),
        ("FullScreen", "\uE740"),
        ("BackToWindow", "\uE73F"),
        ("Settings", "\uE713"),
        ("List", "\uE8FD"),
        ("Subtitles", "\uE8D8"),
        ("Pin", "\uE718"),
        ("Unpin", "\uE77A"),
        ("Theme", "\uE793"),
        ("Puzzle", "\uEA86"),
        ("PictureInPicture", "\uE8B9"),
        ("Camera", "\uE722"),
        ("ChevronLeft", "\uE76B"),
        ("ChevronRight", "\uE76C"),
        ("AppIcon", "\uE8B7"),
    };

    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherQueueTimer _resizeTimer;
    private AppSettings _settings;
    private MpvPlayer? _player;
    private nint _swapChain;
    private bool _started;
    private bool _sidebarVisible;
    private bool _compact;
    private double _videoAspect = 16.0 / 9.0;

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
        InitializeComponent();
        _settings = _settingsStore.Load();

        Title = "Wiina";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(
            Math.Max(640, _settings.WindowWidth),
            Math.Max(360, _settings.WindowHeight)));
        if (_settings.WindowX != int.MinValue && _settings.WindowY != int.MinValue)
        {
            AppWindow.Move(new PointInt32(_settings.WindowX, _settings.WindowY));
        }

        ConfigureCaptionButtons();
        TrySetBackdrop();

        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(80);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => ApplyVideoLayout();

        TitleBar.Title = "Wiina";
        TitleBar.PinClicked += (_, _) => TogglePin();
        TitleBar.LayoutClicked += (_, _) => ToggleLayout();
        TitleBar.OscClicked += (_, _) => CycleOsc();
        TitleBar.SidebarClicked += (_, _) => ToggleSidebar();
        TitleBar.ThemeClicked += (_, _) => CycleTheme();
        TitleBar.IconsClicked += async (_, _) => await ShowIconDialogAsync();
        TitleBar.WindowClicked += (_, _) => ShowWindowMenu();
        SidebarLeading.CloseClicked += (_, _) => HideSidebar();
        SidebarTrailing.CloseClicked += (_, _) => HideSidebar();

        VideoHost.Loaded += OnVideoHostLoaded;
        VideoFrame.SizeChanged += OnVideoFrameSizeChanged;
        RootGrid.SizeChanged += OnRootSizeChanged;
        RootGrid.Loaded += OnRootLoaded;
        Closed += OnClosed;

        ApplySettings();
    }

    private void ApplySettings()
    {
        ApplyTheme();
        ApplyLayout();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        }
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        DispatcherQueue.TryEnqueue(ConfigureCaptionButtons);
    }

    private void ConfigureCaptionButtons()
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;
        bool dark = RootGrid.ActualTheme != ElementTheme.Light;
        Color background = dark
            ? Color.FromArgb(0xCC, 0x10, 0x11, 0x14)
            : Color.FromArgb(0xCC, 0xF3, 0xF3, 0xF3);

        bar.ExtendsContentIntoTitleBar = true;
        bar.BackgroundColor = background;
        bar.InactiveBackgroundColor = background;
        bar.ButtonBackgroundColor = background;
        bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        bar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x1F, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0, 0, 0);
    }

    private void TrySetBackdrop()
    {
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica is unavailable on Windows 10 / some VMs; the solid
            // background remains a valid fallback.
        }
    }

    private void ApplyLayout()
    {
        _compact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < 520;
        bool docked = _settings.Layout == WindowLayoutMode.Docked;
        Grid.SetRow(VideoFrame, docked ? 1 : 0);
        Grid.SetRowSpan(VideoFrame, docked ? 1 : 3);

        TitleBar.SetDocked(docked);
        TitleBar.IsPinned = _settings.AlwaysOnTop;
        TitleBar.SetTopOscVisible(
            _settings.OscPosition == OscPosition.Top && !_compact);

        bool useBottomOsc =
            _settings.OscPosition == OscPosition.Bottom ||
            (docked && _settings.OscPosition == OscPosition.Floating);
        OscBottom.Visibility = useBottomOsc && !_compact
            ? Visibility.Visible
            : Visibility.Collapsed;
        bool sidebarVisible = !_compact && _sidebarVisible;
        OscFloating.Visibility = !docked && !_compact && !sidebarVisible &&
            _settings.OscPosition == OscPosition.Floating
            ? Visibility.Visible
            : Visibility.Collapsed;

        bool showLeading = sidebarVisible &&
            _settings.SidebarSide == SidebarSide.Leading;
        bool showTrailing = sidebarVisible &&
            _settings.SidebarSide == SidebarSide.Trailing;
        SidebarLeading.Visibility = showLeading ? Visibility.Visible : Visibility.Collapsed;
        SidebarTrailing.Visibility = showTrailing ? Visibility.Visible : Visibility.Collapsed;

        double sidebarTop = _settings.OscPosition == OscPosition.Top ? 68 : 40;
        SidebarLeading.Margin = new Thickness(0, sidebarTop, 0, 0);
        SidebarTrailing.Margin = new Thickness(0, sidebarTop, 0, 0);

        double left = docked && showLeading ? _settings.SidebarWidth : 0;
        double right = docked && showTrailing ? _settings.SidebarWidth : 0;
        Thickness chromeMargin = new(left, 0, right, 0);

        VideoFrame.Margin = docked ? chromeMargin : new Thickness(0);
        TitleBar.Margin = new Thickness(0);

        // The bottom bar spans the full window; the sidebar overlays its right
        // edge in docked mode. Stage 4 will make the fragments compress.
        OscBottom.Margin = new Thickness(0);

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureCaptionButtons();
        WindowInterop.Attach(
            this,
            285,
            120,
            () => _settings.LockAspectRatio,
            () => _videoAspect);

        if (RootGrid.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }
    }

    private void OnVideoFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactState();
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateCompactState()
    {
        bool compact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < 520;
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        ApplyLayout();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
        ConfigureCaptionButtons();
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
            _player.ObserveProperty("display-swapchain", MpvFormat.Int64);

            long existing = _player.GetPropertyInt64("display-swapchain");
            if (existing > 0)
            {
                OnDisplaySwapChainChanged(existing);
            }

            _player.Command("loadfile", "av://lavfi:testsrc=size=1280x720:rate=30");
            Trace("loadfile issued");
        }
        catch (Exception ex)
        {
            Trace("mpv start failed: " + ex);
            TitleBar.Title = "Wiina — mpv failed";
        }
    }

    private void OnDisplaySwapChainChanged(long swapChain)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (swapChain <= 0 || _swapChain == (nint)swapChain)
            {
                return;
            }

            _swapChain = (nint)swapChain;
            try
            {
                SwapChainPanelInterop.SetSwapChain(VideoHost, _swapChain);
            }
            catch (Exception ex)
            {
                Trace("swapchain bind failed: " + ex);
            }
        });
    }

    private void ApplyVideoLayout()
    {
        UpdateCompactState();
        double dpi = VideoFrame.XamlRoot?.RasterizationScale ?? 1.0;
        double dipWidth = VideoFrame.ActualWidth;
        double dipHeight = VideoFrame.ActualHeight;
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

        VideoFrame.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, dipWidth, dipHeight),
        };

        if (_player is { IsRunning: true })
        {
            if (!_player.TrySetPropertyString(
                    "d3d11-composition-size",
                    $"{pixelWidth}x{pixelHeight}"))
            {
                Trace($"resize {pixelWidth}x{pixelHeight} failed");
            }
        }
    }

    private (int Width, int Height) GetPixelSize()
    {
        double dpi = VideoFrame.XamlRoot?.RasterizationScale ?? 1.0;
        return (
            Math.Max(1, (int)Math.Round(VideoFrame.ActualWidth * dpi)),
            Math.Max(1, (int)Math.Round(VideoFrame.ActualHeight * dpi)));
    }

    private void TogglePin()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        }

        TitleBar.IsPinned = _settings.AlwaysOnTop;
        SaveSettings();
    }

    private void ToggleLayout()
    {
        _settings.Layout = _settings.Layout == WindowLayoutMode.Docked
            ? WindowLayoutMode.EdgeToEdge
            : WindowLayoutMode.Docked;
        ApplyLayout();
        SaveSettings();
    }

    private void CycleOsc()
    {
        _settings.OscPosition = _settings.OscPosition switch
        {
            OscPosition.Floating => OscPosition.Bottom,
            OscPosition.Bottom => OscPosition.Top,
            _ => OscPosition.Floating,
        };
        ApplyLayout();
        SaveSettings();
    }

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        ApplyLayout();
        SaveSettings();
    }

    private void HideSidebar()
    {
        _sidebarVisible = false;
        ApplyLayout();
        SaveSettings();
    }

    private void CycleTheme()
    {
        _settings.Theme = _settings.Theme switch
        {
            ThemeMode.System => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.Light,
            _ => ThemeMode.System,
        };
        ApplyTheme();
        SaveSettings();
    }

    private async Task ShowIconDialogAsync()
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach ((string name, string glyph) in IconSamples)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 20,
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{name}   U+{(int)glyph[0]:X4}",
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(row);
        }

        var dialog = new ContentDialog
        {
            Title = "Segoe Fluent Icons — Stage 1 映射",
            Content = new ScrollViewer { Content = panel, Height = 420 },
            CloseButtonText = "关闭",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void ShowWindowMenu()
    {
        var flyout = new MenuFlyout();
        AddItem("0.5×", () => ResizeToScale(0.5));
        AddItem("1×", () => ResizeToScale(1.0));
        AddItem("2×", () => ResizeToScale(2.0));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem("适合屏幕", FitToScreen);
        flyout.ShowAt(TitleBar);

        void AddItem(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }
    }

    private void ResizeToScale(double scale)
    {
        double dpi = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        int width = Math.Max(320, (int)Math.Round(1280 * scale * dpi));
        int height = Math.Max(180, (int)Math.Round(720 * scale * dpi));
        AppWindow.Resize(new SizeInt32(width, height));
    }

    private void FitToScreen()
    {
        RectInt32 area = DisplayArea
            .GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;

        int width = Math.Max(320, area.Width - 96);
        int height = (int)Math.Round(width / (16.0 / 9.0));
        if (height > area.Height - 96)
        {
            height = Math.Max(180, area.Height - 96);
            width = (int)Math.Round(height * (16.0 / 9.0));
        }

        AppWindow.MoveAndResize(new RectInt32(
            area.X + Math.Max(0, (area.Width - width) / 2),
            area.Y + Math.Max(0, (area.Height - height) / 2),
            width,
            height));
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

        DispatcherQueue.TryEnqueue(ApplyVideoLayout);
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            args.Handled = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            DispatcherQueue.TryEnqueue(ApplyVideoLayout);
        }
    }

    private void OnToggleSidebarAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleSidebar();
    }

    private void OnToggleLayoutAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleLayout();
    }

    private void OnCycleOscAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleOsc();
    }

    private void OnCycleThemeAccel(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CycleTheme();
    }

    private void SaveSettings()
    {
        try
        {
            SizeInt32 size = AppWindow.Size;
            PointInt32 position = AppWindow.Position;
            _settings.WindowWidth = size.Width;
            _settings.WindowHeight = size.Height;
            _settings.WindowX = position.X;
            _settings.WindowY = position.Y;
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            Trace("save settings failed: " + ex);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Trace("Window closed");
        _resizeTimer.Stop();
        SaveSettings();
        WindowInterop.Detach(this);

        if (_swapChain != 0)
        {
            try
            {
                SwapChainPanelInterop.SetSwapChain(VideoHost, 0);
            }
            catch
            {
                // Window is going away.
            }

            _swapChain = 0;
        }

        _player?.Dispose();
        _player = null;
    }
}
