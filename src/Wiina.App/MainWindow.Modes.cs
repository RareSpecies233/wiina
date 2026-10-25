using System.Linq;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Wiina.App.Interop;
using Wiina.Core.Models;
using Wiina.App.Localization;
using Wiina.Core.State;
using Wiina.Core.Text;

namespace Wiina.App;

/// <summary>
/// Stage 6: explicit Normal / Fullscreen / PiP / Mini mode machine, the
/// self-drawn PiP window, mini/music mode, shared-settings refresh and
/// multi-window / media-key glue. The pure rules live in
/// <see cref="ModeStateMachine"/>, <see cref="PipGeometry"/>,
/// <see cref="MiniPlayerLayout"/> and <see cref="WindowCoordinator"/>.
/// </summary>
public sealed partial class MainWindow
{
    private readonly ModeStateMachine _mode = new();
    private readonly IReadOnlyList<string> _startupPaths;
    private RectInt32? _normalFrame;
    private bool _modeResizedWindow;
    private PipWindow? _pipWindow;
    private bool _closingPipFromMain;
    private string? _artist;
    private string? _album;
    private bool _hasAlbumArt;
    private int _miniDragWindowX;
    private int _miniDragWindowY;
    private int _miniDragCursorX;
    private int _miniDragCursorY;

    /// <summary>
    /// Background the root grid had before mini mode pinned an opaque,
    /// theme-matched base. Restored on exit.
    /// </summary>
    private Brush? _miniBackgroundBackup;

    /// <summary>Stable id used by the app-level window registry.</summary>
    public string WindowId { get; } = Guid.NewGuid().ToString("N");

    public PlayerMode CurrentMode => _mode.Current;

    private bool InMiniMode => _mode.Current == PlayerMode.Mini;

    private bool InPipMode => _mode.Current == PlayerMode.PiP;

    /// <summary>Window title used for the task bar / registry.</summary>
    public string TitleText => string.IsNullOrWhiteSpace(_currentTitle) ? "Wiina" : _currentTitle;

    private void InitializeStage6()
    {
        App.SettingsChanged += OnSharedSettingsChanged;
        _mode.Changed += OnModeChanged;

        MiniPlayer.SetAnimationsEnabled(AnimationsEnabled);
        MiniPlayer.PlayPauseRequested += (_, _) => TogglePlayPause();
        MiniPlayer.PreviousRequested += (_, _) => PlaylistPrevious();
        MiniPlayer.NextRequested += (_, _) => PlaylistNext();
        MiniPlayer.RestoreRequested += (_, _) => RequestMode(PlayerMode.Normal);
        MiniPlayer.MuteToggleRequested += (_, _) => ToggleMute();
        MiniPlayer.SeekRequested += seconds => SeekAbsolute(seconds);
        MiniPlayer.VolumeRequested += SetVolume;
        MiniPlayer.PlaylistExpandedChanged += OnMiniPlaylistExpanded;
        MiniPlayer.PlaylistItemRequested += PlayPlaylistIndex;
        MiniPlayer.DragStarted += (_, _) => BeginMiniDrag();
        MiniPlayer.DragMoved += _ => MoveMiniDrag();
        MiniPlayer.DragEnded += (_, _) => SaveSettings();

        _oscVm.PropertyChanged += (_, _) => UpdateMiniPlayerState();
        UpdateOsdPlacement();
    }

    // -------------------------------------------------------------- layouts

    /// <summary>
    /// Mini mode: compact window, no titlebar/OSC/sidebar, mini overlay on top
    /// of the still-running video surface (album art renders underneath).
    /// </summary>
    private void ApplyMiniLayout()
    {
        _compact = true;
        SidebarLeading.CollapseInstant();
        SidebarTrailing.CollapseInstant();
        _oscState.IsSuppressed = true;
        _chromeShown = false;
        _autoHideTimer.Stop();

        TitleBar.Visibility = Visibility.Visible;
        SetTitleBarVisible(false);
        OscTop.Visibility = Visibility.Collapsed;
        OscBottom.Visibility = Visibility.Collapsed;
        OscFloating.Visibility = Visibility.Collapsed;
        OscCanvas.Visibility = Visibility.Collapsed;

        Grid.SetRow(VideoFrame, 0);
        Grid.SetRowSpan(VideoFrame, 3);
        VideoFrame.Margin = new Thickness(0);

        MiniPlayer.SetAnimationsEnabled(AnimationsEnabled);
        MiniPlayer.Visibility = Visibility.Visible;
        UpdateMiniPlayerState();

        TimePreview.Visibility = Visibility.Collapsed;
        ThumbnailPeek.Hide();
        UpdateFullscreenStatus();
        UpdateOsdPlacement();
    }

