using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.App.Interop;
using Wiina.App.Localization;

namespace Wiina.App.Controls;

public sealed class SidebarTabRequestedEventArgs : EventArgs
{
    public SidebarTabRequestedEventArgs(SidebarType type) => Type = type;

    public SidebarType Type { get; }
}

public sealed class SidebarWidthRequestedEventArgs : EventArgs
{
    public SidebarWidthRequestedEventArgs(double width) => Width = width;

    public double Width { get; }
}

/// <summary>
/// One sidebar slot: tab strip, close button, content host and the 4 DIP
/// inner-edge width grip. The container itself is animation-agnostic; the
/// shell tells it how to enter/leave so system animation settings are honored.
/// </summary>
public sealed partial class SidebarHost : UserControl
{
    private readonly DispatcherQueueTimer _animTimer;
    private SidebarType? _currentType;
    private bool _dragging;
    private double _lastPointerX;
    private bool _hiding;
    private Action? _hideCompleted;
    private double _animFromOffset;
    private double _animToOffset;
    private double _animFromOpacity;
    private double _animToOpacity;
    private long _animStart;

    public SidebarHost()
    {
        InitializeComponent();
        _animTimer = DispatcherQueue.CreateTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(15);
        _animTimer.IsRepeating = true;
        _animTimer.Tick += (_, _) => OnAnimationTick();
        SetSide(SidebarSide.Trailing);

        // Resize from the inner 6 DIP, handled on the root so a 4 DIP child
        // cannot lose hit-testing to the panel background.
        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnGripPointerPressed),
            handledEventsToo: true);
        RootGrid.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnGripPointerMoved),
            handledEventsToo: true);
        RootGrid.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnGripPointerReleased),
            handledEventsToo: true);
        RootGrid.PointerCaptureLost += OnGripPointerCaptureLost;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private ChromeBlurLayer? _blur;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_blur is not null || XamlRoot is null)
        {
            return;
        }

        // Real blurred frame behind the sidebar; the acrylic panel only stays
        // until the first blurred frame is available.
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
        PanelBorder.Background = null;
        SidebarBlurTintLayer.Visibility = Visibility.Visible;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_blur is not null)
        {
            _blur.FirstFrame -= OnFirstBlurFrame;
        }

        _blur = null;
    }

    /// <summary>Optional diagnostic sink wired by the shell to its trace file.</summary>
    public static Action<string>? Log;

    public event EventHandler? CloseClicked;

    public event EventHandler<SidebarTabRequestedEventArgs>? TabRequested;

    /// <summary>Raised continuously while the grip is dragged (unclamped DIP).</summary>
    public event EventHandler<SidebarWidthRequestedEventArgs>? WidthRequested;

    /// <summary>Raised once on pointer release so the shell can persist.</summary>
    public event EventHandler? WidthChangeCompleted;

    public SidebarSide Side { get; private set; } = SidebarSide.Trailing;

    public SidebarType? CurrentType => _currentType;

    public double SlideOffset => SlideTransform.TranslateX;

    public QuickSettingsView QuickSettings => SettingsContent;

    public PlaylistView Playlist => PlaylistContent;

    public ChaptersView Chapters => ChaptersContent;

    public PluginsView Plugins => PluginsContent;

    public void SetSide(SidebarSide side)
    {
        Side = side;
        bool trailing = side == SidebarSide.Trailing;

        PanelBorder.BorderThickness = trailing
            ? new Thickness(1, 0, 0, 0)
            : new Thickness(0, 0, 1, 0);
        ResizeGrip.HorizontalAlignment = trailing
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CloseButton.HorizontalAlignment = trailing
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CloseButton.Content = trailing ? "\uE76B" : "\uE76C";
        TypeTabs.HorizontalAlignment = trailing
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        ToolTipService.SetToolTip(CloseButton, Loc.T("sidebar.close"));
    }

    public void SetWidth(double width) => Width = Math.Max(0, width);

    public void SetType(SidebarType? type)
    {
        _currentType = type;
        SettingsContent.Visibility = type == SidebarType.Settings
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistContent.Visibility = type == SidebarType.Playlist
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChaptersContent.Visibility = type == SidebarType.Chapters
            ? Visibility.Visible
            : Visibility.Collapsed;
        PluginsContent.Visibility = type == SidebarType.Plugins
            ? Visibility.Visible
            : Visibility.Collapsed;

        SettingsTab.IsChecked = type == SidebarType.Settings;
        PlaylistTab.IsChecked = type == SidebarType.Playlist;
        ChaptersTab.IsChecked = type == SidebarType.Chapters;
        PluginsTab.IsChecked = type == SidebarType.Plugins;
    }

    public void SetPage(QuickSettingsPage page) => SettingsContent.ShowPage(page);

    public void ShowInstant()
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        SlideTransform.TranslateX = 0;
        Opacity = 1;
        Visibility = Visibility.Visible;
    }

    public void PlayShow(SidebarAnimationMode mode)
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        Visibility = Visibility.Visible;

        if (mode == SidebarAnimationMode.None)
        {
            SlideTransform.TranslateX = 0;
            Opacity = 1;
            return;
        }

        _animFromOffset = mode == SidebarAnimationMode.Fade
            ? 0
            : Side == SidebarSide.Leading ? -Width : Width;
        _animToOffset = 0;
        _animFromOpacity = mode == SidebarAnimationMode.Fade ? 0 : 1;
        _animToOpacity = 1;
        SlideTransform.TranslateX = _animFromOffset;
        Opacity = _animFromOpacity;
        StartAnimation();
    }

    public void PlayHide(SidebarAnimationMode mode, Action? completed)
    {
        _animTimer.Stop();

        if (mode == SidebarAnimationMode.None)
        {
            CollapseInstant();
            completed?.Invoke();
            return;
        }

        _hiding = true;
        _hideCompleted = completed;
        _animFromOffset = mode == SidebarAnimationMode.Fade
            ? 0
            : SlideTransform.TranslateX;
        _animToOffset = Side == SidebarSide.Leading ? -Width : Width;
        _animFromOpacity = Opacity;
        _animToOpacity = 0;
        StartAnimation();
    }

    public void CollapseInstant()
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        Visibility = Visibility.Collapsed;
        SlideTransform.TranslateX = 0;
        Opacity = 1;
    }

    private void StartAnimation()
    {
        _animStart = Stopwatch.GetTimestamp();
        _animTimer.Start();
    }

    private void OnAnimationTick()
    {
        double elapsed = (Stopwatch.GetTimestamp() - _animStart) /
            (double)Stopwatch.Frequency;
        double progress = Math.Clamp(elapsed / SidebarState.AnimationSeconds, 0, 1);
        double eased = EaseInOut(progress);

        SlideTransform.TranslateX =
            _animFromOffset + ((_animToOffset - _animFromOffset) * eased);
        Opacity = _animFromOpacity + ((_animToOpacity - _animFromOpacity) * eased);

        if (progress < 1)
        {
            return;
        }

        _animTimer.Stop();
        if (_hiding)
        {
            Action? completed = _hideCompleted;
            CollapseInstant();
            completed?.Invoke();
        }
        else
        {
            SlideTransform.TranslateX = 0;
            Opacity = 1;
        }
    }

    private static double EaseInOut(double t) =>
        t < 0.5
            ? 4 * t * t * t
            : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    private void OnTypeTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } &&
            Enum.TryParse(tag, out SidebarType type))
        {
            TabRequested?.Invoke(this, new SidebarTabRequestedEventArgs(type));
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseClicked?.Invoke(this, EventArgs.Empty);

    private void OnGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double x = point.Position.X;
        bool nearInnerEdge = Side == SidebarSide.Trailing
            ? x <= 6
            : x >= RootGrid.ActualWidth - 6;
        if (!nearInnerEdge)
        {
            return;
        }

        Log?.Invoke(
            $"resize press side={Side} x={x:0.0} actual={RootGrid.ActualWidth:0}");

        _dragging = true;
        _lastPointerX = WindowInterop.TryGetCursorPosition(out int cursorX, out _)
            ? cursorX
            : 0;
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        if (!WindowInterop.TryGetCursorPosition(out int x, out _))
        {
            return;
        }

        double delta = x - _lastPointerX;
        _lastPointerX = x;
        double scale = XamlRoot?.RasterizationScale ?? 1.0;
        if (scale > 0.01)
        {
            delta /= scale;
        }

        double proposed = Width + (Side == SidebarSide.Trailing ? -delta : delta);
        Log?.Invoke($"move delta={delta:0.0} proposed={proposed:0}");
        WidthRequested?.Invoke(this, new SidebarWidthRequestedEventArgs(proposed));
        e.Handled = true;
    }

    private void OnGripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        try
        {
            RootGrid.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // Pointer may already be gone.
        }

        Log?.Invoke($"release width={Width:0}");
        WidthChangeCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnGripPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        WidthChangeCompleted?.Invoke(this, EventArgs.Empty);
    }
}
