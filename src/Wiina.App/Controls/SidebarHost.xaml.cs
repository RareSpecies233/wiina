using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private ChromeBlurLayer? _blur;
    private bool _reapplyQueued;
    // True from the first frame of a slide until it settles. While set, the
    // blur layer is not repainted: Apply samples the panel through
    // TransformToVisual, so a frame painted at TranslateX = ±Width bakes the
    // slide into the brush transform and leaves half the panel on the opaque
    // solid brush (1.3.10 regression). The layer is replayed once at rest.
    private bool _animating;

    public SidebarHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => ApplyBlurVisuals();
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

        // A collapsed panel has no layout size, so the blur layer never gets a
        // frame while hidden. The first size after a show (and any later DPI
        // change or width drag) is the moment to replay the cached frame.
        RootGrid.SizeChanged += OnPanelSizeChanged;
        PanelBorder.SizeChanged += OnPanelSizeChanged;
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

        // Real blurred frame behind the sidebar; the opaque panel paint stays
        // until the first blurred frame is available, and in Solid mode.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 0, out ChromeBlurLayer layer))
        {
            return;
        }

        _blur = layer;
        _blur.FirstFrame += OnFirstBlurFrame;
        // A layer attached while a slide is already running inherits the gate;
        // otherwise a capture could still paint at the transient offset.
        _blur.IsTransformSuppressed = _animating;
        _blur.GeometryOffsetX = _animating ? SlideTransform.TranslateX : 0;
        _blur.RaiseFirstFrameIfReady();
    }

    private void OnBlurModeChanged(object? sender, EventArgs e)
    {
        EnsureAttached();
        ApplyBlurVisuals();
        // Leaving Solid while the panel is already on screen attaches a fresh,
        // empty layer without a size change; replay immediately so the panel
        // does not stay on the solid brush.
        RequestBlurReapply();
    }

    private void OnFirstBlurFrame(object? sender, EventArgs e) => ApplyBlurVisuals();

    private void ApplyBlurVisuals()
    {
        bool live = ChromeBlur.Current.Mode != ChromeBlurMode.Solid;
        if (_blur is not null)
        {
            _blur.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        }

        // The layer and tint live inside PanelBorder, above its opaque
        // ChromeSolidBrush, so the Background is never cleared and the XAML
        // ThemeResource keeps resolving on theme/accent changes.
        bool blurred = live && _blur is { HasFrame: true };
        SidebarBlurTintLayer.Visibility = blurred ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The panel just received a layout size (first show, DPI change, width
    /// drag). Repaint the cached blurred frame once layout has completed.
    /// </summary>
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e) =>
        RequestBlurReapply();

    /// <summary>
    /// Replays the last published blurred frame into this panel's layer on the
    /// next dispatcher turn, when <c>ActualWidth/ActualHeight</c> are final.
    /// Deferring also coalesces the SizeChanged burst of a width drag into one
    /// repaint per pass. Solid keeps its opaque brush untouched. A slide in
    /// flight is skipped entirely: the settle handler calls this once
    /// <c>TranslateX</c> is back to 0.
    /// </summary>
    private void RequestBlurReapply()
    {
        if (_blur is null || _reapplyQueued || _animating ||
            ChromeBlur.Current.Mode == ChromeBlurMode.Solid)
        {
            return;
        }

        _reapplyQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _reapplyQueued = false;
            if (_blur is null || _animating ||
                ChromeBlur.Current.Mode == ChromeBlurMode.Solid)
            {
                return;
            }

            ChromeBlur.Current.Reapply(_blur);
        }))
        {
            _reapplyQueued = false;
        }
    }

    /// <summary>
    /// Gates the blur layer for the duration of a slide and keeps its geometry
    /// compensation equal to the current offset, so a frame the capture worker
    /// publishes mid-slide maps to the settled position instead of freezing the
    /// panel half a width off.
    /// </summary>
    private void SetAnimating(bool animating)
    {
        _animating = animating;
        if (_blur is null)
        {
            return;
        }

        _blur.IsTransformSuppressed = animating;
        _blur.GeometryOffsetX = animating ? SlideTransform.TranslateX : 0;
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

    /// <summary>Optional diagnostic sink wired by the shell to its trace file.</summary>
    public static Action<string>? Log;

    public event EventHandler? CloseClicked;

    public event EventHandler<SidebarTabRequestedEventArgs>? TabRequested;

    /// <summary>Raised when the quick-settings page locator changes page.</summary>
    public event EventHandler<QuickSettingsPage>? PageRequested;

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
        CloseButton.Content = "\uE711";
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

        // There is only one navigator: all seven destinations are always
        // visible. Playlist/chapters/plugins are pages of that same row, not a
        // second four-item category layer.
        CategoryTabs.Visibility = Visibility.Collapsed;
        PageTabs.Visibility = Visibility.Visible;
        SettingsTab.IsChecked = false;
        PlaylistTab.IsChecked = false;
        ChaptersTab.IsChecked = false;
        PluginsTab.IsChecked = false;

        LayoutTab.IsChecked = false;
        VideoTab.IsChecked = false;
        AudioTab.IsChecked = false;
        SubtitlesTab.IsChecked = false;
        PlaylistPageTab.IsChecked = false;
        ChaptersPageTab.IsChecked = false;
        PluginsPageTab.IsChecked = false;

        // Destinations reuse the same host and WinUI restores a scroll offset
        // after a visibility cycle, so rewind every list before showing the
        // next one.
        ResetScrollOffsets(this);
        UpdatePageChecks(SettingsContent.CurrentPage, type ?? SidebarType.Settings);
    }

    /// <summary>
    /// Sets the vertical offset of every <see cref="ScrollViewer"/> in the
    /// subtree back to zero without animating.
    /// </summary>
    private static void ResetScrollOffsets(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer viewer)
            {
                viewer.ChangeView(null, 0, null, disableAnimation: true);
            }

            ResetScrollOffsets(child);
        }
    }

    public void SetPage(QuickSettingsPage page)
    {
        SettingsContent.ShowPage(page);

        UpdatePageChecks(page, _currentType ?? SidebarType.Settings);
    }

    private void UpdatePageChecks(QuickSettingsPage page, SidebarType type)
    {
        bool settings = type == SidebarType.Settings;
        LayoutTab.IsChecked = settings && page == QuickSettingsPage.Layout;
        VideoTab.IsChecked = settings && page == QuickSettingsPage.Video;
        AudioTab.IsChecked = settings && page == QuickSettingsPage.Audio;
        SubtitlesTab.IsChecked = settings && page == QuickSettingsPage.Subtitles;

        PlaylistPageTab.IsChecked = type == SidebarType.Playlist;
        ChaptersPageTab.IsChecked = type == SidebarType.Chapters;
        PluginsPageTab.IsChecked = type == SidebarType.Plugins;

        UpdatePageLabels();
        RefreshPageUnderlines();
    }

    /// <summary>
    /// WinUI's ToggleButton visual states occasionally keep the checked
    /// underline after IsChecked flips to false (and therefore leave two
    /// underlines visible). Drive the rail opacity explicitly so exactly one
    /// item can ever show a rail.
    /// </summary>
    private void RefreshPageUnderlines()
    {
        ToggleButton[] tabs =
        {
            LayoutTab, VideoTab, AudioTab, SubtitlesTab,
            PlaylistPageTab, ChaptersPageTab, PluginsPageTab,
        };

        foreach (ToggleButton tab in tabs)
        {
            tab.ApplyTemplate();
            SetUnderlineOpacity(tab, tab.IsChecked == true ? 1 : 0);
        }
    }

    private static void SetUnderlineOpacity(DependencyObject root, double opacity)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Border border &&
                string.Equals(border.Name, "NavUnderline", StringComparison.Ordinal))
            {
                border.Opacity = opacity;
                return;
            }

            SetUnderlineOpacity(child, opacity);
        }
    }

    /// <summary>
    /// Quick-settings locator labels only the current target; the rest stay
    /// icon-only so all seven entries fit without shrinking their names.
    /// </summary>
    private void UpdatePageLabels()
    {
        bool settings = _currentType == SidebarType.Settings;
        QuickSettingsPage page = SettingsContent.CurrentPage;

        LayoutLabel.Visibility = settings && page == QuickSettingsPage.Layout
            ? Visibility.Visible
            : Visibility.Collapsed;
        VideoLabel.Visibility = settings && page == QuickSettingsPage.Video
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioLabel.Visibility = settings && page == QuickSettingsPage.Audio
            ? Visibility.Visible
            : Visibility.Collapsed;
        SubtitlesLabel.Visibility = settings && page == QuickSettingsPage.Subtitles
            ? Visibility.Visible
            : Visibility.Collapsed;

        PagePlaylistLabel.Visibility = _currentType == SidebarType.Playlist
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageChaptersLabel.Visibility = _currentType == SidebarType.Chapters
            ? Visibility.Visible
            : Visibility.Collapsed;
        PagePluginsLabel.Visibility = _currentType == SidebarType.Plugins
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// True while the panel is sliding out. The host stays Visible at its full
    /// width until the animation ends, so the shell must not treat it as
    /// covering the video surface during that window.
    /// </summary>
    public bool IsHiding => _hiding;

    public void ShowInstant()
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        SlideTransform.TranslateX = 0;
        Opacity = 1;
        Visibility = Visibility.Visible;
        SetAnimating(false);
        RequestBlurReapply();
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
            SetAnimating(false);
            RequestBlurReapply();
            return;
        }

        // Do not repaint before or during the slide. The layer may still be
        // empty (no frame was published while the panel was collapsed), but
        // sampling it at ±Width would bake the slide into the brush transform
        // and leave half the panel on the opaque solid brush. OnAnimationTick
        // clears the gate once TranslateX is 0 and replays the cached frame.
        _animFromOffset = mode == SidebarAnimationMode.Fade
            ? 0
            : Side == SidebarSide.Leading ? -Width : Width;
        _animToOffset = 0;
        _animFromOpacity = mode == SidebarAnimationMode.Fade ? 0 : 1;
        _animToOpacity = 1;
        SlideTransform.TranslateX = _animFromOffset;
        Opacity = _animFromOpacity;
        SetAnimating(true);
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
        // Same gate as PlayShow: the layer must not be repainted while the
        // panel is sliding out either (it would freeze the exit offset).
        SetAnimating(true);
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
        SetAnimating(false);
        LayoutTab.IsChecked = false;
        VideoTab.IsChecked = false;
        AudioTab.IsChecked = false;
        SubtitlesTab.IsChecked = false;
        PlaylistPageTab.IsChecked = false;
        ChaptersPageTab.IsChecked = false;
        PluginsPageTab.IsChecked = false;
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
        if (_blur is not null)
        {
            // Keep the geometry compensation equal to the live offset so a
            // frame the capture worker publishes mid-slide maps to the settled
            // position (it cannot shift the image by half a panel).
            _blur.GeometryOffsetX = SlideTransform.TranslateX;
        }

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
            // The panel is at rest: lift the gate and replay the cached frame
            // exactly once, now that the sampled geometry is the settled one.
            SetAnimating(false);
            RequestBlurReapply();
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

    private void OnPageTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse(tag, out QuickSettingsPage page))
        {
            // The four quick-settings pages are hosted by the settings slot.
            // While playlist/chapters/plugins is on screen, swapping only the
            // hidden page leaves the rail on the other surface, so switch the
            // slot first and let the shell re-apply the type before the page
            // below is shown. Toggle state is rebuilt by SetType/SetPage.
            if (_currentType != SidebarType.Settings)
            {
                TabRequested?.Invoke(this, new SidebarTabRequestedEventArgs(SidebarType.Settings));
            }

            SetPage(page);
            PageRequested?.Invoke(this, page);
            return;
        }

        if (Enum.TryParse(tag, out SidebarType type))
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