    /// <summary>
    /// PiP mode keeps the main window chrome (titlebar, restore buttons) but
    /// hides the transport overlays; the video surface lives in the PiP window.
    /// </summary>
    private void ApplyPipMainLayout()
    {
        SidebarLeading.CollapseInstant();
        SidebarTrailing.CollapseInstant();
        _oscState.IsSuppressed = true;
        _chromeShown = true;

        OscTop.Visibility = Visibility.Collapsed;
        OscBottom.Visibility = Visibility.Collapsed;
        OscFloating.Visibility = Visibility.Collapsed;
        OscCanvas.Visibility = Visibility.Collapsed;

        SetTitleBarVisible(true);
        Grid.SetRow(VideoFrame, 0);
        Grid.SetRowSpan(VideoFrame, 3);
        VideoFrame.Margin = new Thickness(0);

        TimePreview.Visibility = Visibility.Collapsed;
        ThumbnailPeek.Hide();
        UpdateFullscreenStatus();
        UpdateOsdPlacement();
    }

    // ------------------------------------------------------------ transitions

    /// <summary>
    /// The single entry point for every mode change. The state machine owns
    /// mutual exclusion and rollback; <see cref="ApplyMode"/> does the WinUI
    /// presenter/window work.
    /// </summary>
    private void RequestMode(PlayerMode target)
    {
        if (target == _mode.Current)
        {
            return;
        }

        Trace($"mode request {_mode.Current} -> {target}");
        ModeTransitionResult result = _mode.Request(target, ApplyMode);
        if (!result.Success)
        {
            Trace($"mode rejected {result.From} -> {result.To}: {result.Reason}");
            if (result.Reason is ModeTransitionReason.ApplyFailed or ModeTransitionReason.RolledBack)
            {
                ShowOsd(Loc.T("modes.switchFailed"), ModeLabel(target));
            }

            RefreshModeChrome();
            return;
        }

        RefreshModeChrome();
        ApplyVideoLayout();
        DispatcherQueue.TryEnqueue(ApplyVideoLayout);
        DispatcherQueue.TryEnqueue(() => RootGrid.Focus(FocusState.Programmatic));
        SaveSettings();
        if (target != PlayerMode.Normal)
        {
            ShowOsd(ModeLabel(target));
        }
    }

    private bool ApplyMode(PlayerMode target)
    {
        switch (target)
        {
            case PlayerMode.Normal:
                ExitFullscreenPresenter();
                RestoreVideoToMain();
                HideMiniPlayer();
                SetTopmost(_settings.AlwaysOnTop);
                RestoreNormalFrame();
                RestoreMainAfterPip();
                return true;

            case PlayerMode.Fullscreen:
                HideMiniPlayer();
                RestoreVideoToMain();
                CaptureNormalFrame();
                SetTopmost(false);
                // The snapped frame must not leak into fullscreen: fullscreen
                // rebuilds the DWM frame and disables the edge magnet.
                _snapSession.Reset();
                _snapFrameEngaged = false;
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    // The extended titlebar keeps a caption hit region alive even
                    // while its XAML is transparent, so dragging the top of the
                    // content area starts the native move loop and drops
                    // fullscreen. Handing the caption back to the system removes
                    // the drag region (the system titlebar paints nothing in
                    // fullscreen) and is restored by ExitFullscreenPresenter.
                    SetTitleBar(null);
                    // Belt and braces: also clear the native caption region so
                    // no hit test can start the OS move loop in fullscreen.
                    WindowInterop.SetCaptionDragEnabled(this, false);
                    // Do not leave keyboard focus on a chrome button: Space and
                    // Enter must act on the player, not on the last clicked OSC
                    // button.
                    RootGrid.Focus(FocusState.Programmatic);
                }

                return AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

            case PlayerMode.PiP:
                ExitFullscreenPresenter();
                HideMiniPlayer();
                return EnterPipInternal();

            case PlayerMode.Mini:
                ExitFullscreenPresenter();
                RestoreVideoToMain();
                return EnterMiniInternal();
        }

