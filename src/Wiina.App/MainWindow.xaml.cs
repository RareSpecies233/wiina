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
using Wiina.Core.Osd;
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

    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherQueueTimer _resizeTimer;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly OscVisibilityController _oscState = new();
    private readonly OscViewModel _oscVm = new();
    private readonly SidebarState _sidebar = new();
    private AppSettings _settings;
    private MpvPlayer? _player;
    private nint _swapChain;
    private bool _started;
    private bool _compact;
    private bool _titleBarVisible = true;
    private bool _sidebarResizing;
    private bool _updatingSidebar;
    private bool _oscDragging;
    private bool _seeking;
    private bool _wasPlayingBeforeSeek;
    private bool _chromeShown = true;
    private bool _pointerOverChrome;
    private bool? _animationsEnabled;
    private DispatcherQueueTimer? _settingsSaveTimer;
    private Point _oscDragStart;
    private Point _oscOrigin;
    private IReadOnlyList<ChapterInfo> _chapters = Array.Empty<ChapterInfo>();
    private double _videoAspect = 16.0 / 9.0;
    private int _videoWidth;
    private int _videoHeight;
    private string? _currentAudioDevice;

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

        _autoHideTimer = DispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _autoHideTimer.IsRepeating = false;
        _autoHideTimer.Tick += OnAutoHideTick;

        OscFloating.DataContext = _oscVm;
        OscBottom.DataContext = _oscVm;
        OscTop.DataContext = _oscVm;
        TitleBar.SetOscDataContext(_oscVm);
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
        Closed += OnClosed;

        ApplySettings();
        InitializeStage3();
        InitializeSidebar();
        InitializeStage5();
    }

    private void ApplySettings()
    {
        ApplyTheme();
        ApplyLayout();
        _oscState.AutoHideEnabled = _settings.OscAutoHide;
        ApplyStage5Settings();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        }
    }

    private void SubscribeOsc()
    {
        _oscVm.SeekRequested += seconds => SeekAbsolute(seconds, showOsd: false);
        _oscVm.VolumeRequested += SetVolume;
        _oscVm.PlayPauseRequested += TogglePlayPause;
        _oscVm.MuteRequested += ToggleMute;
        _oscVm.PreviousRequested += PlaylistPrevious;
        _oscVm.NextRequested += PlaylistNext;
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
        timeline.PreviewRequested += (time, x) => ShowTimePreview(timeline, time, x);
        timeline.PreviewHidden += HideTimePreview;
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
        HideTimePreview();
        _autoHideTimer.Stop();
        if (_wasPlayingBeforeSeek)
        {
            SendPlayerCommand("set", "pause", "yes");
        }
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
        ShowOsd(OsdMessages.Seek(_oscVm.Position, _oscVm.Duration));
        ResetAutoHideTimer();
    }

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ShowChrome();
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point point = e.GetCurrentPoint(RootGrid).Position;
        _pointerOverChrome = IsOverChrome(point);

        if (!_chromeShown)
        {
            ShowChrome();
        }

        if (_pointerOverChrome || _oscDragging || _seeking)
        {
            _autoHideTimer.Stop();
        }
        else
        {
            ResetAutoHideTimer();
        }

        if (!_chromeShown)
        {
            // WinUI restores the default cursor on pointer moves.
            WindowInterop.SetCursorVisible(false);
        }
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Root PointerExited also fires when the pointer moves between child
        // elements; only a real window exit should hide the chrome.
        if (WindowInterop.IsCursorInsideWindow(this))
        {
            return;
        }

        _pointerOverChrome = false;
        _autoHideTimer.Stop();
        HideChrome(force: true);
    }

    private void OnAutoHideTick(DispatcherQueueTimer sender, object args)
    {
        _autoHideTimer.Stop();
        if (_pointerOverChrome || _oscDragging || _seeking)
        {
            return;
        }

        HideChrome();
    }

    private void OnChromePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _autoHideTimer.Stop();
        _oscState.Show();
        FadeChrome(1, hitTest: true);
        _autoHideTimer.Stop();
    }

    private void OnChromePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _autoHideTimer.Stop();
        if (_oscState.AutoHideEnabled)
        {
            _autoHideTimer.Start();
        }
    }

    private void ShowChrome()
    {
        if (!_chromeShown)
        {
            _chromeShown = true;
            _oscState.Show();
            _oscState.CompleteShow();
            FadeChrome(1, hitTest: true);
            WindowInterop.SetCursorVisible(true);
        }

        ResetAutoHideTimer();
    }

    private void HideChrome(bool force = false)
    {
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
        _autoHideTimer.Stop();
        FadeChrome(0, hitTest: false);
        WindowInterop.SetCursorVisible(false);
    }

    private void ResetAutoHideTimer()
    {
        _autoHideTimer.Stop();
        if (_settings.Layout == WindowLayoutMode.Docked ||
            !_oscState.AutoHideEnabled ||
            _oscDragging ||
            _seeking ||
            _sidebarResizing ||
            HasVisibleSidebar ||
            _pointerOverChrome)
        {
            return;
        }

        _autoHideTimer.Start();
    }

    private void FadeChrome(double opacity, bool hitTest)
    {
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
            foreach (FrameworkElement element in elements)
            {
                element.Opacity = opacity;
            }

            UpdateFullscreenStatus();
            UpdateOsdPlacement();
            return;
        }

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
            (Osd.IsPersistentShowing && Contains(Osd, point));
    }

    private bool Contains(FrameworkElement element, Point point)
    {
        if (element.Visibility != Visibility.Visible ||
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

    private void ShowTimePreview(TimelineControl timeline, double seconds, double x)
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

        _previewPointerX = ParsePreviewX(timeline, x);
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
        TimePreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point point = timeline.TransformToVisual(OverlayLayer).TransformPoint(
            new Point(x, timeline.ActualHeight / 2));
        double width = TimePreview.DesiredSize.Width;
        double height = TimePreview.DesiredSize.Height;
        double left = Math.Clamp(
            point.X - width / 2,
            0,
            Math.Max(0, OverlayLayer.ActualWidth - width));
        double top = point.Y - height - 8;
        if (top < 0)
        {
            top = point.Y + timeline.ActualHeight / 2 + 8;
        }

        top = Math.Clamp(top, 0, Math.Max(0, OverlayLayer.ActualHeight - height));
        TimePreview.Margin = new Thickness(left, top, 0, 0);

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

    private double ParsePreviewX(TimelineControl timeline, double x)
    {
        try
        {
            Point point = timeline.TransformToVisual(OverlayLayer).TransformPoint(
                new Point(x, timeline.ActualHeight / 2));
            return point.X;
        }
        catch
        {
            return x;
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

        x = Math.Clamp(x, 0, availableWidth - oscWidth);
        y = Math.Clamp(y, 0, availableHeight - oscHeight);

        // Snap the horizontal centre within +-5 px (spec §5.6).
        double centerX = x + oscWidth / 2;
        double snappedCenter = OscPositioning.SnapToCenter(centerX, availableWidth, 5);
        x = Math.Clamp(snappedCenter - oscWidth / 2, 0, availableWidth - oscWidth);

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

        Canvas.SetLeft(OscFloating, centerX - oscWidth / 2);
        Canvas.SetTop(OscFloating, centerY - oscHeight / 2);
    }

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
        var flyout = new MenuFlyout();

        void AddItem(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            flyout.Items.Add(item);
        }

        void AddSubItem(MenuFlyoutSubItem parent, string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            parent.Items.Add(item);
        }

        AddItem("布局：Edge-to-edge / Docked", ToggleLayout);

        var oscMenu = new MenuFlyoutSubItem { Text = "OSC 位置" };
        AddSubItem(oscMenu, "浮动", () => SetOscPosition(OscPosition.Floating));
        AddSubItem(oscMenu, "底部", () => SetOscPosition(OscPosition.Bottom));
        AddSubItem(oscMenu, "顶部", () => SetOscPosition(OscPosition.Top));
        flyout.Items.Add(oscMenu);

        AddItem("快速设置侧栏 (Ctrl+Shift+S)", ShowSettingsSidebar);
        AddItem("播放列表侧栏 (Ctrl+Shift+B)", ShowPlaylistSidebar);
        AddItem("章节侧栏", ShowChaptersSidebar);
        AddItem("插件侧栏（Stage 9）", ShowPluginsSidebar);

        var sideMenu = new MenuFlyoutSubItem { Text = "侧边栏位置" };
        AddSubItem(sideMenu, "左侧", () => MoveSidebarTo(SidebarSide.Leading));
        AddSubItem(sideMenu, "右侧", () => MoveSidebarTo(SidebarSide.Trailing));
        flyout.Items.Add(sideMenu);

        AddItem("主题", CycleTheme);
        AddItem("置顶", TogglePin);
        AddItem("技术信息 OSD", TogglePersistentOsd);

        var windowMenu = new MenuFlyoutSubItem { Text = "窗口尺寸" };
        AddSubItem(windowMenu, "0.5×", () => ResizeToScale(0.5));
        AddSubItem(windowMenu, "1×", () => ResizeToScale(1.0));
        AddSubItem(windowMenu, "2×", () => ResizeToScale(2.0));
        AddSubItem(windowMenu, "适合屏幕", FitToScreen);
        flyout.Items.Add(windowMenu);

        var icons = new MenuFlyoutItem { Text = "图标映射" };
        icons.Click += async (_, _) => await ShowIconDialogAsync();
        flyout.Items.Add(icons);

        AddItem("设置", ShowSettingsSidebar);

        if (sender is OscToolbar toolbar)
        {
            flyout.ShowAt(toolbar.MoreAnchor);
        }
        else
        {
            flyout.ShowAt(OscFloating);
        }
    }

    private void ToggleFullscreen()
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        SyncFullscreenState();
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            SetTitleBarVisible(false);
        }
        else
        {
            SetTitleBarVisible(_chromeShown);
        }

        DispatcherQueue.TryEnqueue(ApplyVideoLayout);
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
        bar.ExtendsContentIntoTitleBar = true;

        bool dark = RootGrid.ActualTheme != ElementTheme.Light;
        Color background = dark
            ? Color.FromArgb(0xCC, 0x10, 0x11, 0x14)
            : Color.FromArgb(0xCC, 0xF3, 0xF3, 0xF3);

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

    /// <summary>
    /// WinUI 3 extended titlebar visibility. Collapsing the native titlebar
    /// height also removes the system caption buttons (they are drawn by DWM
    /// above XAML, so fading the custom titlebar alone is not enough).
    /// </summary>
    private void SetTitleBarVisible(bool visible)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            visible = false;
        }

        _titleBarVisible = visible;
        AppWindow.TitleBar.PreferredHeightOption = visible
            ? TitleBarHeightOption.Standard
            : TitleBarHeightOption.Collapsed;
        TitleBar.Visibility = Visibility.Visible;
        OscTop.Margin = new Thickness(0, visible ? 40 : 0, 0, 0);
        UpdateSidebarMargins();
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
        bool showLeading = sidebarVisible && _sidebar.IsVisible(SidebarSide.Leading);
        bool showTrailing = sidebarVisible && _sidebar.IsVisible(SidebarSide.Trailing);

        // The floating OSC stays available while a sidebar is open (the sidebar
        // suppresses auto-hide rather than hiding the controls); it is kept
        // inside the free area so it never sits under the panel.
        OscFloating.Visibility = !docked && !_compact && !topOsc &&
            _settings.OscPosition == OscPosition.Floating
            ? Visibility.Visible
            : Visibility.Collapsed;
        OscFloating.SetAvailableWidth(RootGrid.ActualWidth);

        _oscState.IsSuppressed = sidebarVisible || _sidebarResizing;

        UpdateDockedInsets();
        OscCanvas.Margin = new Thickness(
            showLeading ? _sidebar.Width : 0,
            0,
            showTrailing ? _sidebar.Width : 0,
            0);

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

        RootGrid.Focus(FocusState.Programmatic);
        InitializeSmtc();
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
            _player.NumberPropertyChanged += OnNumberPropertyChanged;
            _player.FlagPropertyChanged += OnFlagPropertyChanged;
            _player.StringPropertyChanged += OnStringPropertyChanged;
            _player.FileLoaded += OnFileLoaded;
            _player.Start(width, height);
            _player.ObserveProperty("display-swapchain", MpvFormat.Int64);
            InitializePlayerStage3(_player);
            ApplyPersistedQuickSettings();

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
            ShowOsd("播放引擎启动失败", ex.Message);
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
        ToggleFullscreen();
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            args.Handled = true;
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            SyncFullscreenState();
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
        _autoHideTimer.Stop();
        _settingsSaveTimer?.Stop();
        _settings.SidebarWidth = _sidebar.Width;
        SidebarHost.Log = null;
        WindowInterop.SetCursorVisible(true);
        OnClosedStage3();
        DisposeStage5();
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
