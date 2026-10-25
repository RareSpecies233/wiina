using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Wiina.App.Controls;
using Wiina.App.Interop;
using Wiina.App.ViewModels;
using Wiina.Core.Models;
using Wiina.App.Localization;
using Wiina.Core.Osd;
using Wiina.Core.Playback;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Core.Text;
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

    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly DispatcherQueueTimer _pointerWatchTimer;
    private readonly OscVisibilityController _oscState = new();

    /// <summary>Monotonic clock for the OSC idle rules (unaffected by system time changes).</summary>
    private readonly Stopwatch _idleClock = Stopwatch.StartNew();
    private readonly OscViewModel _oscVm = new();
    private readonly SidebarState _sidebar = new();
    private AppSettings _settings;
    private MpvPlayer? _player;
    private DispatcherQueueTimer? _statsTimer;
    private DispatcherQueueTimer? _cursorHideTimer;

    private bool _protectedCursorHidden;
    private int _cursorDiagTick;
    private nint _swapChain;
    private bool _started;
    private bool _closed;
    private bool _compact;

    // Close path: an explicit state machine with per-step tracing plus a
    // watchdog, so a blocked teardown can never leave a hung window behind
    // (AppHangB1, 26 s in 1.2.6: the window stayed "not responding" because
    // OnClosed never returned).
    private bool _closing;
    private bool _closeCompleted;
    private CancellationTokenSource? _closeWatchdog;
    private readonly Stopwatch _closeClock = new();

    /// <summary>Upper bound for teardown before the process is terminated.</summary>
    private static readonly TimeSpan CloseWatchdogTimeout = TimeSpan.FromMilliseconds(4000);

    // Device loss (DXGI_ERROR_DEVICE_REMOVED and friends): mpv's event thread
    // reports the loss in its log, the swapchain bind fails with the same
    // HRESULT, and playback keeps rendering into a dead device until the VO is
    // rebuilt. Recovery is bounded so broken hardware cannot loop forever.
    private readonly DeviceLossRecoveryBudget _deviceRecovery = new();
    private bool _deviceRecoveryPending;
    private bool _deviceRecoveryFailed;
    private DispatcherQueueTimer? _deviceRecoveryTimer;
    private bool _titleBarVisible = true;

    /// <summary>
    /// Fullscreen fades the OSC without the titlebar, so the titlebar opacity is
    /// pinned to zero while it owns the screen; leaving fullscreen restores the
    /// opacity that belongs to the current chrome state.
    /// </summary>
    private bool _titleBarPinnedByFullscreen;
    private bool _sidebarResizing;
    private bool _updatingSidebar;
    private bool _oscDragging;
    private Point? _pointerPressPosition;
    private bool _seeking;
    private double _seekOsdTarget;
    private bool _wasPlayingBeforeSeek;
    private bool _chromeShown = true;
    private bool _pointerOverChrome;
    private bool _windowActive = true;
    private int _openFlyoutCount;

    private MenuFlyout? _applicationMenu;
    private Storyboard? _chromeStoryboard;
    private bool? _animationsEnabled;
    private DispatcherQueueTimer? _settingsSaveTimer;
    private Point _oscDragStart;
    private Point _oscOrigin;
    private IReadOnlyList<ChapterInfo> _chapters = Array.Empty<ChapterInfo>();
    private double _videoAspect = 16.0 / 9.0;
    private int _videoWidth;
    private int _videoHeight;
    private bool _userResizedWindow;
    private bool _initialVideoSizingDone;
    private string? _currentAudioDevice;
    private bool _filterSoftwareDecode;
    private double? _pendingFilterResumeTime;
    private bool _pendingFilterResumePaused;

    private bool AnimationsEnabled => _settings.AnimationsEnabled && SystemAnimationsEnabled;

    private bool SystemAnimationsEnabled
    {
        get
        {
            if (_animationsEnabled is null)
            {
                try
                {
                    _animationsEnabled =
                        new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
                }
                catch
                {
                    _animationsEnabled = true;
                }
            }

            return _animationsEnabled.Value;
        }
    }

    private static readonly string TracePath =
        Path.Combine(Path.GetTempPath(), "wiina-trace.log");

    private static readonly object TraceLock = new();

    private static void Trace(string message)
    {
        try
        {
            // mpv's event thread and the blur capture thread trace too; without
            // the lock a concurrent append throws and the line is lost exactly
            // when the close path needs it most.
            lock (TraceLock)
            {
                File.AppendAllText(
                    TracePath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Tracing must never break the app.
        }
    }

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupPaths)
    {
        _startupPaths = startupPaths;
        InitializeComponent();
        _settings = App.Settings;

        // Settings are loaded before the window exists, so no SettingsChanged
        // event has fired yet. Build the live key map from the saved bindings
        // now; otherwise the compiled defaults stay active until the next save.
        RebuildKeyMap();

        // Apply the saved capture mode before the chrome first becomes
        // visible: "Solid" must never arm the Win2D capture thread at all.
        ChromeBlur.SetMode(_settings.ChromeBlurMode);

        // Follow system light/dark changes even when the OSD was constructed
        // while the app was still on its default (dark) Application theme.
        RootGrid.ActualThemeChanged += (_, _) => Osd.RequestedTheme = RootGrid.ActualTheme;

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

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTick;

        // WinUI does not raise PointerExited when the pointer leaves the window
        // during a fast in-and-out gesture (the root handler can also miss the
        // exit while a child owns the capture). A cheap presence poll covers the
        // "move in, move out quickly" case: the chrome must vanish immediately.
        _pointerWatchTimer = DispatcherQueue.CreateTimer();
        _pointerWatchTimer.Interval = TimeSpan.FromMilliseconds(200);
        _pointerWatchTimer.IsRepeating = true;
        _pointerWatchTimer.Tick += OnPointerWatchTick;

        _statsTimer = DispatcherQueue.CreateTimer();
        _statsTimer.Interval = TimeSpan.FromSeconds(1);
        _statsTimer.IsRepeating = true;
        _statsTimer.Tick += OnStatsTick;
        _statsTimer.Start();

        // WinUI keeps restoring the arrow from WM_SETCURSOR (and can bump the
        // ShowCursor display counter), so while the chrome is hidden the blank
        // cursor is re-asserted a few times per second. It costs one SetCursor
        // call and only runs during hidden playback.
        _cursorHideTimer = DispatcherQueue.CreateTimer();
        _cursorHideTimer.Interval = TimeSpan.FromMilliseconds(120);
        _cursorHideTimer.IsRepeating = true;
        _cursorHideTimer.Tick += OnCursorHideTick;

        OscFloating.DataContext = _oscVm;
        OscBottom.DataContext = _oscVm;
        OscTop.DataContext = _oscVm;
        TitleBar.SetOscDataContext(_oscVm);
        TitleBar.MinimizeRequested += (_, _) =>
            (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
        TitleBar.MaximizeRequested += (_, _) => ToggleMaximize();
        TitleBar.CloseRequested += (_, _) => Close();
        SubscribeOsc();

        TitleBar.Title = "Wiina";
        TitleBar.PinClicked += (_, _) => TogglePin();
        TitleBar.LayoutClicked += (_, _) => ToggleLayout();
        TitleBar.OscClicked += (_, _) => CycleOsc();
        TitleBar.SidebarClicked += (_, _) => ToggleSidebar();
        TitleBar.ThemeClicked += (_, _) => CycleTheme();
        TitleBar.IconsClicked += async (_, _) => await ShowIconDialogAsync();
        TitleBar.WindowClicked += (_, _) => ShowWindowMenu();

        VideoHost.Loaded += OnVideoHostLoaded;
        VideoFrame.SizeChanged += OnVideoFrameSizeChanged;
        RootGrid.SizeChanged += OnRootSizeChanged;
        RootGrid.Loaded += OnRootLoaded;
        // The floating OSC placement only follows the sidebar edge; the panel
        // may stay open without moving the controls when it does not overlap
        // them (see ApplyOscPosition).
        SidebarLeading.SizeChanged += (_, _) => ApplyOscPosition();
        SidebarTrailing.SizeChanged += (_, _) => ApplyOscPosition();
        // Sliders capture the pointer, so their release may not bubble as a
        // normal event. Listen with handledEventsToo to re-arm auto-hide after
        // any drag that ended (inside or outside the window).
        RootGrid.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnRootPointerReleased),
            handledEventsToo: true);
        // The click-to-hide check needs the press origin: a click is only a
        // click when the pointer did not travel between press and release.
        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnRootPointerPressed),
            handledEventsToo: true);
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // An inactive window must not hide the pointer: the user may be
                // pointing at it while working in another app.
                _windowActive = false;
                _cursorHideTimer?.Stop();
                WindowInterop.SetCursorVisible(true);
                ApplyProtectedCursor(false);
                CloseApplicationMenu();

                // Losing focus keeps the chrome only in the docked layout and
                // while a sidebar is in use. Flyouts close asynchronously on
                // focus loss, so hide after the activation event has unwound.
                if (OscVisibilityController.ShouldHideOnDeactivate(
                        _settings.Layout == WindowLayoutMode.Docked,
                        HasVisibleSidebar))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_closed)
                        {
                            HideChrome(force: true);
                        }
                    });
                }

                return;
            }

            _windowActive = true;
            RootGrid.Focus(FocusState.Programmatic);
            App.ActivateWindow(this);
        };
        Closed += OnClosed;

        ApplySettings();
        InitializeStage3();
        InitializeSidebar();
        InitializeStage5();
        InitializeStage6();
    }

    private void ApplySettings()
    {
        ApplyTheme();
        ApplyLayout();
        _oscState.AutoHideEnabled = _settings.OscAutoHide;
        _oscState.AutoHideSeconds = _settings.OscAutoHideSeconds;
        _autoHideTimer.Interval = TimeSpan.FromSeconds(_oscState.AutoHideSeconds);
        ApplyStage5Settings();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        }
    }

    private void SubscribeOsc()
    {
        _oscVm.SeekRequested += OnOscSeekRequested;
        _oscVm.VolumeRequested += SetVolume;
        _oscVm.PlayPauseRequested += TogglePlayPause;
        _oscVm.MuteRequested += ToggleMute;
        _oscVm.PreviousRequested += OscPrevious;
        _oscVm.NextRequested += OscNext;
        _oscVm.FullscreenRequested += ToggleFullscreen;
        _oscVm.SettingsRequested += ShowSettingsSidebar;
        _oscVm.PlaylistRequested += ShowPlaylistSidebar;
        _oscVm.PropertyChanged += (_, _) => UpdateTimelines();

        SubscribeTimeline(OscFloating.Timeline);
        SubscribeTimeline(OscBottom.Timeline);
        SubscribeTimeline(OscTop.Timeline);
        OscFloating.Toolbar.MoreClicked += OnMoreClicked;
        OscBottom.Toolbar.MoreClicked += OnMoreClicked;
        OscTop.Toolbar.MoreClicked += OnMoreClicked;
        OscFloating.Toolbar.PipClicked += (_, _) => TogglePip();
        OscBottom.Toolbar.PipClicked += (_, _) => TogglePip();
        OscTop.Toolbar.PipClicked += (_, _) => TogglePip();

        OscFloating.DragHandle.PointerPressed += OnOscDragStart;
        OscFloating.DragHandle.PointerMoved += OnOscDragMove;
        OscFloating.DragHandle.PointerReleased += OnOscDragEnd;
        OscFloating.DragHandle.PointerCaptureLost += OnOscDragEnd;
        OscCanvas.SizeChanged += (_, _) => ApplyOscPosition();
        OscFloating.SizeChanged += (_, _) => ApplyOscPosition();
    }

    private void SendPlayerCommand(params string[] args)
    {
        try
        {
            if (_player is { IsRunning: true })
            {
                _player.CommandAsync(args);
            }
        }
        catch (MpvException ex)
        {
            Trace($"mpv command failed ({string.Join(' ', args)}): {ex.Message}");
        }
    }

    private void SubscribeTimeline(TimelineControl timeline)
    {
        timeline.SeekRequested += value => _oscVm.RequestSeek(value);
        timeline.SeekStarted += OnSeekStarted;
        timeline.SeekCompleted += OnSeekCompleted;
        timeline.PreviewRequested += (time, ratio, x) => ShowTimePreview(timeline, time, ratio, x);
        timeline.PreviewHidden += HideTimePreview;
        timeline.SizeChanged += (_, _) =>
        {
            if (ReferenceEquals(_previewTimeline, timeline))
            {
                PositionTimePreview();
                PositionThumbnail();
            }
        };
    }

    private void UpdateTimelines()
    {
        foreach (TimelineControl timeline in new[]
                 {
                     OscFloating.Timeline,
                     OscBottom.Timeline,
                     OscTop.Timeline,
                 })
        {
            timeline.Position = _oscVm.Position;
            timeline.SetDuration(_oscVm.Duration);
            timeline.SetBufferEnd(_oscVm.BufferEnd);
            timeline.SetChapters(_oscVm.Chapters);
            timeline.SetAbLoop(_oscVm.AbLoopA ?? 0, _oscVm.AbLoopB ?? 0);
        }

        UpdateSidebarProgress();
    }

    private void OnSeekStarted()
    {
        _seeking = true;
        _wasPlayingBeforeSeek = _oscVm.IsPlaying;
        _seekOsdTarget = _oscVm.Position;
        HideTimePreview();
        _autoHideTimer.Stop();
        if (_wasPlayingBeforeSeek)
        {
            SendPlayerCommand("set", "pause", "yes");
        }

        // Show the scrub position immediately: pressing the track must not wait
        // for the first slider tick (or for the release) to show feedback.
        ShowOsd(OsdMessages.Seek(_seekOsdTarget, _oscVm.Duration));
    }

    /// <summary>
    /// Timeline drags seek continuously. The OSD follows the thumb so the user
    /// sees the target time and percentage while dragging, not just a flash on
    /// release; OsdQueue coalesces the rapid series into one updating message.
    /// </summary>
    private void OnOscSeekRequested(double seconds)
    {
        if (!HasMedia())
        {
            return;
        }

        SeekAbsolute(seconds, showOsd: false);
        if (!_seeking)
        {
            return;
        }

        _seekOsdTarget = seconds;
        ShowOsd(OsdMessages.Seek(seconds, _oscVm.Duration));
    }

    private void OnSeekCompleted()
    {
        if (!_seeking)
        {
            return;
        }

        _seeking = false;
        if (_wasPlayingBeforeSeek)
        {
            SendPlayerCommand("set", "pause", "no");
        }

        _wasPlayingBeforeSeek = false;
        ShowOsd(OsdMessages.Seek(_seekOsdTarget, _oscVm.Duration));
        ResetAutoHideTimer();
    }

    /// <summary>
    /// The overlay chrome belongs to the normal window and to fullscreen, where
    /// it floats over the video and must still auto-hide. Mini and PiP layouts
    /// draw their own surfaces and ignore the player chrome.
    /// </summary>
    private bool ChromeInteractionEnabled =>
        _mode.Current is PlayerMode.Normal or PlayerMode.Fullscreen;

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!ChromeInteractionEnabled)
        {
            return;
        }

        // An unfocused player must not light up: moving the mouse over it while
        // another window is active is not an intent to control playback. The
        // click raises Activated first, and the next move shows the chrome.
        if (!_windowActive && !_chromeShown)
        {
            Trace("[pointer] enter ignored (window inactive)");
            return;
        }

        Point point = e.GetCurrentPoint(RootGrid).Position;
        _oscState.EnterWindow(point.X, point.Y, _idleClock.Elapsed);
        ShowChrome();
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ChromeInteractionEnabled)
        {
            return;
        }

        Point point = e.GetCurrentPoint(RootGrid).Position;
        _pointerOverChrome = IsOverChrome(point);

        // WinUI resets the cursor to the arrow for every mouse move; while the
        // chrome is faded out the pointer must stay hidden even for jitter.
        ReapplyHiddenCursor();

        // Activity in an inactive window must not wake the chrome or re-arm the
        // countdown. A click activates the window (Activated shows the chrome),
        // so the following move behaves normally again.
        if (!_windowActive && !_chromeShown)
        {
            return;
        }

        // A pointer on the chrome parks the countdown (hover keeps the OSC up);
        // only movement over the video restarts it.
        if (_pointerOverChrome)
        {
            _autoHideTimer.Stop();
        }

        // Sub-threshold jitter (a hand resting on the mouse) is not activity:
        // the OSC must still fade out, even with the pointer sitting on it.
        if (!_oscState.PointerMoved(point.X, point.Y, _idleClock.Elapsed))
        {
            return;
        }

        if (!_chromeShown)
        {
            ShowChrome();
        }
        else
        {
            ResetAutoHideTimer();
        }
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!ChromeInteractionEnabled)
        {
            _pointerOverChrome = false;
            return;
        }

        // A flyout or an active drag temporarily owns the pointer outside the
        // window; hiding the chrome underneath it made menus and sliders appear
        // to "go crazy" mid-interaction. The flyout close handler resumes the
        // countdown once the popup is gone.
        if (_openFlyoutCount > 0 || WindowInterop.IsLeftButtonDown())
        {
            return;
        }

        // Root PointerExited also fires when the pointer moves between child
        // elements; only a real window exit should hide the chrome.
        if (WindowInterop.IsCursorInsideWindow(this))
        {
            return;
        }

        _pointerOverChrome = false;
        _oscState.ResetPointerAnchor();
        _autoHideTimer.Stop();
        CloseApplicationMenu();

        // A visible sidebar is an explicit operating mode. Moving the pointer
        // away must not tear down the titlebar/OSC underneath it. Once the
        // sidebar is closed, however, the normal auto-hide path must resume even
        // if the suppression flag is still unwinding.
        if (HasVisibleSidebar)
        {
            return;
        }

        HideChrome(force: true);
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_mode.Current is not (PlayerMode.Normal or PlayerMode.Fullscreen))
        {
            _pointerPressPosition = null;
            return;
        }

        var point = e.GetCurrentPoint(RootGrid);
        _pointerPressPosition = point.Properties.IsLeftButtonPressed
            ? point.Position
            : null;
    }

    private void OnRootPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_mode.Current is not (PlayerMode.Normal or PlayerMode.Fullscreen))
        {
            _pointerPressPosition = null;
            return;
        }

        Point point = e.GetCurrentPoint(RootGrid).Position;
        _pointerOverChrome = IsOverChrome(point);

        Point? press = _pointerPressPosition;
        _pointerPressPosition = null;
        bool isDrag = press is not { } start ||
            Math.Abs(point.X - start.X) > OscVisibilityController.MovementThreshold ||
            Math.Abs(point.Y - start.Y) > OscVisibilityController.MovementThreshold;

        // A plain left click on the video dismisses the chrome; clicks on the
        // chrome itself, drags and flyout clicks keep it available.
        // The HideOsc single-click action already toggles the chrome on the
        // click timer, so an immediate hide here would fight that toggle.
        if (_settings.SingleClickAction != SingleClickAction.HideOsc &&
            !_suppressClick &&
            !_windowDragging &&
            _settings.Layout != WindowLayoutMode.Docked &&
            _openFlyoutCount == 0 &&
            _oscState.ShouldHideOnClick(
                chromeVisible: _chromeShown,
                overChrome: _pointerOverChrome,
                isDrag: isDrag))
        {
            HideChrome(force: true);
            return;
        }

        ResetAutoHideTimer();
    }

    /// <summary>
    /// Tracks a menu that belongs to the player chrome. While any flyout is
    /// open the auto-hide timer stays parked; otherwise the popup window makes
    /// the main window look "left" and the OSC disappears under the menu.
    /// </summary>
    private void TrackChromeFlyout(MenuFlyout flyout)
    {
        flyout.Opened += (_, _) =>
        {
            _openFlyoutCount++;
            _autoHideTimer.Stop();
        };
        flyout.Closed += (_, _) =>
        {
            _openFlyoutCount = Math.Max(0, _openFlyoutCount - 1);
            if (_openFlyoutCount > 0)
            {
                return;
            }

            // Resume the countdown one dispatcher tick later: the popup's own
            // teardown still reports a pointer outside the player, and hiding
            // here immediately made the OSC flicker out the moment a menu item
            // was picked. The delayed pass sees the settled pointer position.
            DispatcherQueue.TryEnqueue(ResetAutoHideTimer);
        };
    }

    private void OnStatsTick(DispatcherQueueTimer sender, object args)
    {
        if (_player is not { IsRunning: true } player ||
            (!_settings.ShowMpvStats && !_settings.FrameDropOsd))
        {
            return;
        }

        // Frame-drop detection is independent from the statistics card: the old
        // implementation suppressed the OSD whenever the card was open.
        FrameDropCounters counters;
        bool paused;
        try
        {
            counters = ReadFrameDropCounters(player);
            paused = player.GetPropertyFlag("pause");
        }
        catch
        {
            // Tracks/decoders disappear while switching files; try again next
            // tick instead of reporting a bogus drop burst.
            _frameDropDetector.Reset();
            return;
        }

        double now = _frameDropClock.Elapsed.TotalSeconds;
        bool playing = !paused && !_seeking;
        _frameDropDetector.Sample(now, counters, playing);
        if (playing &&
            _settings.FrameDropOsd &&
            _frameDropDetector.TryTakeNotice(now, out FrameDropNotice notice))
        {
            // Report a rate, not a raw delta: one hiccup becomes a visible burst
            // in the detector's window, which is exactly what the user cares
            // about. The notice is an in-app card: mpv's show-text prints ASS
            // tags literally on this build, so the message used to land in the
            // top-left corner with its positioning markup visible.
            ShowFrameDropNotice(notice);
        }

        if (_settings.ShowMpvStats)
        {
            UpdateStatsPanel();
        }
    }

    private void OnAutoHideTick(DispatcherQueueTimer sender, object args)
    {
        _autoHideTimer.Stop();
        if (_seeking ||
            _openFlyoutCount > 0 ||
            WindowInterop.IsLeftButtonDown())
        {
            return;
        }

        // Hovering the chrome parks the countdown: the user is pointing at the
        // OSC/titlebar, so fading it out from under the pointer is wrong. Moving
        // off the chrome restarts the countdown.
        if (_pointerOverChrome && _chromeShown)
        {
            // PointerExited is not delivered reliably while the video surface is
            // swapping, so a stale "over chrome" flag used to park this countdown
            // forever - the OSC (and with it the cursor) stayed on screen until
            // the pointer moved again. Re-check the real cursor position.
            bool stillOver = WindowInterop.IsCursorInsideWindow(this) &&
                WindowInterop.TryGetCursorInWindow(this, out Point cursor) &&
                IsOverChrome(cursor);
            if (stillOver)
            {
                StartPointerWatch();
                return;
            }

            _pointerOverChrome = false;
        }

        // The idle clock, not the timer tick, is the source of truth: a resting
        // pointer over the video must not keep the OSC visible forever.
        if (_oscState.ShouldHideOnIdle(_idleClock.Elapsed))
        {
            HideChrome();
            return;
        }

        // Dispatcher timers may fire a hair before the interval elapses. If the
        // idle clock is not there yet, re-arm for the remainder instead of
        // leaving the chrome up until the next pointer move (which may never
        // come - the old "OSC stays forever" bug).
        if (_chromeShown && _oscState.CanHide && !HasVisibleSidebar)
        {
            TimeSpan remaining =
                TimeSpan.FromSeconds(_oscState.AutoHideSeconds) -
                (_idleClock.Elapsed - _oscState.LastActivity);
            _autoHideTimer.Interval = remaining < TimeSpan.FromMilliseconds(50)
                ? TimeSpan.FromMilliseconds(50)
                : remaining;
            _autoHideTimer.Start();
        }
    }

    private void OnChromePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Hovering the chrome keeps it on screen: the pointer resting on the OSC
        // is a user reading the controls, not an idle player. Leaving the chrome
        // (or the window) re-arms the normal idle countdown.
        _pointerOverChrome = true;
        ShowChrome();
        _autoHideTimer.Stop();
    }

    private void OnChromePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOverChrome = false;
        ResetAutoHideTimer();
    }

    /// <summary>
    /// Presence watchdog for the "move in, move out fast" case: WinUI may not
    /// deliver PointerExited when the pointer leaves during a capture change, so
    /// the chrome would stay up until the idle timer expires. Polling the real
    /// cursor position costs one Win32 call every 200 ms and only runs while the
    /// chrome is visible.
    /// </summary>
    private void OnPointerWatchTick(DispatcherQueueTimer sender, object args)
    {
        if (_closed || !_chromeShown || !ChromeInteractionEnabled)
        {
            sender.Stop();
            return;
        }

        if (!_windowActive ||
            WindowInterop.IsCursorInsideWindow(this) ||
            _openFlyoutCount > 0 ||
            WindowInterop.IsLeftButtonDown() ||
            _oscDragging)
        {
            return;
        }

        sender.Stop();
        _pointerOverChrome = false;
        _oscState.ResetPointerAnchor();
        CloseApplicationMenu();
        if (!HasVisibleSidebar)
        {
            HideChrome(force: true);
        }
    }

    private void StartPointerWatch()
    {
        if (_pointerWatchTimer.IsRunning == false)
        {
            _pointerWatchTimer.Start();
        }
    }

    private void ShowChrome()
    {
        if (!ChromeInteractionEnabled)
        {
            return;
        }

        // Central guard for every show path: an inactive window keeps its chrome
        // down so hover over an unfocused player cannot wake the OSC. While the
        // chrome is already visible (activated by a click, sidebar open, docked
        // layout) shows stay allowed.
        if (!_windowActive && !_chromeShown)
        {
            Trace("[chrome] show ignored (window inactive)");
            return;
        }

        if (!_chromeShown)
        {
            _chromeShown = true;
            _oscState.Show();
            _oscState.CompleteShow();
            ChromeBlur.SetActive(true);
            FadeChrome(1, hitTest: true);
            UpdateCursorForChrome();
        }

        StartPointerWatch();
        ResetAutoHideTimer();
    }

    private void HideChrome(bool force = false)
    {
        if (!ChromeInteractionEnabled)
        {
            return;
        }

        if (_settings.Layout == WindowLayoutMode.Docked)
        {
            // Docked titlebar/bottom bar are part of the layout: never fade
            // them out, otherwise the window looks like it lost its chrome.
            return;
        }

        if (_oscDragging)
        {
            return;
        }

        if (_openFlyoutCount > 0 || WindowInterop.IsLeftButtonDown())
        {
            return;
        }

        if (!force && !_oscState.CanHide)
        {
            return;
        }

        if (_oscState.Hide(force) != OscVisibilityState.WillHide || !_chromeShown)
        {
            return;
        }

        _oscState.CompleteHide();
        _chromeShown = false;
        ChromeBlur.SetActive(false);
        _autoHideTimer.Stop();
        _pointerWatchTimer.Stop();
        FadeChrome(0, hitTest: false);
        UpdateCursorForChrome();
    }

    /// <summary>
    /// Re-pushes the hidden pointer after WinUI handled a mouse move (it resets
    /// the cursor to the arrow on every move). Only the hidden state is applied
    /// here: while the chrome is visible the platform owns the cursor shape,
    /// including the resize grips and text I-beams.
    /// </summary>
    private void ReapplyHiddenCursor()
    {
        if (!ShouldHideCursor())
        {
            return;
        }

        WindowInterop.SetCursorVisible(false);
        StartCursorHideTimer();
    }

    /// <summary>
    /// Reconciles the pointer with the chrome state after a show/hide, layout or
    /// mode change: hidden only over the player once the chrome faded out,
    /// visible everywhere else (including the docked layout, where the chrome
    /// never hides).
    /// </summary>
    private void UpdateCursorForChrome()
    {
        bool hidden = ShouldHideCursor();
        Trace($"[cursor] update hidden={hidden} {DescribeCursorBlockers()}");
        WindowInterop.SetCursorVisible(!hidden);
        ApplyProtectedCursor(hidden);
        if (hidden)
        {
            StartCursorHideTimer();
        }
        else
        {
            _cursorHideTimer?.Stop();
        }
    }

    /// <summary>
    /// Pushes the WinUI-level cursor state once per transition. The XAML cursor
    /// is owned by the CoreMessaging input stack, which ignores SetCursor and
    /// WM_SETCURSOR, so <c>ProtectedCursor</c> on the content root (and the video
    /// host) is the only supported way to hide it. Reflection failures are
    /// swallowed inside the interop helper.
    /// </summary>
    private void ApplyProtectedCursor(bool hidden)
    {
        if (_protectedCursorHidden == hidden)
        {
            return;
        }

        _protectedCursorHidden = hidden;
        // The cursor WinUI displays comes from the deepest element under the
        // pointer, and the video area is covered by several stacked layers
        // (SwapChainPanel, gesture overlays, mini-player surfaces). Giving the
        // whole tree the blank cursor means whichever layer wins the hit test
        // still reports "no cursor"; restoring clears them back to their own
        // cursors.
        int applied = 0;
        foreach (FrameworkElement element in EnumerateCursorHosts(RootGrid))
        {
            if (WindowInterop.SetProtectedCursor(element, hidden))
            {
                applied++;
            }
        }

        Trace($"[cursor] protected hidden={hidden} elements={applied}");
    }

    private static IEnumerable<FrameworkElement> EnumerateCursorHosts(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DependencyObject node = stack.Pop();
            if (node is FrameworkElement element)
            {
                yield return element;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    /// <summary>
    /// The deepest element under the pointer is what WinUI asks for a cursor, so
    /// the SwapChainPanel inside the video host must carry the same protected
    /// cursor as the XAML root.
    /// </summary>
    private FrameworkElement FindCursorSurface()
    {
        if (_cursorSurface is not null)
        {
            return _cursorSurface;
        }

        _cursorSurface = VideoHost;
        try
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(VideoHost);
            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(current);
                for (int index = 0; index < count; index++)
                {
                    DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(current, index);
                    if (child is SwapChainPanel panel)
                    {
                        _cursorSurface = panel;
                        return _cursorSurface;
                    }

                    queue.Enqueue(child);
                }
            }
        }
        catch
        {
            // Visual tree walks are best effort; the video host covers the area.
        }

        return _cursorSurface;
    }

    private FrameworkElement? _cursorSurface;

    /// <summary>
    /// Whether the pointer may be hidden right now. It never hides while the
    /// chrome (OSC, titlebar, sidebar) is up, while the pointer is over that
    /// chrome, in the docked layout, when another window is active, or during
    /// close.
    /// </summary>
    private bool ShouldHideCursor() =>
        _windowActive &&
        !_closed &&
        !_chromeShown &&
        // The pointer position over the chrome is only meaningful while the
        // chrome is up: the auto-hide timer can fade the OSC out with the pointer
        // resting on it, and a stale "over chrome" flag would then leave the
        // cursor visible forever over a fully hidden player.
        !_oscDragging &&
        !_seeking &&
        _settings.Layout != WindowLayoutMode.Docked &&
        ChromeInteractionEnabled &&
        WindowInterop.IsCursorInsideWindow(this);

    private string DescribeCursorBlockers() =>
        $"active={_windowActive} closed={_closed} chrome={_chromeShown} over={_pointerOverChrome} " +
        $"drag={_oscDragging} seeking={_seeking} docked={_settings.Layout == WindowLayoutMode.Docked} " +
        $"mode={_mode.Current} inside={WindowInterop.IsCursorInsideWindow(this)}";

    private void StartCursorHideTimer()
    {
        if (_cursorHideTimer is { IsRunning: false })
        {
            _cursorHideTimer.Start();
        }
    }

    /// <summary>
    /// Keeps the hidden cursor hidden while playback runs untouched. The
    /// WM_SETCURSOR subclass handles every mouse move; this timer also covers
    /// hosts that stop sending it on a resting pointer, and re-hides after the
    /// platform bumps the ShowCursor display counter back up.
    /// </summary>
    private void OnCursorHideTick(DispatcherQueueTimer sender, object args)
    {
        if (!ShouldHideCursor())
        {
            Trace($"[cursor] tick stop: {DescribeCursorBlockers()}");
            sender.Stop();
            if (!WindowInterop.CursorHidden && !_protectedCursorHidden)
            {
                return;
            }

            WindowInterop.SetCursorVisible(true);
            ApplyProtectedCursor(false);
            return;
        }

        WindowInterop.ReassertHiddenCursor();
        if (_cursorDiagTick++ % 8 == 0)
        {
            WindowInterop.LogCursorState("tick");
            WindowInterop.LogCursorResourceState("tick");
        }
    }

    private void ResetAutoHideTimer()
    {
        _autoHideTimer.Stop();
        if (!ChromeInteractionEnabled ||
            _settings.Layout == WindowLayoutMode.Docked ||
            !_oscState.AutoHideEnabled ||
            _oscDragging ||
            _seeking ||
            _sidebarResizing ||
            _openFlyoutCount > 0 ||
            WindowInterop.IsLeftButtonDown() ||
            HasVisibleSidebar ||
            _oscState.IsSuppressed ||
            !_chromeShown)
        {
            return;
        }

        _oscState.AutoHideSeconds = _settings.OscAutoHideSeconds;
        _oscState.MarkActivity(_idleClock.Elapsed);
        _autoHideTimer.Interval = TimeSpan.FromSeconds(_oscState.AutoHideSeconds);
        _autoHideTimer.Start();
    }

    private void FadeChrome(double opacity, bool hitTest)
    {
        if (!ChromeInteractionEnabled)
        {
            return;
        }

        if (_settings.Layout == WindowLayoutMode.Docked)
        {
            opacity = 1;
            hitTest = true;
        }

        bool fullscreen = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        bool titleBarVisible = opacity > 0.01 && !fullscreen;
        SetTitleBarVisible(titleBarVisible);
        if (fullscreen)
        {
            TitleBar.Opacity = 0;
            TitleBar.IsHitTestVisible = false;
            _titleBarPinnedByFullscreen = true;
        }
        else
        {
            TitleBar.IsHitTestVisible = hitTest;
        }

        OscBottom.IsHitTestVisible = hitTest;
        OscFloating.IsHitTestVisible = hitTest;
        OscTop.IsHitTestVisible = hitTest;

        FrameworkElement[] elements = fullscreen
            ? new FrameworkElement[] { OscBottom, OscFloating, OscTop }
            : new FrameworkElement[] { TitleBar, OscBottom, OscFloating, OscTop };

        if (!AnimationsEnabled)
        {
            _chromeStoryboard?.Stop();
            _chromeStoryboard = null;
            foreach (FrameworkElement element in elements)
            {
                element.Opacity = opacity;
            }

            UpdateFullscreenStatus();
            UpdateOsdPlacement();
            return;
        }

        // Rapid show/hide (pointer sweeping across the chrome) used to leave two
        // storyboards animating the same property; the later one won visually
        // but the earlier one could finish last and strand the chrome at a
        // half-visible opacity. Always cancel the previous fade first.
        _chromeStoryboard?.Stop();

        var storyboard = new Storyboard();
        foreach (FrameworkElement element in elements)
        {
            var animation = new DoubleAnimation
            {
                To = opacity,
                Duration = TimeSpan.FromMilliseconds(250),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
        }

        storyboard.Completed += (_, _) =>
        {
            foreach (FrameworkElement element in elements)
            {
                element.Opacity = opacity;
            }
        };
        _chromeStoryboard = storyboard;
        storyboard.Begin();
        UpdateFullscreenStatus();
        UpdateOsdPlacement();
    }

    private bool IsOverChrome(Point point)
    {
        return Contains(TitleBar, point) ||
            (OscBottom.Visibility == Visibility.Visible && Contains(OscBottom, point)) ||
            (OscTop.Visibility == Visibility.Visible && Contains(OscTop, point)) ||
            (OscFloating.Visibility == Visibility.Visible && Contains(OscFloating, point)) ||
            (SidebarLeading.Visibility == Visibility.Visible && Contains(SidebarLeading, point)) ||
            (SidebarTrailing.Visibility == Visibility.Visible && Contains(SidebarTrailing, point)) ||
            (StatsPanel.Visibility == Visibility.Visible && Contains(StatsPanel, point));
    }

    private bool Contains(FrameworkElement element, Point point)
    {
        if (element.Visibility != Visibility.Visible ||
            element.Opacity <= 0.01 ||
            element.ActualWidth <= 0 ||
            element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            Point origin = element.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0));
            return point.X >= origin.X &&
                point.X <= origin.X + element.ActualWidth &&
                point.Y >= origin.Y &&
                point.Y <= origin.Y + element.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    private void ShowTimePreview(
        TimelineControl timeline,
        double seconds,
        double ratio,
        double pointerX)
    {
        _previewTimeline = timeline;
        if (_seeking ||
            HasVisibleSidebar ||
            _oscDragging ||
            OsdCoversTimeline())
        {
            HideTimePreview();
            return;
        }

        _previewRatio = Math.Clamp(ratio, 0, 1);
        _previewPointerX = Math.Max(0, pointerX);
        TimePreviewText.Text = TimeFormatter.Format(seconds);
        string? chapterTitle = FindChapterTitle(seconds);
        if (string.IsNullOrWhiteSpace(chapterTitle))
        {
            TimePreviewChapter.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimePreviewChapter.Text = chapterTitle;
            TimePreviewChapter.Visibility = Visibility.Visible;
        }

        TimePreview.Visibility = Visibility.Visible;
        // DesiredSize includes Margin. Zero it before measuring so the cached
        // size is content-only; otherwise the card width grows by its previous
        // left offset on every hover and the preview drifts across the video.
        TimePreview.Margin = new Thickness(0);
        TimePreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PositionTimePreview();

        if (_settings.ThumbnailPreviewEnabled && File.Exists(_currentPath))
        {
            double thumbWidth = Math.Clamp(_settings.ThumbnailWidth, 80, 320);
            double aspect = _videoAspect > 0 ? _videoAspect : 16.0 / 9.0;
            double thumbHeight = Math.Clamp(thumbWidth / aspect, 45, 220);
            ThumbnailPeek.ShowLoading(
                TimeFormatter.Format(seconds),
                thumbWidth,
                thumbHeight);
            PositionThumbnail();
            RequestThumbnailAsync(seconds);
        }
    }

    /// <summary>
    /// Anchors the preview to the timeline's current rendered position. The
    /// timeline can move (OSC slide, dock/sidebar, DPI change), so this is
    /// recomputed from the live transform instead of caching window pixels.
    /// </summary>
    private void PositionTimePreview()
    {
        if (_previewTimeline is null || TimePreview.Visibility != Visibility.Visible)
        {
            return;
        }

        try
        {
            // Measured with Margin reset in ShowTimePreview, so this is the
            // content size rather than content + previous position.
            double width = TimePreview.DesiredSize.Width;
            double height = TimePreview.DesiredSize.Height;
            if (width <= 0)
            {
                width = Math.Max(0, TimePreview.ActualWidth);
            }

            if (height <= 0)
            {
                height = Math.Max(0, TimePreview.ActualHeight);
            }

            _previewWidth = width;
            _previewHeight = height;
            Point origin = _previewTimeline.TransformToVisual(OverlayLayer)
                .TransformPoint(new Point(0, 0));
            double centerX = origin.X +
                Math.Clamp(_previewPointerX, 0, _previewTimeline.ActualWidth);
            double left = Math.Clamp(
                centerX - width / 2,
                8,
                Math.Max(8, OverlayLayer.ActualWidth - width - 8));
            double top = origin.Y - height - 8;
            _previewAboveTimeline = top >= 8;
            if (!_previewAboveTimeline)
            {
                top = origin.Y + _previewTimeline.ActualHeight + 8;
            }

            top = Math.Clamp(top, 8, Math.Max(8, OverlayLayer.ActualHeight - height - 8));
            TimePreview.Margin = new Thickness(left, top, 0, 0);
        }
        catch
        {
            // Layout can race a collapse; the next hover repositions it.
        }
    }

    private string? FindChapterTitle(double seconds)
    {
        string? title = null;
        foreach (ChapterInfo chapter in _chapters)
        {
            if (chapter.Time > seconds)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(chapter.Title))
            {
                title = chapter.Title;
            }
        }

        return title;
    }

    private void HideTimePreview()
    {
        TimePreview.Visibility = Visibility.Collapsed;
        ThumbnailPeek.Hide();
        _previewTimeline = null;
        _thumbnailRequest++;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private void OnOscDragStart(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(OscFloating).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _oscDragging = true;
        _oscState.IsDragging = true;
        HideTimePreview();
        _autoHideTimer.Stop();
        _oscDragStart = e.GetCurrentPoint(RootGrid).Position;
        double left = Canvas.GetLeft(OscFloating);
        double top = Canvas.GetTop(OscFloating);
        _oscOrigin = new Point(
            double.IsNaN(left) ? 0 : left,
            double.IsNaN(top) ? 0 : top);
        OscFloating.DragHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnOscDragMove(object sender, PointerRoutedEventArgs e)
    {
        if (!_oscDragging)
        {
            return;
        }

        Point current = e.GetCurrentPoint(RootGrid).Position;
        double x = _oscOrigin.X + (current.X - _oscDragStart.X);
        double y = _oscOrigin.Y + (current.Y - _oscDragStart.Y);
        double availableWidth = OscCanvas.ActualWidth;
        double availableHeight = OscCanvas.ActualHeight;
        double oscWidth = OscFloating.ActualWidth;
        double oscHeight = OscFloating.ActualHeight;
        if (availableWidth <= oscWidth || availableHeight <= oscHeight)
        {
            return;
        }

        y = Math.Clamp(y, 0, availableHeight - oscHeight);

        // Dragging honours the same sidebar rule as ApplyOscPosition: the panel
        // only blocks the area it actually covers.
        double minX = 0;
        double maxX = availableWidth - oscWidth;
        (double leading, double trailing) = VisibleSidebarInsets();
        if ((leading > 0 || trailing > 0) &&
            y + oscHeight > SidebarPanelTop() + SidebarOverlapTolerance)
        {
            double sidebarMinX = leading > 0 ? leading + OscSidebarGap : 0;
            double sidebarMaxX = (trailing > 0
                ? availableWidth - trailing - OscSidebarGap
                : availableWidth) - oscWidth;
            if (sidebarMaxX >= sidebarMinX)
            {
                minX = sidebarMinX;
                maxX = sidebarMaxX;
            }
        }

        x = Math.Clamp(x, minX, maxX);

        // Snap the horizontal centre within +-5 px (spec §5.6).
        double centerX = x + oscWidth / 2;
        double snappedCenter = OscPositioning.SnapToCenter(centerX, availableWidth, 5);
        x = Math.Clamp(snappedCenter - oscWidth / 2, minX, maxX);

        Canvas.SetLeft(OscFloating, x);
        Canvas.SetTop(OscFloating, y);
        e.Handled = true;
    }

    private void OnOscDragEnd(object sender, PointerRoutedEventArgs e)
    {
        if (!_oscDragging)
        {
            return;
        }

        _oscDragging = false;
        _oscState.IsDragging = false;
        try
        {
            OscFloating.DragHandle.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // Pointer may already be gone.
        }

        SaveOscPosition();
        ResetAutoHideTimer();
    }

    private void ApplyOscPosition()
    {
        if (OscFloating.Visibility != Visibility.Visible ||
            OscCanvas.ActualWidth < 10 ||
            OscCanvas.ActualHeight < 10 ||
            _oscDragging)
        {
            return;
        }

        double width = OscCanvas.ActualWidth;
        double height = OscCanvas.ActualHeight;
        double oscWidth = OscFloating.ActualWidth > 0 ? OscFloating.ActualWidth : 460;
        double oscHeight = OscFloating.ActualHeight > 0 ? OscFloating.ActualHeight : 64;

        double centerX = OscPositioning.ClampCenter(
            OscPositioning.DenormalizeCenter(_settings.OscX, width),
            oscWidth,
            width);
        double centerY = OscPositioning.ClampCenter(
            OscPositioning.DenormalizeCenter(_settings.OscY, height),
            oscHeight,
            height);
        double left = centerX - oscWidth / 2;
        double top = centerY - oscHeight / 2;

        // A sidebar displaces the floating OSC only when the two rectangles
        // actually intersect. Opening a panel must not make the controls jump
        // across the video when they sit above it or beside it.
        (double leading, double trailing) = VisibleSidebarInsets();
        if ((leading > 0 || trailing > 0) &&
            top + oscHeight > SidebarPanelTop() + SidebarOverlapTolerance)
        {
            double minLeft = leading > 0 ? leading + OscSidebarGap : 0;
            double maxLeft = (trailing > 0
                ? width - trailing - OscSidebarGap
                : width) - oscWidth;
            if (maxLeft >= minLeft)
            {
                left = Math.Clamp(left, minLeft, maxLeft);
            }
        }

        Canvas.SetLeft(OscFloating, left);
        Canvas.SetTop(OscFloating, top);
    }

    /// <summary>Gap kept between the floating OSC and an open sidebar panel.</summary>
    private const double OscSidebarGap = 8;

    /// <summary>Vertical slack when testing whether the sidebar would cover the OSC.</summary>
    private const double SidebarOverlapTolerance = 0.5;

    /// <summary>Widths of the sidebar panels that are currently on screen.</summary>
    private (double Leading, double Trailing) VisibleSidebarInsets()
    {
        // A panel that is sliding out keeps reporting Visible at its full width
        // until the animation collapses it; it must not keep the floating OSC
        // pushed aside for that trailing stretch.
        double leading = SidebarLeading.Visibility == Visibility.Visible &&
            !SidebarLeading.IsHiding &&
            SidebarLeading.ActualWidth > 0
            ? SidebarLeading.ActualWidth
            : 0;
        double trailing = SidebarTrailing.Visibility == Visibility.Visible &&
            !SidebarTrailing.IsHiding &&
            SidebarTrailing.ActualWidth > 0
            ? SidebarTrailing.ActualWidth
            : 0;
        return (leading, trailing);
    }

    /// <summary>Top edge of the sidebar panels (they start under the titlebar).</summary>
    private double SidebarPanelTop() =>
        Math.Max(0, Math.Max(SidebarLeading.Margin.Top, SidebarTrailing.Margin.Top));

    private void SaveOscPosition()
    {
        double width = OscCanvas.ActualWidth;
        double height = OscCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double left = Canvas.GetLeft(OscFloating);
        double top = Canvas.GetTop(OscFloating);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return;
        }

        _settings.OscX = OscPositioning.NormalizeCenter(
            left + OscFloating.ActualWidth / 2,
            width);
        _settings.OscY = OscPositioning.NormalizeCenter(
            top + OscFloating.ActualHeight / 2,
            height);
        SaveSettings();
    }

    private void SetOscPosition(OscPosition position)
    {
        _settings.OscPosition = position;
        ApplyLayout();
        SaveSettings();
    }

    private void OnMoreClicked(object? sender, EventArgs e)
    {
        FrameworkElement anchor = sender is OscToolbar toolbar
            ? toolbar.MoreAnchor
            : OscFloating;
        ShowApplicationMenu(anchor);
    }
    private void ToggleFullscreen()
    {
        RequestMode(IsFullscreen ? PlayerMode.Normal : PlayerMode.Fullscreen);
    }

    private void SyncFullscreenState()
    {
        _oscVm.IsFullscreen = IsFullscreen;
        UpdateFullscreenStatus();
        UpdateOsdPlacement();
    }

    private void OnNumberPropertyChanged(string name, double value)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (name)
            {
                case "time-pos":
                    _oscVm.SetPositionFromPlayer(value);
                    MaintainAbLoop(value);
                    break;
                case "duration":
                    _oscVm.SetDurationFromPlayer(value);
                    UpdateSmtc();
                    break;
                case "volume":
                    _oscVm.SetVolumeFromPlayer(value);
                    _settings.Volume = QuickSettings.ClampVolume(value, _settings.MaxVolume);
                    SyncSidebarVolume();
                    break;
                case "demuxer-cache-time":
                    _oscVm.SetBufferFromPlayer(value);
                    break;
                case "cache-buffering-state":
                    SetCacheBuffering(value);
                    break;
                case "cache-speed":
                    SetCacheSpeed(value);
                    break;
                case "speed":
                    _oscVm.SetSpeedFromPlayer(value);
                    break;
                default:
                    OnStage3NumberProperty(name, value);
                    break;
            }
        });
    }

    private void OnFlagPropertyChanged(string name, bool value)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (name)
            {
                case "pause":
                    _oscVm.SetPlayingFromPlayer(!value);
                    _pipWindow?.SetPlaying(!value);
                    UpdateSmtc();
                    break;
                case "mute":
                    _oscVm.SetMutedFromPlayer(value);
                    _settings.Muted = value;
                    SyncSidebarVolume();
                    break;
                case "paused-for-cache":
                    SetPausedForCache(value);
                    break;
                case "seeking":
                    SetSeekingProperty(value);
                    break;
                default:
                    OnStage3FlagProperty(name, value);
                    break;
            }
        });
    }

    private void OnStringPropertyChanged(string name, string? value)
    {
        if (name == "media-title" && !string.IsNullOrWhiteSpace(value))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _currentTitle = value;
                SetWindowTitle(value);
                UpdateSmtc();
            });
        }

        DispatcherQueue.TryEnqueue(() => OnStage3StringProperty(name, value));
    }

    private void OnFileLoaded()
    {
        DispatcherQueue.TryEnqueue(ApplyLoadedMedia);
    }

    private void ApplyLoadedMedia()
    {
        if (_player is null)
        {
            return;
        }

        _oscVm.SetDurationFromPlayer(_player.GetPropertyDouble("duration"));
        _oscVm.SetPositionFromPlayer(_player.GetPropertyDouble("time-pos"));
        _oscVm.SetVolumeFromPlayer(_player.GetPropertyDouble("volume"));
        _oscVm.SetSpeedFromPlayer(_player.GetPropertyDouble("speed"));
        _oscVm.SetPlayingFromPlayer(!_player.GetPropertyFlag("pause"));
        _oscVm.SetMutedFromPlayer(_player.GetPropertyFlag("mute"));

        _chapters = _chapterList;
        _oscVm.Chapters = _chapterList.Select(chapter => chapter.Time).ToArray();
        _oscVm.AbLoopA = null;
        _oscVm.AbLoopB = null;
        UpdateTimelines();

        string? path = _player.GetPropertyString("path");
        string? title = _player.GetPropertyString("media-title");
        if (string.IsNullOrWhiteSpace(title) ||
            (!string.IsNullOrEmpty(path) && string.Equals(title, path, StringComparison.Ordinal)))
        {
            title = string.IsNullOrEmpty(path)
                ? null
                : System.IO.Path.GetFileName(path);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            _currentTitle = title;
            SetWindowTitle(title);
        }

        ShowChrome();
        OnMediaLoadedStage3();
        EndStreamOpen();
        RefreshSidebarContent();
        ApplyCropFromSettings();
        ApplyStage8Settings();

        if (_pendingFilterResumeTime is double resume)
        {
            _pendingFilterResumeTime = null;
            if (resume > 0.25)
            {
                SeekAbsolute(resume, showOsd: false);
            }

            if (_pendingFilterResumePaused)
            {
                _player.CommandAsync("set", "pause", "yes");
            }
        }
    }

    /// <summary>
    /// Re-resolves the theme for this player window. Also called by
    /// <see cref="App"/> for every registered window on a theme switch.
    /// </summary>
    internal void ApplyTheme()
    {
        ElementTheme theme = App.ResolveTheme();

        RootGrid.RequestedTheme = theme;
        // OsdText is loaded before the first theme pass; its ThemeResource
        // brushes can otherwise keep the dark values used at construction.
        Osd.RequestedTheme = theme;
        // PiP is a separate HWND; keep it in sync while it is open.
        _pipWindow?.ApplyTheme();
        // Chrome paint is a solid colour mixed with the system accent, so it
        // must be recomputed whenever the theme (and therefore the resolved
        // theme-dictionary brush) changes.
        Services.ChromeTint.Recolor();
        // The settings save is debounced; other windows should not keep the old
        // theme until it lands (a nested call is a no-op).
        App.SyncOpenWindowThemes();

        DispatcherQueue.TryEnqueue(ConfigureCaptionButtons);
    }

    private void ConfigureCaptionButtons()
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            // Windows 10 and hosts without title-bar customisation: the
            // caption-colour/height setters throw or are ignored there, so
            // keep the native chrome and hide our XAML duplicates instead of
            // crashing the dispatcher callback.
            TitleBar.ShowCustomCaptionButtons = false;
            return;
        }

        TitleBar.ShowCustomCaptionButtons = true;
        bar.ExtendsContentIntoTitleBar = true;

        bool dark = App.IsDarkTheme;

        bar.BackgroundColor = Colors.Transparent;
        bar.InactiveBackgroundColor = Colors.Transparent;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        bar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x1F, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0, 0, 0);
    }

    /// <summary>
    /// WinUI 3 extended titlebar visibility. Collapsing the native titlebar
    /// height also removes the system caption buttons (they are drawn by DWM
    /// above XAML, so fading the custom titlebar alone is not enough).
    /// </summary>
    private void SetTitleBarVisible(bool visible)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen || InMiniMode)
        {
            visible = false;
        }

        _titleBarVisible = visible;
        // System caption buttons are always collapsed: DWM paints an opaque
        // backing behind them in an extended titlebar. The custom WinUI
        // controls in TitleBarView provide the native-looking window buttons.
        // Only touch this when the platform supports titlebar customisation:
        // on Windows 10 the setter faults.
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        }
        TitleBar.Visibility = Visibility.Visible;

        // Fullscreen pinned the titlebar transparent; once the overlapped
        // presenter is back, the titlebar must follow the chrome visibility again
        // (its own fades set the matching opacity and hit testing).
        if (visible && _titleBarPinnedByFullscreen &&
            AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
        {
            _titleBarPinnedByFullscreen = false;
            TitleBar.Opacity = 1;
            TitleBar.IsHitTestVisible = true;
        }

        OscTop.Margin = new Thickness(0, visible ? 40 : 0, 0, 0);
        UpdateSidebarMargins();
    }

    private void ToggleMaximize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            // A maximized/restored transition invalidates the edge magnet; the
            // flush-edge DWM frame must be restored when leaving the edge.
            _snapSession.Reset();
            ApplySnapFrame(false);
            presenter.Maximize();
        }
    }

    private void TrySetBackdrop()
    {
        try
        {
            // Windows Terminal-style acrylic; Mica is the fallback on systems
            // where DesktopAcrylicBackdrop is unavailable.
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // Solid background remains a valid fallback.
            }
        }
    }

    private void ApplyLayout()
    {
        if (InMiniMode)
        {
            ApplyMiniLayout();
            return;
        }

        if (InPipMode)
        {
            ApplyPipMainLayout();
            return;
        }

        _compact = RootGrid.ActualWidth > 0 &&
            RootGrid.ActualWidth < SidebarState.CompactWindowWidth;
        if (_compact)
        {
            CollapseSidebarsInstant();
        }

        UpdateSidebarMargins();

        bool docked = _settings.Layout == WindowLayoutMode.Docked;
        Grid.SetRow(VideoFrame, docked ? 1 : 0);
        Grid.SetRowSpan(VideoFrame, docked ? 1 : 3);

        TitleBar.SetDocked(docked);
        TitleBar.IsPinned = _settings.AlwaysOnTop;

        bool topOsc = !_compact && _settings.OscPosition == OscPosition.Top;
        OscTop.Visibility = topOsc ? Visibility.Visible : Visibility.Collapsed;

        bool useBottomOsc = !topOsc &&
            (_settings.OscPosition == OscPosition.Bottom ||
             (docked && _settings.OscPosition == OscPosition.Floating));
        OscBottom.Visibility = useBottomOsc ? Visibility.Visible : Visibility.Collapsed;

        bool sidebarVisible = !_compact && HasVisibleSidebar;

        // The floating OSC stays available while a sidebar is open (the sidebar
        // suppresses auto-hide rather than hiding the controls). Placement is
        // resolved per OSC in ApplyOscPosition: the panel only pushes the
        // floating controls when it would actually cover them.
        OscFloating.Visibility = !docked && !_compact && !topOsc &&
            _settings.OscPosition == OscPosition.Floating
            ? Visibility.Visible
            : Visibility.Collapsed;
        OscFloating.SetAvailableWidth(RootGrid.ActualWidth);

        _oscState.IsSuppressed = sidebarVisible || _sidebarResizing;

        UpdateDockedInsets();
        OscCanvas.Visibility = Visibility.Visible;
        OscCanvas.Margin = new Thickness(0);

        if (docked)
        {
            // Docked titlebar/bottom bar are layout chrome and stay visible.
            _chromeShown = true;
            _autoHideTimer.Stop();
        }
        else if (sidebarVisible)
        {
            _chromeShown = true;
        }

        FadeChrome(_chromeShown ? 1 : 0, _chromeShown);
        ApplyOscPosition();
        UpdateOsdPlacement();
        UpdateFullscreenStatus();
        if (sidebarVisible)
        {
            HideTimePreview();
        }

        ResetAutoHideTimer();
        // Layout/mode changes can hide or reveal the chrome under a resting
        // pointer, and the platform resets the cursor on the next move: push the
        // state that belongs to the new layout.
        UpdateCursorForChrome();
    }

    /// <summary>Shared edge magnet: snap threshold + breakaway hysteresis.</summary>
    private readonly EdgeSnapSession _snapSession = new();

    /// <summary>Last snapped state requested by the edge magnet (video/mini drag, WM_MOVING).</summary>
    private bool _snapFrameEngaged;

    /// <summary>
    /// Last DWM frame style we applied, or null when it must be re-applied (a
    /// presenter switch rebuilds the non-client frame and can re-enable the
    /// system border behind our back).
    /// </summary>
    private WindowInterop.WindowFrameStyle? _frameStyleApplied;

    /// <summary>
    /// Invisible resize-border margins of this window. The OS reports the outer
    /// window rect (≈8 px of border on left/right/bottom at 100%), so snapping
    /// the outer rect leaves a gap on three sides; every snap path passes these
    /// margins to <see cref="EdgeSnapSession"/> so the visible frame is what
    /// lands flush (issue 2).
    /// </summary>
    private FrameMargins SnapMargins() =>
        WindowInterop.TryGetFrameMargins(this, out FrameMargins margins) ? margins : FrameMargins.None;

    // The OS anchors WM_MOVING proposals on the last rectangle the window was
    // moved to, so the proposal is not the cursor-anchored ("raw") position after
    // a snap. Feeding the applied rect back into the session made the magnet drift
    // and flicker at the edge. The raw position is reconstructed from the cursor
    // plus the grab offset captured once per drag gesture instead.
    private int _moveAnchorX;
    private int _moveAnchorY;
    private bool _moveAnchorValid;

    /// <summary>WM_ENTERSIZEMOVE: a fresh drag gesture starts.</summary>
    private void OnWindowMoveStarted()
    {
        _snapSession.Reset();
        _moveAnchorValid = false;
    }

    private void SnapWindowToEdges()
    {
        if (!_settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _snapSession.Reset();
            ApplySnapFrame(false);
            return;
        }

        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        FrameMargins margins = SnapMargins();
        EdgeSnapResult result = _snapSession.Apply(
            new WindowRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height),
            WorkAreaUnderPointer(),
            DisplayScale,
            margins: margins);

        if (result.Rect.Left != position.X || result.Rect.Top != position.Y)
        {
            AppWindow.Move(new PointInt32(result.Rect.Left, result.Rect.Top));
        }

        ApplySnapFrame(result.IsSnapped);
    }

    /// <summary>
    /// Snaps the live WM_MOVING rectangle so edges feel magnetic while the user
    /// is still dragging, not only after releasing the mouse. The OS-proposed
    /// rectangle is ignored for the snap decision (it is anchored on the applied
    /// rect); the raw rectangle is rebuilt from the cursor and the grab offset
    /// captured at the first message of the gesture, so a 1 px drag always moves
    /// the window 1 px and the magnet cannot feed back into itself.
    /// </summary>
    private WindowRect SnapMovingRect(WindowRect rect)
    {
        if (!_settings.EdgeSnapping ||
            AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _snapSession.Reset();
            _moveAnchorValid = false;
            ApplySnapFrame(false);
            return rect;
        }

        FrameMargins snapMargins = SnapMargins();
        WindowRect raw = rect;
        if (WindowInterop.TryGetCursorPosition(out int cursorX, out int cursorY))
        {
            if (!_moveAnchorValid)
            {
                _moveAnchorX = rect.Left - cursorX;
                _moveAnchorY = rect.Top - cursorY;
                _moveAnchorValid = true;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int left = cursorX + _moveAnchorX;
            int top = cursorY + _moveAnchorY;
            raw = new WindowRect(left, top, left + width, top + height);
        }

        EdgeSnapResult result = _snapSession.Apply(
            raw,
            WorkAreaUnderPointer(),
            DisplayScale,
            margins: snapMargins);

        ApplySnapFrame(result.IsSnapped);
        return result.Rect;
    }

    private double DisplayScale => RootGrid.XamlRoot?.RasterizationScale ?? 1.0;

    /// <summary>Work area of the monitor under the pointer (fallback: nearest).</summary>
    private WorkArea WorkAreaUnderPointer()
    {
        DisplayArea area = WindowInterop.TryGetCursorPosition(out int x, out int y)
            ? DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 work = area.WorkArea;
        return new WorkArea(work.X, work.Y, work.Width, work.Height);
    }

    /// <summary>
    /// Records the magnet state and re-resolves the effective frame style.
    /// Fullscreen always wins over a stale snapped state, which is what used to
    /// leave a thin outline around the fullscreen surface.
    /// </summary>
    private void ApplySnapFrame(bool snapped)
    {
        _snapFrameEngaged = snapped;
        ApplyWindowFrame();
    }

    /// <summary>
    /// Applies the DWM frame only when the effective style changes. A snapped
    /// window gets no border line and no rounded corners, so it really touches
    /// the monitor edge (issue 4 acceptance); a fullscreen window forces the
    /// same flush style so no ring survives the presenter switch (v1.2.7).
    /// </summary>
    private void ApplyWindowFrame()
    {
        WindowInterop.WindowFrameStyle style = IsFullscreen
            ? WindowInterop.WindowFrameStyle.Fullscreen
            : _snapFrameEngaged
                ? WindowInterop.WindowFrameStyle.Snapped
                : WindowInterop.WindowFrameStyle.Default;
        if (_frameStyleApplied == style)
        {
            return;
        }

        _frameStyleApplied = style;
        WindowInterop.SetWindowFrame(this, style);
    }

    /// <summary>
    /// Presenter switches recreate the non-client frame, so the DWM border may
    /// come back after we set it. Entering fullscreen also invalidates the edge
    /// magnet: the snapped frame must not be restored when leaving fullscreen.
    /// </summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange)
        {
            return;
        }

        if (IsFullscreen)
        {
            _snapSession.Reset();
            _snapFrameEngaged = false;
        }

        _frameStyleApplied = null;
        ApplyWindowFrame();

        // The content island is re-created on a presenter switch and WinUI puts
        // it one pixel below the client top, which shows as a 1 px strip above
        // the video in fullscreen. Re-stretch it once the frame has settled.
        DispatcherQueue.TryEnqueue(() => WindowInterop.CoverClientArea(this));
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.SetMode(_settings.ChromeBlurMode);
        ConfigureCaptionButtons();
        WindowInterop.Attach(
            this,
            285,
            120,
            () => _settings.LockAspectRatio,
            () => _videoAspect,
            OnAppCommand,
            () => _userResizedWindow = true,
            SnapWindowToEdges,
            SnapMovingRect,
            // A fresh drag/resize gesture starts with no magnet engaged and a
            // fresh cursor anchor for the WM_MOVING snap path.
            OnWindowMoveStarted,
            // Fullscreen reports every hit as client area: a native move or
            // resize would drop the fullscreen presenter.
            () => IsFullscreen);

        if (RootGrid.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }

        // Fullscreen / PiP / mini switches rebuild the DWM frame; keep the
        // borderless style authoritative instead of trusting the cached state.
        AppWindow.Changed += OnAppWindowChanged;

        RootGrid.Focus(FocusState.Programmatic);
        UpdateSmtc();
    }

    private void OnVideoFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactState();
        OscFloating.SetAvailableWidth(RootGrid.ActualWidth);
        ApplyOscPosition();
        UpdateOsdPlacement();
        if (_previewTimeline is not null && TimePreview.Visibility == Visibility.Visible)
        {
            PositionTimePreview();
            PositionThumbnail();
        }

        // A resize can leave the pointer over a different element, and the next
        // mouse move will have its cursor reset by the platform.
        ReapplyHiddenCursor();

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateCompactState()
    {
        bool compact = RootGrid.ActualWidth > 0 &&
            RootGrid.ActualWidth < SidebarState.CompactWindowWidth;
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
        // A DPI / monitor change recreates the DWM frame; re-assert the
        // fullscreen or snapped decoration so no border ring survives it.
        ApplyWindowFrame();
        if (_previewTimeline is not null && TimePreview.Visibility == Visibility.Visible)
        {
            PositionTimePreview();
            PositionThumbnail();
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
            _player.Log += OnPlayerLog;
            _player.DisplaySwapChainChanged += OnDisplaySwapChainChanged;
            _player.NumberPropertyChanged += OnNumberPropertyChanged;
            _player.FlagPropertyChanged += OnFlagPropertyChanged;
            _player.StringPropertyChanged += OnStringPropertyChanged;
            _player.FileLoaded += OnFileLoaded;
            _player.Start(
                width,
                height,
                headless: false,
                hwdec: HasActiveVideoFilters() ? "no" : EffectiveHwdecValue());
            _player.ObserveProperty("display-swapchain", MpvFormat.Int64);
            InitializePlayerStage3(_player);
            ApplyQualityPreset();
            ApplyPersistedQuickSettings();
            ApplyStage7Settings();
            ApplyStage8Settings();

            _oscVm.SetDurationFromPlayer(_player.GetPropertyDouble("duration"));
            _oscVm.SetPositionFromPlayer(_player.GetPropertyDouble("time-pos"));
            _oscVm.SetVolumeFromPlayer(_player.GetPropertyDouble("volume"));
            _oscVm.SetBufferFromPlayer(_player.GetPropertyDouble("demuxer-cache-time"));
            _oscVm.SetSpeedFromPlayer(_player.GetPropertyDouble("speed"));
            _oscVm.SetPlayingFromPlayer(!_player.GetPropertyFlag("pause"));
            _oscVm.SetMutedFromPlayer(_player.GetPropertyFlag("mute"));
            SyncFullscreenState();
            UpdateTimelines();

            long existing = _player.GetPropertyInt64("display-swapchain");
            if (existing > 0)
            {
                OnDisplaySwapChainChanged(existing);
            }

            OpenStartupMedia();
        }
        catch (Exception ex)
        {
            Trace("mpv start failed: " + ex);
            TitleBar.Title = "Wiina — mpv failed";
            ShowOsd(Loc.T("osd.engineFailed"), ex.Message);
        }
    }

    private void OnPlayerLog(string message)
    {
        Trace("mpv: " + message);

        // A lost device floods the log (the 1.2.6 trace had 73 hits of
        // 0x887A0005 from the screenshot path). Only the first line of a burst
        // may start recovery; the budget ignores the rest.
        if (DeviceLoss.ClassifyLogLine(message) is DeviceLossSignal signal &&
            signal != DeviceLossSignal.None)
        {
            _ = DispatcherQueue.TryEnqueue(() => RecoverFromDeviceLoss($"mpv log ({signal})"));
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
                if (InPipMode && _pipWindow is { } pip)
                {
                    pip.AttachSwapChain(_swapChain);
                }
                else
                {
                    VideoHost.Attach(_swapChain);
                }

                CompleteDeviceRecovery();
            }
            catch (Exception ex)
            {
                Trace("swapchain bind failed: " + ex);

                // The bind reports the device loss directly when the device is
                // already gone; the log path would also catch it, but acting
                // here avoids waiting for mpv to print the next line.
                if (DeviceLoss.IsDeviceLoss(ex.HResult))
                {
                    RecoverFromDeviceLoss($"bind hr=0x{ex.HResult:X8}");
                }
            }
        });
    }

    /// <summary>
    /// Rebuilds the video output after the D3D11 device was removed. mpv owns
    /// the device and the composition swapchain, so the app cannot recreate
    /// either itself: it detaches the panel, asks the VO to rebuild
    /// (<c>video-reload</c>), and lets mpv publish a fresh
    /// <c>display-swapchain</c> that rebinds through
    /// <see cref="OnDisplaySwapChainChanged"/>. Playback state (position,
    /// playlist, audio) is untouched by a video-output reload.
    /// </summary>
    private void RecoverFromDeviceLoss(string reason)
    {
        if (_closed || _closing || _deviceRecoveryFailed)
        {
            return;
        }

        if (_player is not { } player || !player.IsRunning)
        {
            return;
        }

        if (_deviceRecoveryPending)
        {
            Trace($"[devicelost] {reason}: recovery already pending");
            return;
        }

        if (!_deviceRecovery.TryBegin(DateTimeOffset.UtcNow))
        {
            Trace($"[devicelost] {reason}: budget exhausted after {_deviceRecovery.Attempts} attempt(s)");
            FailDeviceRecovery();
            return;
        }

        _deviceRecoveryPending = true;
        Trace($"[devicelost] {reason}: attempt {_deviceRecovery.Attempts}/{DeviceLossRecoveryBudget.DefaultAttempts}");

        // The blur worker is the only part of Wiina that keeps submitting GPU
        // work against the dead device (8 Hz screenshot-raw). Stop it first.
        ChromeBlur.SetActive(false);

        try
        {
            DetachVideoSurface();
        }
        catch (Exception ex)
        {
            Trace("[devicelost] detach failed: " + ex.Message);
        }

        ShowOsd(Loc.T("osd.deviceLost"));

        try
        {
            player.CommandAsync("video-reload");
        }
        catch (Exception ex)
        {
            Trace("[devicelost] video-reload failed: " + ex);
            _deviceRecoveryPending = false;
            FailDeviceRecovery();
            return;
        }

        StartDeviceRecoveryTimeout();
    }

    /// <summary>Detaches mpv's swapchain from whichever panel currently shows it.</summary>
    private void DetachVideoSurface()
    {
        if (InPipMode && _pipWindow is { } pip)
        {
            pip.DetachSwapChain();
        }
        else
        {
            VideoHost.Detach();
        }

        _swapChain = 0;
    }

    private void StartDeviceRecoveryTimeout()
    {
        _deviceRecoveryTimer ??= DispatcherQueue.CreateTimer();
        _deviceRecoveryTimer.Interval = TimeSpan.FromSeconds(5);
        _deviceRecoveryTimer.IsRepeating = false;
        _deviceRecoveryTimer.Tick += (_, _) =>
        {
            if (!_deviceRecoveryPending)
            {
                return;
            }

            _deviceRecoveryPending = false;
            Trace("[devicelost] no new swapchain within 5 s");
            FailDeviceRecovery();
        };
        _deviceRecoveryTimer.Start();
    }

    private void CompleteDeviceRecovery()
    {
        if (!_deviceRecoveryPending)
        {
            return;
        }

        _deviceRecoveryPending = false;
        _deviceRecoveryTimer?.Stop();
        Trace("[devicelost] recovered");
        ChromeBlur.SetMode(_settings.ChromeBlurMode);
        ShowOsd(Loc.T("osd.deviceRecovered"));
    }

    private void FailDeviceRecovery()
    {
        if (_deviceRecoveryFailed)
        {
            return;
        }

        _deviceRecoveryFailed = true;
        _deviceRecoveryTimer?.Stop();
        Trace("[devicelost] giving up; playback continues without video output");
        ShowOsd(Loc.T("osd.deviceLostFailed"));
    }

    private (int Width, int Height) _lastCompositionSize;

    private void ApplyVideoLayout()
    {
        UpdateCompactState();

        // While the swapchain lives in the PiP window the main surface must not
        // resize it; the PiP window owns the composition size for now.
        if (InPipMode)
        {
            return;
        }

        double dpi = VideoFrame.XamlRoot?.RasterizationScale ?? 1.0;
        double dipWidth = VideoFrame.ActualWidth;
        double dipHeight = VideoFrame.ActualHeight;
        if (dipWidth < 16 || dipHeight < 16)
        {
            return;
        }

        int pixelWidth = Math.Max(1, (int)Math.Round(dipWidth * dpi));
        int pixelHeight = Math.Max(1, (int)Math.Round(dipHeight * dpi));

        // The XAML surface always spans the whole video area; only mpv's
        // composition buffer may be capped (PiP). Passing the render size here
        // used to shrink the panel and leave part of the window uncovered.
        VideoHost.SetLayoutSize(pixelWidth, pixelHeight, dpi);

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
            else if (_lastCompositionSize != (pixelWidth, pixelHeight) &&
                _player.GetPropertyFlag("pause"))
            {
                // A paused mpv does not present new frames on its own; a
                // relative zero seek forces one exact refresh so a resize while
                // paused never leaves the surface stale/black.
                _player.CommandAsync("seek", "0", "relative", "exact");
            }
        }

        _lastCompositionSize = (pixelWidth, pixelHeight);

        // Keep the WinUI content island flush with the client area; after a
        // fullscreen switch Windows/WinUI leaves a 1 px row above the video.
        WindowInterop.CoverClientArea(this);
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
        TitleBar.IsPinned = _settings.AlwaysOnTop;

        if (InMiniMode)
        {
            SetTopmost(_settings.AlwaysOnTop);
            ShowOsd(_settings.AlwaysOnTop ? Loc.T("osd.miniOnTopOn") : Loc.T("osd.miniOnTopOff"));
        }
        else if (IsFullscreen)
        {
            ShowOsd(Loc.T("osd.fullscreenOnTop"));
        }
        else
        {
            SetTopmost(_settings.AlwaysOnTop);
        }

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
            Title = Loc.T("icons.title"),
            Content = new ScrollViewer { Content = panel, Height = 420 },
            CloseButtonText = Loc.T("dialogs.close"),
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void ShowWindowMenu()
    {
        var flyout = new MenuFlyout();
        TrackChromeFlyout(flyout);
        AddItem(Loc.T("icons.newWindow"), NewWindow);
        AddItem(Loc.T("icons.openInNew"), () => _ = OpenInNewWindowAsync());
        AddItem(Loc.T("icons.closeWindow"), CloseCurrentWindow);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem("0.5×", () => ResizeToScale(0.5));
        AddItem("1×", () => ResizeToScale(1.0));
        AddItem("2×", () => ResizeToScale(2.0));
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(Loc.T("icons.fit"), FitToScreen);
        AddItem(Loc.T("icons.pip"), TogglePictureInPicture);
        AddItem(Loc.T("icons.fullscreen"), EnterFullscreenFromMenu);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddItem(_settings.AlwaysOnTop ? Loc.T("icons.unpin") : Loc.T("icons.pin"), TogglePin);
        flyout.ShowAt(TitleBar);

        void AddItem(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }
    }

    /// <summary>PiP has no default chord; the title-bar menu is one of its entry points.</summary>
    private void TogglePictureInPicture() =>
        RequestMode(_mode.Current == PlayerMode.PiP ? PlayerMode.Normal : PlayerMode.PiP);

    /// <summary>
    /// 0.5× / 1× / 2×: the window is sized from the actual video resolution
    /// (mpv's display dw/dh when present, else the decoded size), so 1× shows
    /// every video pixel at a physical pixel. The helper returns DIP and keeps
    /// the window inside the current monitor's work area.
    /// </summary>
    private void ResizeToScale(double scale)
    {
        double dpi = DisplayScale;
        RectInt32 workArea = CurrentWorkArea();
        (int videoWidth, int videoHeight) = DisplayVideoSize();
        DipSize? size = WindowSizing.ScaledVideoWindowSize(
            videoWidth, videoHeight, scale, dpi, workArea.Width, workArea.Height);
        if (size is null)
        {
            return;
        }

        (int width, int height) = WindowSizing.ToPhysical(size.Value, dpi);
        width = Math.Clamp(width, 1, workArea.Width);
        height = Math.Clamp(height, 1, workArea.Height);

        // An explicit size choice must survive the first-video auto sizing.
        _userResizedWindow = true;
        AppWindow.Resize(new SizeInt32(width, height));
        ClampWindowIntoWorkArea(workArea, width, height);
    }

    /// <summary>
    /// Fits the window to the largest area with the video's display aspect ratio
    /// (not a hardcoded 16:9 box), centered on the current monitor with the same
    /// 96 px breathing room as before. No video keeps the 1280x720 fallback.
    /// </summary>
    private void FitToScreen()
    {
        double dpi = DisplayScale;
        RectInt32 workArea = CurrentWorkArea();
        const int margin = 96;
        int availableWidth = Math.Max(1, workArea.Width - margin);
        int availableHeight = Math.Max(1, workArea.Height - margin);
        (int videoWidth, int videoHeight) = DisplayVideoSize();
        DipSize? size = WindowSizing.FitVideoWindowSize(
            videoWidth, videoHeight, dpi, availableWidth, availableHeight);
        if (size is null)
        {
            return;
        }

        (int width, int height) = WindowSizing.ToPhysical(size.Value, dpi);
        width = Math.Clamp(width, 1, availableWidth);
        height = Math.Clamp(height, 1, availableHeight);

        _userResizedWindow = true;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height));
    }

    /// <summary>Work area of the monitor the window currently sits on.</summary>
    private RectInt32 CurrentWorkArea() =>
        DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

    /// <summary>
    /// Video display size: mpv's aspect-corrected dw/dh (anamorphic sources are
    /// widened) with the dwidth/dheight and decoded-size fallbacks. (0,0) when
    /// no video is loaded; the WindowSizing helpers then use 1280x720.
    /// </summary>
    private (int Width, int Height) DisplayVideoSize()
    {
        int width = (int)(_player?.GetPropertyInt64("video-params/dw") ?? 0);
        int height = (int)(_player?.GetPropertyInt64("video-params/dh") ?? 0);
        if (width <= 0)
        {
            width = (int)(_player?.GetPropertyInt64("dwidth") ?? 0);
        }

        if (height <= 0)
        {
            height = (int)(_player?.GetPropertyInt64("dheight") ?? 0);
        }

        if (width <= 0 || height <= 0)
        {
            return (_videoWidth, _videoHeight);
        }

        return (width, height);
    }

    /// <summary>Keeps a just-resized window on screen without changing its size.</summary>
    private void ClampWindowIntoWorkArea(RectInt32 workArea, int width, int height)
    {
        PointInt32 position = AppWindow.Position;
        int x = Math.Clamp(
            position.X, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - width));
        int y = Math.Clamp(
            position.Y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - height));
        if (x != position.X || y != position.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }
    }

    /// <summary>
    /// Restores a maximized window under the cursor the way a native title-bar
    /// drag does: the restored frame keeps its normal size and the cursor keeps
    /// the same relative grab point it had on the maximized frame, so the window
    /// does not jump away from the pointer. The content drag is then rebased on
    /// the restored frame, so the same gesture continues and the edge magnet can
    /// engage on the very next move. Returns true when the window was maximized
    /// and has been restored.
    /// </summary>
    private bool TryRestoreFromMaximized(int cursorX, int cursorY)
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Maximized } presenter)
        {
            return false;
        }

        PointInt32 position = AppWindow.Position;
        SizeInt32 size = AppWindow.Size;
        (int restoreWidth, int restoreHeight) = RestoreFrameSize();
        WindowRect? target = WindowSizing.RestoreUnderCursor(
            new WindowRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height),
            new WindowRect(0, 0, restoreWidth, restoreHeight),
            _dragStartCursorX,
            _dragStartCursorY,
            cursorX,
            cursorY);
        if (target is null)
        {
            return false;
        }

        presenter.Restore();
        AppWindow.MoveAndResize(new RectInt32(
            target.Value.Left, target.Value.Top, target.Value.Width, target.Value.Height));

        // The normal frame can be larger than the monitor under the cursor
        // (restored from a bigger display): keep it on screen. The clamped
        // position is computed from the target instead of being read back, so
        // the rebase never races the presenter/MoveAndResize bookkeeping.
        RectInt32 workArea = CurrentWorkArea();
        int placedX = Math.Clamp(
            target.Value.Left,
            workArea.X,
            Math.Max(workArea.X, workArea.X + workArea.Width - target.Value.Width));
        int placedY = Math.Clamp(
            target.Value.Top,
            workArea.Y,
            Math.Max(workArea.Y, workArea.Y + workArea.Height - target.Value.Height));
        if (placedX != target.Value.Left || placedY != target.Value.Top)
        {
            AppWindow.Move(new PointInt32(placedX, placedY));
        }

        _windowStartX = placedX;
        _windowStartY = placedY;
        _dragStartCursorX = cursorX;
        _dragStartCursorY = cursorY;
        _snapSession.Reset();
        ApplySnapFrame(false);
        Trace("window drag: restored from maximized under cursor");
        return true;
    }

    /// <summary>
    /// Size a maximized window restores to: the platform's normal placement
    /// first (exactly what a title-bar drag would restore to), then the saved
    /// window size, raised to the minimum window size.
    /// </summary>
    private (int Width, int Height) RestoreFrameSize()
    {
        int width = 0;
        int height = 0;
        if (TryGetNormalPlacement(out RectInt32 normal))
        {
            width = normal.Width;
            height = normal.Height;
        }

        if (width <= 0 || height <= 0)
        {
            width = _settings.WindowWidth;
            height = _settings.WindowHeight;
        }

        double scale = DisplayScale;
        if (width <= 0)
        {
            width = (int)Math.Round(WindowSizing.FallbackVideoWidth * scale);
        }

        if (height <= 0)
        {
            height = (int)Math.Round(WindowSizing.FallbackVideoHeight * scale);
        }

        int minimumWidth = (int)Math.Round(WindowSizing.MinimumWidthDip * scale);
        int minimumHeight = (int)Math.Round(WindowSizing.MinimumHeightDip * scale);
        return (Math.Max(minimumWidth, width), Math.Max(minimumHeight, height));
    }

    /// <summary>
    /// Normal placement of the window in physical pixels. A maximized window
    /// reports the frame it will restore to, which is the same rectangle the
    /// native title-bar drag uses.
    /// </summary>
    private bool TryGetNormalPlacement(out RectInt32 normal)
    {
        normal = default;
        try
        {
            nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            var placement = new WindowPlacement
            {
                Length = System.Runtime.InteropServices.Marshal.SizeOf<WindowPlacement>(),
            };
            if (!GetWindowPlacement(hwnd, ref placement))
            {
                return false;
            }

            int width = placement.NormalPosition.Right - placement.NormalPosition.Left;
            int height = placement.NormalPosition.Bottom - placement.NormalPosition.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            normal = new RectInt32(
                placement.NormalPosition.Left, placement.NormalPosition.Top, width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public PlacementPoint MinPosition;
        public PlacementPoint MaxPosition;
        public PlacementRect NormalPosition;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PlacementPoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PlacementRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(nint hWnd, ref WindowPlacement placement);

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
            if (!InMiniMode && !IsFullscreen)
            {
                SizeInt32 size = AppWindow.Size;
                PointInt32 position = AppWindow.Position;
                _settings.WindowWidth = size.Width;
                _settings.WindowHeight = size.Height;
                _settings.WindowX = position.X;
                _settings.WindowY = position.Y;
            }

            _settings.SidebarWidth = _sidebar.Width;
            App.SaveSettings();
        }
        catch (Exception ex)
        {
            Trace("save settings failed: " + ex);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _closeClock.Restart();
        CloseStage("enter");

        // Start the watchdog before touching anything that can block: if any
        // step below hangs (the 1.2.6 hang was mpv teardown racing a dead GPU),
        // the process ends instead of leaving an unresponsive window.
        StartCloseWatchdog();

        _closed = true;
        _deviceRecoveryTimer?.Stop();

        // Make the close feel instant even if teardown needs a moment.
        try
        {
            AppWindow.Hide();
        }
        catch
        {
            // The window may already be gone.
        }

        CloseStage("hidden");

        AppWindow.Changed -= OnAppWindowChanged;
        App.SettingsChanged -= OnSharedSettingsChanged;
        _resizeTimer.Stop();
        _autoHideTimer.Stop();
        _statsTimer?.Stop();
        _cursorHideTimer?.Stop();
        _settingsSaveTimer?.Stop();
        SidebarHost.Log = null;
        WindowInterop.SetCursorVisible(true);
        ApplyProtectedCursor(false);
        CloseStage("timers stopped");

        // Exactly one settings write in the whole close path, and it happens
        // before any teardown call that could block.
        _settings.SidebarWidth = _sidebar.Width;
        SaveSettings();
        CloseStage("settings saved");

        // Stop GPU work on a (possibly dead) device before anything is torn
        // down, and pause so the video output is not producing frames while
        // its swapchain is detached.
        ChromeBlur.SetActive(false);
        PausePlaybackForClose();
        CloseStage("playback paused, blur off");

        if (_pipWindow is { } pip)
        {
            _pipWindow = null;
            try
            {
                pip.DetachSwapChain();
                pip.CloseFromMain();
            }
            catch
            {
                // Window is going away.
            }
        }

        CloseStage("pip closed");

        OnClosedStage3();
        CloseStage("history, timers, player events closed");

        DisposeStage5();
        CloseStage("thumbnails disposed");

        // The blur worker may be inside mpv screenshot-raw right now; join it
        // before the player handle is destroyed (known hang/crash source).
        ChromeBlur.Shutdown();
        CloseStage("blur worker stopped");

        WindowInterop.Detach(this);
        CloseStage("interop detached");

        VideoHost.Detach();
        _swapChain = 0;
        CloseStage("video surface detached");

        _player?.Dispose();
        _player = null;
        CloseStage("player disposed");

        CompleteClose();
    }

    private void CloseStage(string stage) =>
        Trace($"[close +{_closeClock.ElapsedMilliseconds} ms] {stage}");

    private void StartCloseWatchdog()
    {
        var cts = new CancellationTokenSource();
        _closeWatchdog = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CloseWatchdogTimeout, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (Volatile.Read(ref _closeCompleted))
            {
                return;
            }

            Trace($"[close +{_closeClock.ElapsedMilliseconds} ms] WATCHDOG: teardown blocked, terminating process");
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch
            {
                Environment.Exit(0);
            }
        });
    }

    private void CompleteClose()
    {
        Volatile.Write(ref _closeCompleted, true);
        _closeWatchdog?.Cancel();
        _closeWatchdog = null;
        CloseStage("done");
    }

    private void PausePlaybackForClose()
    {
        try
        {
            // Async: queueing the pause never blocks the UI thread, and mpv
            // stops presenting frames before Dispose joins the event thread.
            _player?.CommandAsync("set", "pause", "yes");
        }
        catch (Exception ex)
        {
            Trace("[close] pause failed: " + ex.Message);
        }
    }
}