        return false;
    }

    private void OnModeChanged(object? sender, ModeTransitionResult result)
    {
        Trace($"mode {result.From} -> {result.To} ok={result.Success} reason={result.Reason}");
        UpdateFullscreenStatus();
        UpdateOsdPlacement();
    }

    private static string ModeLabel(PlayerMode mode) => mode switch
    {
        PlayerMode.Fullscreen => Loc.T("modes.fullscreen"),
        PlayerMode.PiP => Loc.T("modes.pip"),
        PlayerMode.Mini => Loc.T("modes.mini"),
        _ => Loc.T("modes.normal"),
    };

    private void RefreshModeChrome()
    {
        bool mini = InMiniMode;
        bool pip = InPipMode;

        MiniPlayer.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        PipPlaceholder.Visibility = pip ? Visibility.Visible : Visibility.Collapsed;

        if (mini)
        {
            SetTitleBarVisible(false);
        }
        else if (!IsFullscreen)
        {
            SetTitleBarVisible(_chromeShown);
        }

        ApplyLayout();
        UpdateOsdPlacement();
        UpdateFullscreenStatus();
        // Mode switches rebuild the DWM frame; re-assert the fullscreen/snapped
        // style after the presenter state has settled (v1.2.7 fullscreen ring).
        ApplyWindowFrame();

        if (pip)
        {
            PipPlaceholderTitle.Text = _currentTitle ?? Loc.T("pip.placeholder");
            PipPlaceholderSubtitle.Text = Loc.T("pip.placeholderSubtitle");
        }
    }

    // -------------------------------------------------------------- fullscreen

    private void EnterFullscreenFromMenu() =>
        RequestMode(IsFullscreen ? PlayerMode.Normal : PlayerMode.Fullscreen);

    private void ExitFullscreenPresenter()
    {
        if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        // Restore the custom titlebar drag region that entering fullscreen
        // deliberately removed (see ApplyMode).
        SetTitleBar(TitleBar);
        SetTopmost(_settings.AlwaysOnTop);
        SetTitleBarVisible(_chromeShown);
    }

    // -------------------------------------------------------------------- PiP

    private bool EnterPipInternal()
    {
        if (_pipWindow is not null)
        {
            return true;
        }

        if (_swapChain == 0 || _player is not { IsRunning: true })
        {
            ShowOsd(Loc.T("modes.switchFailed"), Loc.T("pip.unavailable"));
            return false;
        }

        DisplayArea area = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Nearest);
        RectInt32 workArea = area.WorkArea;
        SizeInt32 size = AppWindow.Size;
        PointInt32 position = AppWindow.Position;
        var mainFrame = new RectInt32(position.X, position.Y, size.Width, size.Height);

        var pip = new PipWindow(
            workArea,
            mainFrame,
            _settings.PipWidth,
            _settings.PipHeight,
            RootGrid.XamlRoot?.RasterizationScale ?? 1.0);

        pip.RestoreRequested += (_, _) =>
            DispatcherQueue.TryEnqueue(() => RequestMode(PlayerMode.Normal));
        pip.UserClosed += (_, _) =>
            DispatcherQueue.TryEnqueue(() => RequestMode(PlayerMode.Normal));
        pip.PlayPauseRequested += (_, _) => TogglePlayPause();
        pip.BufferSizeChanged += OnPipBufferSizeChanged;
        pip.Closed += (_, _) => OnPipWindowClosed(pip);
        pip.SetTitle(_currentTitle);
        pip.SetPlaying(_oscVm.IsPlaying);

        _pipWindow = pip;

        // Hand the same composition swapchain to the PiP panel. Detaching the
        // main panel first guarantees exactly one presenter owns it.
        VideoHost.Detach();
        pip.AttachSwapChain(_swapChain);
        pip.Activate();

        // PiP is meant to get the player out of the way. Minimize the main
        // window after the PiP window is visible; it is restored on exit.
        MinimizeMainForPip();

        PipPlaceholder.Visibility = Visibility.Visible;
        PipPlaceholderTitle.Text = _currentTitle ?? Loc.T("pip.placeholder");
        return true;
    }

    private void RestoreVideoToMain()
    {
        if (_pipWindow is { } pip)
        {
            _pipWindow = null;
            _closingPipFromMain = true;
            try
            {
                pip.DetachSwapChain();
                pip.CloseFromMain();
            }
            catch (Exception ex)
            {
                Trace("close pip failed: " + ex);
            }
            finally
            {
                _closingPipFromMain = false;
            }
        }

        PipPlaceholder.Visibility = Visibility.Collapsed;

        if (_swapChain != 0)
        {
            try
            {
                VideoHost.Attach(_swapChain);
                ApplyVideoLayout();
            }
            catch (Exception ex)
            {
                Trace("restore swapchain failed: " + ex);
            }
        }

        // Any mode that leaves PiP must bring the minimized main window back.
        RestoreMainAfterPip();
    }

    private void OnPipWindowClosed(PipWindow pip)
    {
        if (!ReferenceEquals(_pipWindow, pip))
        {
            return;
        }

        _pipWindow = null;
        PipPlaceholder.Visibility = Visibility.Collapsed;

        if (_swapChain != 0)
        {
            try
            {
                VideoHost.Attach(_swapChain);
                ApplyVideoLayout();
            }
            catch (Exception ex)
            {
                Trace("pip closed reattach failed: " + ex);
            }
        }

        // Closing PiP with its X restores the main window instead of leaving
        // it minimized with no visible video surface.
        RestoreMainAfterPip();

        if (!_closingPipFromMain && _mode.Current == PlayerMode.PiP)
        {
            DispatcherQueue.TryEnqueue(() => RequestMode(PlayerMode.Normal));
        }
    }

    /// <summary>
    /// PiP reports the <em>layout</em> pixel size of its video surface (full
    /// window). The mpv composition buffer uses exactly the same size so the
    /// mini window stays as sharp as the main window instead of being upscaled
    /// by the compositor (issue 7).
    /// </summary>
    private void OnPipBufferSizeChanged(int pixelWidth, int pixelHeight, double scale)
    {
        double dpi = scale > 0.1 ? scale : 1.0;
        (int widthDip, int heightDip) = PipGeometry.Clamp(
            (int)Math.Round(pixelWidth / dpi),
            (int)Math.Round(pixelHeight / dpi));
        _settings.PipWidth = widthDip;
        _settings.PipHeight = heightDip;
        if (_player is not { IsRunning: true })
        {
            return;
        }

        (int renderWidth, int renderHeight) = PipGeometry.RenderSize(pixelWidth, pixelHeight);
        _player.TrySetPropertyString(
            "d3d11-composition-size",
            $"{renderWidth}x{renderHeight}");

        // A paused mpv does not present new frames on its own; a relative
        // zero seek forces one exact refresh so rebinding never stays black.
        if (_player.GetPropertyFlag("pause"))
        {
            _player.CommandAsync("seek", "0", "relative", "exact");
        }
    }

    private void OnPipRestoreClick(object sender, RoutedEventArgs e) =>
        RequestMode(PlayerMode.Normal);

    private void MinimizeMainForPip()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State != OverlappedPresenterState.Minimized)
        {
            presenter.Minimize();
        }
    }

    private void RestoreMainAfterPip()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
            Activate();
        }
    }

    // ------------------------------------------------------------------- mini

    private bool _miniBackdropSuspended;

    /// <summary>
    /// Mini mode replaces the window acrylic with Mica. The acrylic would only
    /// add compositor work under the covered surface (small-window reference
    /// doc §4), but dropping the backdrop entirely is not an option either:
    /// the XAML island does not cover the non-client frame band, so a window
    /// with no backdrop paints that band black — which is exactly how the
    /// light-theme "black mini window" report looked. Mica is theme-tinted,
    /// DWM-composited and cheap. The previous backdrop is restored on exit.
    /// </summary>
    private void SetMiniBackdropSuspended(bool suspended)
    {
        if (_miniBackdropSuspended == suspended)
        {
            return;
        }

        _miniBackdropSuspended = suspended;
        try
        {
            if (suspended)
            {
                // Mini chrome is forced dark, so the window backdrop is dropped
                // entirely: Mica follows the app theme and would paint a light
                // rim/band around the dark mini window in the light theme. A
                // null backdrop paints that band black, matching the dark base.
                SystemBackdrop = null;
            }
            else
            {
                TrySetBackdrop();
            }
        }
        catch
        {
            // Backdrop is cosmetic; never block a mode switch. Fall back to the
            // normal window backdrop if Mica is unavailable on this system.
            try
            {
                if (!suspended)
                {
                    TrySetBackdrop();
                }
            }
            catch
            {
                // Ignore: still cosmetic.
            }
        }
    }

    private bool EnterMiniInternal()
    {
        CaptureNormalFrame();
        SetTopmost(true);
        SetMiniBackdropSuspended(true);
        ResizeWindowDip(MiniPlayerLayout.DefaultWidth, MiniPlayerLayout.DefaultHeight);
        _modeResizedWindow = true;

        MiniPlayer.Visibility = Visibility.Visible;
        // Mini chrome is always dark, whatever the app theme is. A light mini
        // window beside the video looked wrong and left dark-on-light text over
        // the cover, which was the "浅色小窗对比不对" report. Forcing the element
        // theme makes every ThemeResource in MiniPlayerView.xaml resolve to the
        // Dark token set (dark panels + light text, like IINA's mini player).
        // Restored in HideMiniPlayer; normal mode, PiP and Settings are
        // unaffected because only this subtree is themed.
        MiniPlayer.RequestedTheme = ElementTheme.Dark;
        MiniPlayer.SetAnimationsEnabled(AnimationsEnabled);
        MiniPlayer.ResetControls();
        // The normal root is transparent: the window backdrop paints it. The
        // backdrop is suspended in mini mode, so any transparent pixel would
        // expose the black mpv swap chain (the light-theme "black mini window"
        // report). Pin the dark mini base that pairs with the forced dark
        // element theme above.
        _miniBackgroundBackup = RootGrid.Background;
        RootGrid.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x08, 0x09, 0x0A));
        // Mouse-only chrome: Tab must not park focus on a mini control, where
        // Space would press it instead of pausing. Restored on exit below.
        MiniPlayer.SetKeyboardMouseOnly(true);
        // The main-window chrome (titlebar buttons, OSC) is hidden behind the
        // mini overlay but still in the tab order; the recursive pass is cheap
        // and the preview handler remains the backstop for lazy containers.
        MakeMediaChromeKeyboardTransparent();
        MiniPlayer.SetPlaylistExpanded(_settings.MusicModeShowPlaylist);
        UpdateMiniPlayerState();
        UpdateMiniPlaylist();
        if (_settings.MusicModeShowPlaylist)
        {
            ResizeMiniForPlaylist(expanded: true);
        }

        // Diagnostics for the light-theme background report: records the theme
        // and the effective brush of every layer that can paint the window.
        Trace($"mini theme: {MiniPlayer.DescribeThemeSurfaces()} " +
            $"windowBg={DescribeBrush(RootGrid.Background)} element={MiniPlayer.ActualTheme}");

        // Mini mode is keyboard-driven (Space pauses); never leave focus on a
        // mini control after the mode switch or the player shortcuts stop.
        RootGrid.Focus(FocusState.Programmatic);
        return true;
    }

    private static string DescribeBrush(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.ToString(),
        LinearGradientBrush gradient => $"gradient({gradient.GradientStops.Count})",
        null => "null",
        _ => brush.GetType().Name,
    };

    private void HideMiniPlayer()
    {
        if (MiniPlayer.Visibility != Visibility.Visible)
        {
            return;
        }

        MiniPlayer.Visibility = Visibility.Collapsed;
        // Mini forced a dark element theme (see EnterMiniInternal); hand the
        // subtree back to the app theme so a later normal-mode overlay and the
        // settings windows are unaffected.
        MiniPlayer.RequestedTheme = ElementTheme.Default;
        MiniPlayer.SetKeyboardMouseOnly(false);
        MiniPlayer.HideVolumeBubble();
        MiniPlayer.SetPlaylistExpanded(false);
        MiniPlayer.HideControls();
        MiniPlayer.ResetControls();
        if (_miniBackgroundBackup is not null)
        {
            RootGrid.Background = _miniBackgroundBackup;
            _miniBackgroundBackup = null;
        }

        SetMiniBackdropSuspended(false);
        // The restored normal frame is not edge-snapped: drop the magnet and
        // hand the normal DWM frame back.
        _snapSession.Reset();
        ApplySnapFrame(false);
        RestoreTopmostFromSettings();
        RestoreNormalFrame();
    }

    private void UpdateMiniPlayerState()
    {
        if (MiniPlayer.Visibility != Visibility.Visible)
        {
            return;
        }

        MiniPlayer.SetPlaybackState(_oscVm.IsPlaying, _oscVm.IsMuted, _oscVm.Volume);
        MiniPlayer.SetMedia(_currentTitle, _artist, _album, _hasAlbumArt);
        MiniPlayer.SetPosition(_oscVm.Position, _oscVm.Duration);
    }

    private void UpdateMiniPlaylist()
    {
        if (MiniPlayer.Visibility != Visibility.Visible)
        {
            return;
        }

        string[] names = _playlistItems
            .Select(item => item.DisplayName)
            .ToArray();
        MiniPlayer.SetPlaylist(names, _currentPlaylistIndex);
    }

    private void OnMiniPlaylistExpanded(bool expanded)
    {
        _settings.MusicModeShowPlaylist = expanded;
        if (!InMiniMode)
        {
            return;
        }

        ResizeMiniForPlaylist(expanded);
        ApplyVideoLayout();
        SaveSettings();
    }

    private void ResizeMiniForPlaylist(bool expanded)
    {
        double dpi = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        double widthDip = Math.Max(MiniPlayerLayout.MinWidth, RootGrid.ActualWidth);
        RectInt32 workArea = DisplayArea
            .GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary)
            .WorkArea;
        double maxHeightDip = (workArea.Height / dpi) - 32;
        double heightDip = Math.Min(
            MiniPlayerLayout.HeightWithPlaylist(widthDip, expanded),
            Math.Max(MiniPlayerLayout.MinHeight, maxHeightDip));
        ResizeWindowDip(widthDip, heightDip);
    }

    private void BeginMiniDrag()
    {
        if (!InMiniMode)
        {
            return;
        }

        WindowInterop.TryGetCursorPosition(out _miniDragCursorX, out _miniDragCursorY);
        _miniDragWindowX = AppWindow.Position.X;
        _miniDragWindowY = AppWindow.Position.Y;
        // Mini drag is a third drag path (issue 4) and shares the edge magnet.
        _snapSession.Reset();
    }

    private void MoveMiniDrag()
    {
        if (!InMiniMode ||
            !WindowInterop.TryGetCursorPosition(out int cursorX, out int cursorY))
        {
            return;
        }

        int x = _miniDragWindowX + (cursorX - _miniDragCursorX);
        int y = _miniDragWindowY + (cursorY - _miniDragCursorY);
        SizeInt32 size = AppWindow.Size;
        if (_settings.EdgeSnapping)
        {
            EdgeSnapResult snap = _snapSession.Apply(
                new WindowRect(x, y, x + size.Width, y + size.Height),
                WorkAreaUnderPointer(),
                DisplayScale,
                margins: SnapMargins());
            x = snap.Rect.Left;
            y = snap.Rect.Top;
            ApplySnapFrame(snap.IsSnapped);
        }
        else
        {
            _snapSession.Reset();
            ApplySnapFrame(false);
        }

        AppWindow.Move(new PointInt32(x, y));
    }

    private void UpdateAlbumArtState()
    {
        _hasAlbumArt = MediaModeAdvice.HasAlbumArt(_trackList);

        if (_mode.Current == PlayerMode.Mini &&
            MediaModeAdvice.HasRealVideo(_trackList))
        {
            RequestMode(PlayerMode.Normal);
        }
    }

    // ------------------------------------------------------------ shared state

    private void OnSharedSettingsChanged()
    {
        if (_closed)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
            {
                return;
            }

            ApplyTheme();
            _oscState.AutoHideEnabled = _settings.OscAutoHide;
            ApplyStage5Settings();
            if (!IsFullscreen)
            {
                SetTopmost(_settings.AlwaysOnTop);
            }

            ApplyLayout();
            UpdateMiniPlayerState();
            RebuildKeyMap();
            ApplyStage7Settings();
            ApplyStage8Settings();
            SyncQuickSettingsValues();
        });
    }

    // ------------------------------------------------------------- media keys

    /// <summary>SMTC / WM_APPCOMMAND routed to this window by the App.</summary>
    public void HandleMediaCommand(MediaCommand command, double position = 0)
    {
        switch (command)
        {
            case MediaCommand.Play:
                PlayOrPause(true);
                break;
            case MediaCommand.Pause:
                PlayOrPause(false);
                break;
            case MediaCommand.TogglePlayPause:
                TogglePlayPause();
                break;
            case MediaCommand.Stop:
                StopPlayback();
                break;
            case MediaCommand.Next:
                PlaylistNext();
                break;
            case MediaCommand.Previous:
                PlaylistPrevious();
                break;
            case MediaCommand.Seek:
                SeekAbsolute(position);
                break;
        }
    }

    /// <summary>Called when this window becomes the SMTC target.</summary>
    public void PushMediaStateToSession() => UpdateSmtc();

    private bool OnAppCommand(int command)
    {
        if (!_settings.MediaKeysEnabled || !App.IsMediaTarget(this))
        {
            return false;
        }

        switch (command)
        {
            case WindowInterop.AppCommandMediaPlayPause:
                TogglePlayPause();
                return true;
            case WindowInterop.AppCommandMediaPlay:
                PlayOrPause(true);
                return true;
            case WindowInterop.AppCommandMediaPause:
                PlayOrPause(false);
                return true;
            case WindowInterop.AppCommandMediaStop:
                StopPlayback();
                return true;
            case WindowInterop.AppCommandMediaNextTrack:
                PlaylistNext();
                return true;
            case WindowInterop.AppCommandMediaPreviousTrack:
                PlaylistPrevious();
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------- multi-window

    private void NewWindow() => App.CreateWindow();

    private void TogglePip() => RequestMode(
        _mode.Current == PlayerMode.PiP ? PlayerMode.Normal : PlayerMode.PiP);

    private void ToggleMini() => RequestMode(
        _mode.Current == PlayerMode.Mini ? PlayerMode.Normal : PlayerMode.Mini);

    private async Task OpenInNewWindowAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);

            IReadOnlyList<Windows.Storage.StorageFile> files =
                await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                App.OpenInNewWindow(files.Select(file => file.Path));
            }
        }
        catch (Exception ex)
        {
            Trace("open in new window failed: " + ex);
            ShowOsd(Loc.T("modes.newWindowFailed"), ex.Message);
        }
    }

    private void CloseCurrentWindow() => Close();

    // ------------------------------------------------------------- geometry

    private void CaptureNormalFrame()
    {
        if (_normalFrame is not null ||
            AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped)
        {
            return;
        }

        SizeInt32 size = AppWindow.Size;
        PointInt32 position = AppWindow.Position;
        _normalFrame = new RectInt32(position.X, position.Y, size.Width, size.Height);
    }

    private void RestoreNormalFrame()
    {
        if (_normalFrame is not { } frame)
        {
            _modeResizedWindow = false;
            return;
        }

        if (_modeResizedWindow)
        {
            AppWindow.MoveAndResize(frame);
        }

        _normalFrame = null;
        _modeResizedWindow = false;
    }

    private void ResizeWindowDip(double widthDip, double heightDip)
    {
        double dpi = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        AppWindow.Resize(new SizeInt32(
            Math.Max(1, (int)Math.Round(widthDip * dpi)),
            Math.Max(1, (int)Math.Round(heightDip * dpi))));
    }

    private void RestoreTopmostFromSettings()
    {
        if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
        {
            SetTopmost(_settings.AlwaysOnTop);
        }
    }

    private bool GetTopmost() =>
        AppWindow.Presenter is OverlappedPresenter presenter && presenter.IsAlwaysOnTop;

    private void SetTopmost(bool value)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = value;
        }
    }
}
