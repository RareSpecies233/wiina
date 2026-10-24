using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;
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
    private int _lastPixelWidth;
    private int _lastPixelHeight;

    /// <summary>Shared edge magnet (issue 4); PiP is its own drag surface.</summary>
    private readonly EdgeSnapSession _snapSession = new();

    private bool? _snapFrameApplied;

    /// <summary>
    /// Task-58: the main window that created this PiP surface. Captured at
    /// construction because the owner is minimized while PiP is the visible
    /// surface; forwarded keys must still reach that exact window.
    /// </summary>
    private readonly MainWindow? _shortcutHost;

    public PipWindow(
        RectInt32 workArea,
        RectInt32 mainFrame,
        int widthDip,
        int heightDip,
        double rasterizationScale)
    {
        InitializeComponent();

        // Task-58: remember the owner now (it is the active window while the PiP
        // is being created) so forwarded keys reach the right window even when
        // several main windows are open.
        _shortcutHost = App.ActiveWindow;

        Root.RequestedTheme = App.ResolveTheme();

        _rasterizationScale = rasterizationScale > 0.1 ? rasterizationScale : 1.0;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragRegion);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        }
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
        WindowInterop.SetImmersiveDarkMode(this, dark: App.IsDarkTheme);
        WindowInterop.Attach(
            this,
            (int)PipGeometry.MinWidth,
            (int)PipGeometry.MinHeight,
            () => true,
            () => PipGeometry.AspectRatio,
            appCommand: null,
            windowResizeStarted: null,
            windowMoveEnded: SnapToEdges,
            windowMoving: SnapMovingRect,
            windowMoveStarted: _snapSession.Reset);

        Root.SizeChanged += OnRootSizeChanged;
        // Task-57: the transport buttons must never take keyboard focus, and the
        // play/pause + restore keys must work no matter which element has focus.
        Root.AddHandler(
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(OnRootPreviewKeyDown),
            handledEventsToo: true);
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

    /// <summary>
    /// Re-resolves the app theme for this HWND while PiP stays open. The player
    /// calls this from <c>MainWindow.ApplyTheme</c> after a theme switch.
    /// </summary>
    public void ApplyTheme()
    {
        Root.RequestedTheme = App.ResolveTheme();
        WindowInterop.SetImmersiveDarkMode(this, dark: App.IsDarkTheme);
    }

    /// <summary>User closed the window (Alt+F4 / taskbar / close button).</summary>
    public event EventHandler? UserClosed;

    public event EventHandler? PlayPauseRequested;

    /// <summary>
    /// Physical layout size of the PiP video surface (the whole window). The mpv
    /// composition buffer uses the same full size so the mini window is exactly
    /// as sharp as the main window (issue 7).
    /// </summary>
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
        double dpi = Root.XamlRoot?.RasterizationScale ?? _rasterizationScale;
        double width = Root.ActualWidth;
        double height = Root.ActualHeight;
        if (width < 16 || height < 16)
        {
            return;
        }

        int pixelWidth = Math.Max(1, (int)Math.Round(width * dpi));
        int pixelHeight = Math.Max(1, (int)Math.Round(height * dpi));

        // Layout and render size are both the full window pixel size: no capped
        // swapchain and no compositor upscale (issue 7). The XAML panel must also
        // span the whole window or part of it stays uncovered (issue 3).
        VideoHost.SetLayoutSize(pixelWidth, pixelHeight, dpi);
        // Clip in layout space: the panel is laid out at physical pixels and
        // scaled back by 1/dpi, so a DIP-sized clip would cut a strip off the
        // right/bottom edge on high-DPI displays.
        VideoHost.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, pixelWidth, pixelHeight),
        };

        if (pixelWidth == _lastPixelWidth && pixelHeight == _lastPixelHeight)
        {
            return;
        }

        _lastPixelWidth = pixelWidth;
        _lastPixelHeight = pixelHeight;
        BufferSizeChanged?.Invoke(pixelWidth, pixelHeight, dpi);
    }

    private void SnapToEdges()
    {
        if (!App.Settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _snapSession.Reset();
            ApplySnapFrame(false);
            return;
        }

        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        EdgeSnapResult result = _snapSession.Apply(
            new WindowRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height),
            WorkAreaUnderPointer(),
            DisplayScale,
            fromApplied: true,
            margins: SnapMargins());
        if (result.Rect.Left != position.X || result.Rect.Top != position.Y)
        {
            AppWindow.Move(new PointInt32(result.Rect.Left, result.Rect.Top));
        }

        ApplySnapFrame(result.IsSnapped);
    }

    private WindowRect SnapMovingRect(WindowRect rect)
    {
        if (!App.Settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _snapSession.Reset();
            ApplySnapFrame(false);
            return rect;
        }

        EdgeSnapResult result = _snapSession.Apply(
            rect,
            WorkAreaUnderPointer(),
            DisplayScale,
            fromApplied: true,
            margins: SnapMargins());
        ApplySnapFrame(result.IsSnapped);
        return result.Rect;
    }

    /// <summary>
    /// Invisible resize-border margins of this window, so the PiP drag snaps its
    /// visible frame flush (issue 2).
    /// </summary>
    private FrameMargins SnapMargins() =>
        WindowInterop.TryGetFrameMargins(this, out FrameMargins margins) ? margins : FrameMargins.None;

    private double DisplayScale => Root.XamlRoot?.RasterizationScale ?? _rasterizationScale;

    private WorkArea WorkAreaUnderPointer()
    {
        DisplayArea area = WindowInterop.TryGetCursorPosition(out int x, out int y)
            ? DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 work = area.WorkArea;
        return new WorkArea(work.X, work.Y, work.Width, work.Height);
    }

    private void ApplySnapFrame(bool snapped)
    {
        if (_snapFrameApplied == snapped)
        {
            return;
        }

        _snapFrameApplied = snapped;
        WindowInterop.SetSnappedFrame(this, snapped);
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

    /// <summary>
    /// Task-57/58: runs in the preview (tunneling) phase before any focused
    /// control can consume the key. Tab never reaches the transport buttons,
    /// Space pauses, Escape returns to the main window, and every other key is
    /// forwarded to the owning main window so the PiP surface uses the same key
    /// map (seeking, volume, mute, screenshot, speed, fullscreen...). Unbound
    /// keys stay unhandled so IME and system shortcuts keep working.
    /// </summary>
    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Tab:
                e.Handled = true;
                return;
            case VirtualKey.Space:
                e.Handled = true;
                PlayPauseRequested?.Invoke(this, EventArgs.Empty);
                ShowControls();
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                return;
        }

        MainWindow? host = _shortcutHost ?? App.ActiveWindow;
        if (host is not null && host.TryForwardShortcutKey(e.Key))
        {
            e.Handled = true;
        }
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
