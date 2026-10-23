using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Wiina.App.Interop;
using Wiina.Core.State;
using Wiina.App.Localization;

namespace Wiina.App;

/// <summary>
/// Self-drawn picture-in-picture window (Stage 6): borderless, 16:9 aspect
/// locked, always-on-top, with a compact transport strip that auto-hides.
/// The video surface is the same mpv swapchain as the main window; the main
/// window detaches it before this window binds it so exactly one panel owns it.
/// </summary>
public sealed partial class PipWindow : Window
{
    private readonly DispatcherQueueTimer _hideTimer;
    private readonly double _rasterizationScale;
    private bool _closedFromMain;
    private bool _pointerOverControls;
    private bool _pointerInside;
    private bool _isActive;
    private bool _isPinned = true;
    private double _lastWidth;
    private double _lastHeight;

    public PipWindow(
        RectInt32 workArea,
        RectInt32 mainFrame,
        int widthDip,
        int heightDip,
        double rasterizationScale)
    {
        InitializeComponent();

        _rasterizationScale = rasterizationScale > 0.1 ? rasterizationScale : 1.0;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        AppWindow.Title = Loc.T("pip.title");
        Title = Loc.T("pip.title");

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = true;
        }

        (int clampedWidthDip, int clampedHeightDip) = PipGeometry.Clamp(widthDip, heightDip);
        int width = Math.Max(1, (int)Math.Round(clampedWidthDip * _rasterizationScale));
        int height = Math.Max(1, (int)Math.Round(clampedHeightDip * _rasterizationScale));
        (int x, int y) = PipGeometry.PlaceBottomRight(
            workArea.X,
            workArea.Y,
            workArea.Width,
            workArea.Height,
            width,
            height,
            margin: 16);

        // Keep the PiP window off the main window's centre so it does not
        // immediately cover the placeholder the user has to click.
        if (mainFrame.Width > 0 && mainFrame.Height > 0)
        {
            (x, y) = NudgeAwayFromMain(x, y, width, height, workArea, mainFrame);
        }

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        WindowInterop.SetImmersiveDarkMode(this, dark: true);
        WindowInterop.Attach(
            this,
            (int)PipGeometry.MinWidth,
            (int)PipGeometry.MinHeight,
            () => true,
            () => PipGeometry.AspectRatio,
            appCommand: null,
            windowResizeStarted: null,
            windowMoveEnded: SnapToEdges,
            windowMoving: SnapMovingRect);

        Root.SizeChanged += OnRootSizeChanged;
        Root.Loaded += (_, _) =>
        {
            if (Root.XamlRoot is { } xamlRoot)
            {
                xamlRoot.Changed += OnXamlRootChanged;
            }
        };
        Activated += OnActivated;
        Closed += OnClosed;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideControls();

        // Start fully chrome-free; the hint, drag strip and transport appear
        // only after the pointer moves over the picture.
        HideControls();
    }

    /// <summary>"Return to main window" (button or double-click).</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>User closed the window (Alt+F4 / taskbar / close button).</summary>
    public event EventHandler? UserClosed;

    public event EventHandler? PlayPauseRequested;

    /// <summary>Physical buffer size the swapchain must adopt while in PiP.</summary>
    public event Action<int, int, double>? BufferSizeChanged;

    public bool IsClosed { get; private set; }

    public void AttachSwapChain(nint swapChain)
    {
        VideoHost.Attach(swapChain);
        UpdateSurface();
    }

    public void DetachSwapChain() => VideoHost.Detach();

    /// <summary>Closes from <see cref="MainWindow"/> without raising UserClosed.</summary>
    public void CloseFromMain()
    {
        _closedFromMain = true;
        Close();
    }

    public void SetTitle(string? title)
    {
        string resolved = string.IsNullOrWhiteSpace(title) ? Loc.T("pip.title") : title;
        AppWindow.Title = resolved;
        Title = resolved;
    }

    public void SetPlaying(bool playing)
    {
        PlayPauseButton.Content = playing ? "\uE769" : "\uE768";
    }

    public void ShowControls()
    {
        // Controls belong to an active, hovered mini-player only. A hovered
        // window that lost focus must stay chrome-free.
        if (!_isActive || !_pointerInside)
        {
            return;
        }

        _hideTimer.Stop();
        ControlStrip.Opacity = 1;
        ControlStrip.IsHitTestVisible = true;
        DragRegion.Opacity = 1;
        HintBadge.Opacity = 1;
    }

    private void HideControls(bool force = false)
    {
        if (_pointerOverControls && !force)
        {
            return;
        }

        _hideTimer.Stop();
        ControlStrip.IsHitTestVisible = false;
        if (!AnimationsEnabled())
        {
            ControlStrip.Opacity = 0;
            DragRegion.Opacity = 0;
            HintBadge.Opacity = 0;
            return;
        }

        var storyboard = new Storyboard();
        foreach (UIElement element in new UIElement[] { ControlStrip, DragRegion, HintBadge })
        {
            var animation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }
        storyboard.Begin();
    }

    private static bool AnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private void UpdateSurface()
    {
        double dpi = Root.XamlRoot?.RasterizationScale ?? 1.0;
        double width = Root.ActualWidth;
        double height = Root.ActualHeight;
        if (width < 16 || height < 16)
        {
            return;
        }

        int pixelWidth = Math.Max(1, (int)Math.Round(width * dpi));
        int pixelHeight = Math.Max(1, (int)Math.Round(height * dpi));

        // Mini-player is intentionally a low-resolution compositor surface.
        // Rendering a 4K/1080p frame into a ~500 px window wastes GPU time that
        // a foreground game needs; cap the source buffer and let the compositor
        // scale it. This is the closest safe equivalent of CanvasSwapChain's
        // SourceSize without replacing mpv's render path.
        const int maxRenderWidth = 640;
        const int maxRenderHeight = 360;
        int renderWidth = Math.Min(pixelWidth, maxRenderWidth);
        int renderHeight = Math.Min(pixelHeight, maxRenderHeight);
        if (pixelWidth > 0)
        {
            renderHeight = Math.Max(1, (int)Math.Round(renderWidth * (double)pixelHeight / pixelWidth));
        }

        VideoHost.SetBufferSize(renderWidth, renderHeight, dpi);
        VideoHost.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height),
        };

        if (Math.Abs(width - _lastWidth) < 0.5 && Math.Abs(height - _lastHeight) < 0.5)
        {
            return;
        }

        _lastWidth = width;
        _lastHeight = height;
        BufferSizeChanged?.Invoke(renderWidth, renderHeight, dpi);
    }

    private void SnapToEdges()
    {
        if (!App.Settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        WindowRect snapped = SnapMovingRect(new WindowRect(
            position.X, position.Y, position.X + size.Width, position.Y + size.Height));
        if (snapped.Left != position.X || snapped.Top != position.Y)
        {
            AppWindow.Move(new PointInt32(snapped.Left, snapped.Top));
        }
    }

    private WindowRect SnapMovingRect(WindowRect rect)
    {
        if (!App.Settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return rect;
        }

        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 work = area.WorkArea;
        int threshold = (int)Math.Round(10 * Math.Max(1.0, _rasterizationScale));
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int x = rect.Left;
        int y = rect.Top;
        if (Math.Abs(x - work.X) <= threshold)
        {
            x = work.X;
        }
        else if (Math.Abs(rect.Right - (work.X + work.Width)) <= threshold)
        {
            x = work.X + work.Width - width;
        }

        if (Math.Abs(y - work.Y) <= threshold)
        {
            y = work.Y;
        }
        else if (Math.Abs(rect.Bottom - (work.Y + work.Height)) <= threshold)
        {
            y = work.Y + work.Height - height;
        }

        return new WindowRect(x, y, x + width, y + height);
    }

    private static (int X, int Y) NudgeAwayFromMain(
        int x,
        int y,
        int width,
        int height,
        RectInt32 workArea,
        RectInt32 mainFrame)
    {
        bool overlaps =
            x < mainFrame.X + mainFrame.Width &&
            x + width > mainFrame.X &&
            y < mainFrame.Y + mainFrame.Height &&
            y + height > mainFrame.Y;
        if (!overlaps)
        {
            return (x, y);
        }

        int topY = workArea.Y + 16;
        int rightX = workArea.X + workArea.Width - width - 16;
        return (rightX, topY);
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSurface();

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) =>
        UpdateSurface();

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (!_isActive)
        {
            _pointerOverControls = false;
            HideControls(force: true);
        }
        else if (_pointerInside)
        {
            ShowControls();
        }
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        ShowControls();
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e) =>
        ShowControls();

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        _pointerOverControls = false;
        HideControls(force: true);
    }

    private void OnRootTapped(object sender, TappedRoutedEventArgs e) => ShowControls();

    private void OnControlStripPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverControls = true;
        if (!_isActive)
        {
            return;
        }

        _hideTimer.Stop();
        ControlStrip.Opacity = 1;
        ControlStrip.IsHitTestVisible = true;
    }

    private void OnControlStripPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverControls = false;
        _hideTimer.Start();
    }

    private void OnRootDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEscapeAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSpaceAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
        ShowControls();
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _isPinned;
        }

        PinButton.Content = _isPinned ? "\uE718" : "\uE77A";
        ShowControls();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        IsClosed = true;
        _hideTimer.Stop();
        VideoHost.Detach();
        if (!_closedFromMain)
        {
            UserClosed?.Invoke(this, EventArgs.Empty);
        }
    }
}
