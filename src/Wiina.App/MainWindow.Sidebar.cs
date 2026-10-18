using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Wiina.App.Controls;
using Wiina.Core.Filters;
using Wiina.App.Localization;
using Wiina.Core.Models;
using Wiina.Core.Osd;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Stage 4: the two sidebar slots, the quick-settings/playlist/chapter pages,
/// width drag, slide/fade animation and persistence. The pure rules live in
/// <see cref="SidebarState"/>; this file is the WinUI bridge.
/// </summary>
public sealed partial class MainWindow
{
    private SidebarType? VisibleSidebarType =>
        _sidebar.TypeFor(SidebarSide.Leading) ?? _sidebar.TypeFor(SidebarSide.Trailing);

    private bool HasVisibleSidebar => _sidebar.IsAnyVisible;

    private void InitializeSidebar()
    {
        SidebarHost.Log = message => Trace("sidebar-host: " + message);
        _sidebar.SetWidth(_settings.SidebarWidth, RootGrid.ActualWidth);
        _oscState.AutoHideEnabled = _settings.OscAutoHide;

        _settingsSaveTimer = DispatcherQueue.CreateTimer();
        _settingsSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _settingsSaveTimer.IsRepeating = false;
        _settingsSaveTimer.Tick += (_, _) => SaveSettings();

        WireSidebarHost(SidebarLeading);
        WireSidebarHost(SidebarTrailing);
        ApplySidebarSide();
    }

    private void WireSidebarHost(SidebarHost host)
    {
        host.CloseClicked += (_, _) => HideSidebar();
        host.TabRequested += OnSidebarTabRequested;
        host.WidthRequested += OnSidebarWidthRequested;
        host.WidthChangeCompleted += (_, _) =>
        {
            _sidebarResizing = false;
            _oscState.IsSuppressed = HasVisibleSidebar;
            SaveSidebarSettings();
            ApplyLayout();
        };

        host.QuickSettings.SettingChanged += OnQuickSettingChanged;
        host.QuickSettings.PageChanged += (_, page) =>
        {
            _settings.QuickSettingsPage = page;
            SyncQuickSettingsPage(page);
            ScheduleSettingsSave();
        };

        host.Playlist.AddRequested += (_, _) => _ = AddFilesToPlaylistAsync();
        host.Playlist.SaveRequested += (_, _) => _ = SavePlaylistPickerAsync();
        host.Playlist.PlayRequested += (_, index) => PlayPlaylistIndex(index);
        host.Playlist.RemoveRequested += (_, indexes) => RemovePlaylistIndexes(indexes);
        host.Playlist.MoveRequested += (_, move) => MovePlaylistItem(move.From, move.To);
        host.Chapters.JumpRequested += (_, index) => JumpToChapter(index);
    }

    private void ApplySidebarSide()
    {
        SidebarLeading.SetSide(SidebarSide.Leading);
        SidebarTrailing.SetSide(SidebarSide.Trailing);
        SidebarLeading.SetWidth(_sidebar.Width);
        SidebarTrailing.SetWidth(_sidebar.Width);
        SidebarLeading.SetPage(_settings.QuickSettingsPage);
        SidebarTrailing.SetPage(_settings.QuickSettingsPage);
    }

    // ------------------------------------------------------------- visibility

    /// <summary>Ctrl+Shift+B / titlebar button: playlist sidebar toggle.</summary>
    private void ToggleSidebar() => ToggleSidebarType(SidebarType.Playlist);

    private void ShowPlaylistSidebar() => ShowSidebarType(SidebarType.Playlist);

    private void ShowSettingsSidebar() => ToggleSidebarType(SidebarType.Settings);

    private void ShowChaptersSidebar() => ToggleSidebarType(SidebarType.Chapters);

    private void ShowPluginsSidebar() => ToggleSidebarType(SidebarType.Plugins);

    private void ToggleSidebarType(SidebarType type)
    {
        if (_compact)
        {
            ShowOsd(Loc.T("osd.windowTooNarrow"), Loc.T("osd.sidebarHiddenBelow"));
            return;
        }

        SidebarSide side = _sidebar.SideFor(type) ?? _settings.SidebarSide;
        SidebarToggleResult result = _sidebar.Toggle(side, type);
        if (result != SidebarToggleResult.Hidden)
        {
            _settings.SidebarSide = side;
            _settings.SidebarType = type;
        }

        ApplySidebarState(animate: true);
        SaveSidebarSettings();
    }

    private void ShowSidebarType(SidebarType type)
    {
        if (_compact)
        {
            return;
        }

        SidebarSide side = _settings.SidebarSide;
        _sidebar.Show(side, type);
        _settings.SidebarSide = side;
        _settings.SidebarType = type;
        ApplySidebarState(animate: true);
        SaveSidebarSettings();
    }

    private void HideSidebar()
    {
        if (!HasVisibleSidebar)
        {
            return;
        }

        _sidebar.HideAll();
        ApplySidebarState(animate: true);
        SaveSidebarSettings();
    }

    private void OnSidebarTabRequested(object? sender, SidebarTabRequestedEventArgs e)
    {
        if (sender is not SidebarHost host)
        {
            return;
        }

        SidebarToggleResult result = _sidebar.Toggle(host.Side, e.Type);
        if (result != SidebarToggleResult.Hidden)
        {
            _settings.SidebarSide = host.Side;
            _settings.SidebarType = e.Type;
        }

        ApplySidebarState(animate: true);
        SaveSidebarSettings();
    }

    private void ApplySidebarState(bool animate)
    {
        if (_updatingSidebar)
        {
            return;
        }

        _updatingSidebar = true;
        try
        {
            _sidebar.SetWidth(_sidebar.Width, RootGrid.ActualWidth);
            SidebarAnimationMode mode = SidebarState.ResolveAnimation(
                _settings.AnimationsEnabled,
                SystemAnimationsEnabled);

            ApplySidebarHost(SidebarLeading, animate, mode);
            ApplySidebarHost(SidebarTrailing, animate, mode);
        }
        finally
        {
            _updatingSidebar = false;
        }

        if (HasVisibleSidebar)
        {
            _oscState.IsSuppressed = true;
            ShowChrome();
        }
        else
        {
            _oscState.IsSuppressed = false;
        }

        ApplyLayout();
        ResetAutoHideTimer();
        if (HasVisibleSidebar)
        {
            RefreshSidebarContent();
        }

        Trace(
            $"sidebar state: leading={SidebarLeading.Visibility}/{SidebarLeading.Opacity:0.00}/" +
            $"w={SidebarLeading.ActualWidth:0}/tx={SidebarLeading.SlideOffset:0}; " +
            $"trailing={SidebarTrailing.Visibility}/{SidebarTrailing.Opacity:0.00}/" +
            $"w={SidebarTrailing.ActualWidth:0}/tx={SidebarTrailing.SlideOffset:0}; " +
            $"any={HasVisibleSidebar}");
    }

    private void ApplySidebarHost(
        SidebarHost host,
        bool animate,
        SidebarAnimationMode mode)
    {
        SidebarType? type = _sidebar.TypeFor(host.Side);
        bool show = type is not null &&
            !_compact &&
            SidebarState.AllowedAtWindowWidth(RootGrid.ActualWidth);

        if (show)
        {
            host.SetWidth(_sidebar.Width);
            host.SetType(type);
            host.SetPage(_settings.QuickSettingsPage);
            if (host.Visibility != Visibility.Visible)
            {
                if (animate)
                {
                    host.PlayShow(mode);
                }
                else
                {
                    host.ShowInstant();
                }
            }
        }
        else if (host.Visibility == Visibility.Visible)
        {
            if (animate)
            {
                host.PlayHide(mode, null);
            }
            else
            {
                host.CollapseInstant();
            }
        }
    }

    /// <summary>Compact/menu path: drop both slots without animating.</summary>
    private void CollapseSidebarsInstant()
    {
        if (!HasVisibleSidebar)
        {
            return;
        }

        _sidebar.HideAll();
        SidebarLeading.CollapseInstant();
        SidebarTrailing.CollapseInstant();
        _oscState.IsSuppressed = false;
    }

    private void OnSidebarWidthRequested(object? sender, SidebarWidthRequestedEventArgs e)
    {
        double width = SidebarState.ClampWidth(e.Width, RootGrid.ActualWidth);
        _sidebar.SetWidth(width, RootGrid.ActualWidth);
        _sidebarResizing = true;
        _oscState.IsSuppressed = true;
        _autoHideTimer.Stop();

        SidebarLeading.SetWidth(width);
        SidebarTrailing.SetWidth(width);
        SidebarLeading.QuickSettings.SetSidebarWidthValue(width);
        SidebarTrailing.QuickSettings.SetSidebarWidthValue(width);
        UpdateDockedInsets();
    }

    private void UpdateSidebarMargins()
    {
        double top = _titleBarVisible ? 40 : 0;
        SidebarLeading.Margin = new Thickness(0, top, 0, 0);
        SidebarTrailing.Margin = new Thickness(0, top, 0, 0);
    }

    /// <summary>Video/bottom-OSC insets while docked (edge-to-edge overlays).</summary>
    private void UpdateDockedInsets()
    {
        bool docked = _settings.Layout == WindowLayoutMode.Docked;
        bool leading = HasVisibleSidebar && _sidebar.IsVisible(SidebarSide.Leading);
        bool trailing = HasVisibleSidebar && _sidebar.IsVisible(SidebarSide.Trailing);
        double left = docked && leading ? _sidebar.Width : 0;
        double right = docked && trailing ? _sidebar.Width : 0;

        VideoFrame.Margin = new Thickness(left, 0, right, 0);
        OscBottom.Margin = new Thickness(
            leading ? _sidebar.Width : 0,
            0,
            trailing ? _sidebar.Width : 0,
            0);
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void SaveSidebarSettings()
    {
        _settings.SidebarWidth = _sidebar.Width;
        if (_sidebar.IsVisible(SidebarSide.Leading))
        {
            _settings.SidebarSide = SidebarSide.Leading;
        }
        else if (_sidebar.IsVisible(SidebarSide.Trailing))
        {
            _settings.SidebarSide = SidebarSide.Trailing;
        }

        ScheduleSettingsSave();
    }

    private void ScheduleSettingsSave()
    {
        if (_settingsSaveTimer is null)
        {
            SaveSettings();
            return;
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    // ------------------------------------------------------- content refresh

    private void RefreshSidebarContent()
    {
        RefreshSidebarTracks();
        RefreshSidebarPlaylist();
        RefreshSidebarChapters();
        RefreshSidebarDevices();
    }

    private void RefreshSidebarTracks()
    {
        if (_player is not { IsRunning: true })
        {
            return;
        }

        IReadOnlyList<TrackInfo> tracks = _player.GetTracks();
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetMediaState(HasMedia());
            host.QuickSettings.SetTracks(tracks);
            host.QuickSettings.SetVideoSize(_videoWidth, _videoHeight);
        }
    }

    private void RefreshSidebarPlaylist()
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.Playlist.SetItems(_playlistItems, _currentPlaylistIndex);
            host.Playlist.SetProgress(_oscVm.Position, _oscVm.Duration);
        }
    }

    private void UpdateSidebarPlaylistPosition(int index)
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.Playlist.UpdateCurrent(index);
        }
    }

    private void RefreshSidebarChapters()
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.Chapters.SetChapters(_chapterList, _currentChapterIndex);
        }
    }

    private void UpdateSidebarChapter(int index)
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.Chapters.SetCurrent(index);
        }
    }

    private void RefreshSidebarDevices()
    {
        if (_player is not { IsRunning: true })
        {
            return;
        }

        IReadOnlyList<MpvAudioDevice> devices = _player.GetAudioDevices();
        string current = string.IsNullOrWhiteSpace(_currentAudioDevice)
            ? _settings.AudioDevice
            : _currentAudioDevice;
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetAudioDevices(devices, current);
        }
    }

    private void UpdateSidebarProgress()
    {
        SidebarLeading.Playlist.SetProgress(_oscVm.Position, _oscVm.Duration);
        SidebarTrailing.Playlist.SetProgress(_oscVm.Position, _oscVm.Duration);
    }

    private void SyncQuickSettingsPage(QuickSettingsPage page)
    {
        SidebarLeading.QuickSettings.ShowPage(page);
        SidebarTrailing.QuickSettings.ShowPage(page);
    }

    private void SyncQuickSettingsValues()
    {
        SidebarLeading.QuickSettings.LoadFromSettings(_settings);
        SidebarTrailing.QuickSettings.LoadFromSettings(_settings);
    }

    // ------------------------------------------------------- playlist actions

    private void PlayPlaylistIndex(int index)
    {
        if (_player is null || index < 0)
        {
            return;
        }

        SendPlayerCommandAsync(
            "set",
            "playlist-pos",
            index.ToString(CultureInfo.InvariantCulture));
        SendPlayerCommandAsync("set", "pause", "no");
        ShowChrome();
    }

    private void RemovePlaylistIndexes(IReadOnlyList<int> indexes)
    {
        foreach (int index in indexes.OrderByDescending(value => value))
        {
            SendPlayerCommandAsync(
                "playlist-remove",
                index.ToString(CultureInfo.InvariantCulture));
        }

        ShowOsd(Loc.T("osd.playlistRemoved"), Loc.F("osd.fileCount", indexes.Count));
    }

    private void MovePlaylistItem(int from, int to)
    {
        if (from == to)
        {
            return;
        }

        SendPlayerCommandAsync(
            "playlist-move",
            from.ToString(CultureInfo.InvariantCulture),
            to.ToString(CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------- quick settings

    private void OnQuickSettingChanged(
        object? sender,
        QuickSettingChangedEventArgs e)
    {
        if (sender is not QuickSettingsView view)
        {
            return;
        }

        switch (e.Key)
        {
            case QuickSettingKey.Theme:
                _settings.Theme = (ThemeMode)Math.Clamp((int)e.Number, 0, 2);
                ApplyTheme();
                break;
            case QuickSettingKey.Animations:
                _settings.AnimationsEnabled = e.Flag;
                break;
            case QuickSettingKey.Layout:
                _settings.Layout = e.Number >= 1
                    ? WindowLayoutMode.Docked
                    : WindowLayoutMode.EdgeToEdge;
                ApplyLayout();
                break;
            case QuickSettingKey.AlwaysOnTop:
                _settings.AlwaysOnTop = e.Flag;
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = e.Flag;
                }

                TitleBar.IsPinned = e.Flag;
                break;
            case QuickSettingKey.LockAspect:
                _settings.LockAspectRatio = e.Flag;
                break;
            case QuickSettingKey.OscPosition:
                SetOscPosition((OscPosition)Math.Clamp((int)e.Number, 0, 2));
                break;
            case QuickSettingKey.OscAutoHide:
                _settings.OscAutoHide = e.Flag;
                _oscState.AutoHideEnabled = e.Flag;
                ResetAutoHideTimer();
                break;
            case QuickSettingKey.OsdEnabled:
                _settings.OsdEnabled = e.Flag;
                Osd.SetEnabled(e.Flag);
                break;
            case QuickSettingKey.OsdPosition:
                _settings.OsdPosition = (OsdPosition)Math.Clamp((int)e.Number, 0, 2);
                UpdateOsdPlacement();
                break;
            case QuickSettingKey.OsdSize:
                _settings.OsdSizePercent = OsdSettings.ClampSize(e.Number);
                Osd.SetScale(_settings.OsdSizePercent);
                UpdateOsdPlacement();
                break;
            case QuickSettingKey.OsdDuration:
                _settings.OsdDurationSeconds = OsdSettings.ClampDuration(e.Number);
                Osd.SetDuration(OsdSettings.Duration(_settings.OsdDurationSeconds));
                break;
            case QuickSettingKey.OsdPersistent:
                if (e.Flag)
                {
                    ShowPersistentOsd();
                }
                else
                {
                    ClosePersistentOsd();
                }

                break;
            case QuickSettingKey.FullscreenClock:
                _settings.FullscreenClockEnabled = e.Flag;
                ApplyStage5Settings();
                break;
            case QuickSettingKey.FullscreenBattery:
                _settings.FullscreenBatteryEnabled = e.Flag;
                ApplyStage5Settings();
                break;
            case QuickSettingKey.FullscreenTitle:
                _settings.FullscreenTitleEnabled = e.Flag;
                ApplyStage5Settings();
                break;
            case QuickSettingKey.ThumbnailPreview:
                _settings.ThumbnailPreviewEnabled = e.Flag;
                if (!e.Flag)
                {
                    HideTimePreview();
                }

                break;
            case QuickSettingKey.SidebarSide:
                MoveSidebarTo(e.Number >= 1 ? SidebarSide.Trailing : SidebarSide.Leading);
                break;
            case QuickSettingKey.SidebarWidth:
                SetSidebarWidth(e.Number);
                break;
            case QuickSettingKey.VideoTrack:
                ApplyTrackSelection(TrackType.Video, (int)e.Number);
                break;
            case QuickSettingKey.AudioTrack:
                ApplyTrackSelection(TrackType.Audio, (int)e.Number);
                break;
            case QuickSettingKey.SubtitleTrack:
                ApplyTrackSelection(TrackType.Subtitle, (int)e.Number);
                break;
            case QuickSettingKey.SecondarySubtitle:
                ApplySecondarySubtitle((int)e.Number);
                break;
            case QuickSettingKey.Aspect:
                _settings.VideoAspect = QuickSettings.NormalizeAspect(e.Text);
                ApplyMpvString("video-aspect-override", _settings.VideoAspect);
                break;
            case QuickSettingKey.Crop:
                _settings.VideoCropAspect = e.Number;
                ApplyCropFromSettings();
                break;
            case QuickSettingKey.Rotation:
                _settings.VideoRotation = QuickSettings.NormalizeRotation((int)e.Number);
                ApplyMpvString(
                    "video-rotate",
                    _settings.VideoRotation.ToString(CultureInfo.InvariantCulture));
                break;
            case QuickSettingKey.FlipHorizontal:
                _settings.VideoFlipHorizontal = e.Flag;
                ApplyVideoFilters();
                break;
            case QuickSettingKey.FlipVertical:
                _settings.VideoFlipVertical = e.Flag;
                ApplyVideoFilters();
                break;
            case QuickSettingKey.Deinterlace:
                _settings.VideoDeinterlace = e.Flag;
                ApplyMpvString(
                    "deinterlace",
                    QuickSettings.DeinterlaceValue(e.Flag));
                break;
            case QuickSettingKey.HardwareDecode:
                _settings.VideoHardwareDecode = e.Flag;
                ApplyMpvString("hwdec", EffectiveHwdecValue());
                ApplyVideoFilters();
                break;
            case QuickSettingKey.Zoom:
                _settings.VideoZoom = QuickSettings.ClampZoom(e.Number);
                ApplyMpvString("video-zoom", FormatDouble(_settings.VideoZoom));
                break;
            case QuickSettingKey.PanX:
                _settings.VideoPanX = QuickSettings.ClampPan(e.Number);
                ApplyMpvString("video-pan-x", FormatDouble(_settings.VideoPanX));
                break;
            case QuickSettingKey.PanY:
                _settings.VideoPanY = QuickSettings.ClampPan(e.Number);
                ApplyMpvString("video-pan-y", FormatDouble(_settings.VideoPanY));
                break;
            case QuickSettingKey.Volume:
                ApplyVolumeSilent(e.Number);
                break;
            case QuickSettingKey.MaxVolume:
                _settings.MaxVolume = QuickSettings.ClampMaxVolume(e.Number);
                ApplyMpvString("volume-max", FormatDouble(_settings.MaxVolume));
                _settings.Volume = QuickSettings.ClampVolume(_settings.Volume, _settings.MaxVolume);
                ApplyVolumeSilent(_settings.Volume);
                view.LoadFromSettings(_settings);
                break;
            case QuickSettingKey.AudioDelay:
                _settings.AudioDelay = QuickSettings.ClampAudioDelay(e.Number);
                ApplyMpvString("audio-delay", FormatDouble(_settings.AudioDelay));
                break;
            case QuickSettingKey.AudioDevice:
                _settings.AudioDevice = e.Text ?? "auto";
                ApplyMpvString("audio-device", _settings.AudioDevice);
                break;
            case QuickSettingKey.EqualizerEnabled:
                _settings.EqualizerEnabled = e.Flag;
                ApplyAudioFilters();
                SyncEqualizerViews();
                break;
            case QuickSettingKey.EqualizerPreset:
                ApplyEqualizerPreset(e.Text);
                ApplyAudioFilters();
                SyncEqualizerViews();
                break;
            case QuickSettingKey.EqualizerGain:
                _settings.EqualizerGains = EqualizerBands.NormalizeGains(e.Gains);
                _settings.EqualizerPreset = EqualizerBands.DetectPreset(_settings.EqualizerGains);
                ApplyAudioFilters();
                break;
            case QuickSettingKey.Mute:
                _settings.Muted = e.Flag;
                ApplyMpvString("mute", QuickSettings.BooleanValue(e.Flag));
                break;
            case QuickSettingKey.SubtitleVisible:
                _settings.SubtitleVisible = e.Flag;
                ApplyMpvString("sub-visibility", QuickSettings.BooleanValue(e.Flag));
                break;
            case QuickSettingKey.SubtitleDelay:
                _settings.SubtitleDelay = QuickSettings.ClampSubtitleDelay(e.Number);
                ApplyMpvString("sub-delay", FormatDouble(_settings.SubtitleDelay));
                break;
            case QuickSettingKey.SubtitlePosition:
                _settings.SubtitlePosition = QuickSettings.ClampSubtitlePosition(e.Number);
                ApplyMpvString("sub-pos", FormatDouble(_settings.SubtitlePosition));
                break;
            case QuickSettingKey.SubtitleScale:
                _settings.SubtitleScale = QuickSettings.ClampSubtitleScale(e.Number);
                ApplyMpvString("sub-scale", FormatDouble(_settings.SubtitleScale));
                break;
            case QuickSettingKey.SubtitleFontSize:
                _settings.SubtitleFontSize = QuickSettings.ClampSubtitleFontSize(e.Number);
                ApplyMpvString("sub-font-size", FormatDouble(_settings.SubtitleFontSize));
                break;
            case QuickSettingKey.SubtitleColor:
                _settings.SubtitleColor = QuickSettings.NormalizeColor(e.Text);
                ApplyMpvString("sub-color", _settings.SubtitleColor);
                break;
            case QuickSettingKey.SubtitleBorder:
                _settings.SubtitleBorderSize = QuickSettings.ClampSubtitleBorder(e.Number);
                ApplyMpvString("sub-border-size", FormatDouble(_settings.SubtitleBorderSize));
                break;
            case QuickSettingKey.LoadSubtitle:
                _ = OpenSubtitlePickerAsync();
                break;
            case QuickSettingKey.RefreshAudioDevices:
                RefreshSidebarDevices();
                break;
            case QuickSettingKey.ResetCard:
                ResetQuickSettingsCard(view, e.Text ?? string.Empty);
                break;
        }

        ScheduleSettingsSave();
    }

    private void MoveSidebarTo(SidebarSide side)
    {
        _settings.SidebarSide = side;
        SidebarType type = VisibleSidebarType ?? _settings.SidebarType;
        if (HasVisibleSidebar)
        {
            _sidebar.HideAll();
            _sidebar.Show(side, type);
            ApplySidebarState(animate: false);
        }
    }

    private void SetSidebarWidth(double width)
    {
        _sidebar.SetWidth(width, RootGrid.ActualWidth);
        _settings.SidebarWidth = _sidebar.Width;
        SidebarLeading.SetWidth(_sidebar.Width);
        SidebarTrailing.SetWidth(_sidebar.Width);
        if (HasVisibleSidebar)
        {
            UpdateDockedInsets();
        }

        ScheduleSettingsSave();
    }

    private void ResetQuickSettingsCard(QuickSettingsView view, string card)
    {
        switch (card)
        {
            case "appearance":
                _settings.Theme = ThemeMode.Dark;
                _settings.AnimationsEnabled = true;
                ApplyTheme();
                break;
            case "window":
                _settings.Layout = WindowLayoutMode.EdgeToEdge;
                _settings.AlwaysOnTop = false;
                _settings.LockAspectRatio = true;
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = false;
                }

                TitleBar.IsPinned = false;
                ApplyLayout();
                break;
            case "osc":
                _settings.OscPosition = OscPosition.Floating;
                _settings.OscAutoHide = true;
                _oscState.AutoHideEnabled = true;
                ApplyLayout();
                break;
            case "osd":
                _settings.OsdEnabled = true;
                _settings.OsdPosition = OsdPosition.BottomRight;
                _settings.OsdSizePercent = 100;
                _settings.OsdDurationSeconds = 1;
                _settings.FullscreenClockEnabled = true;
                _settings.FullscreenBatteryEnabled = true;
                _settings.FullscreenTitleEnabled = false;
                _settings.ThumbnailPreviewEnabled = true;
                ClosePersistentOsd();
                ApplyStage5Settings();
                break;
            case "sidebar":
                _settings.SidebarWidth = SidebarState.DefaultWidth;
                _sidebar.SetWidth(SidebarState.DefaultWidth, RootGrid.ActualWidth);
                _settings.SidebarWidth = _sidebar.Width;
                MoveSidebarTo(SidebarSide.Trailing);
                ApplySidebarSide();
                break;
            case "video-track":
                ApplyTrackSelection(TrackType.Video, -2);
                break;
            case "video-picture":
                _settings.VideoAspect = "no";
                _settings.VideoCropAspect = 0;
                _settings.VideoRotation = 0;
                _settings.VideoFlipHorizontal = false;
                _settings.VideoFlipVertical = false;
                _settings.VideoDeinterlace = false;
                _settings.VideoHardwareDecode = true;
                ApplyPersistedVideoSettings();
                break;
            case "video-zoom":
                _settings.VideoZoom = 0;
                _settings.VideoPanX = 0;
                _settings.VideoPanY = 0;
                ApplyPersistedVideoSettings();
                break;
            case "audio-track":
                ApplyTrackSelection(TrackType.Audio, -2);
                break;
            case "audio-volume":
                _settings.Volume = 100;
                _settings.MaxVolume = 130;
                _settings.Muted = false;
                ApplyPersistedAudioSettings();
                break;
            case "audio-delay":
                _settings.AudioDelay = 0;
                ApplyPersistedAudioSettings();
                break;
            case "audio-device":
                _settings.AudioDevice = "auto";
                ApplyPersistedAudioSettings();
                break;
            case "audio-equalizer":
                _settings.EqualizerEnabled = true;
                _settings.EqualizerGains = EqualizerBands.CreatePreset("Flat").ToList();
                _settings.EqualizerPreset = "Flat";
                ApplyAudioFilters();
                SyncEqualizerViews();
                break;
            case "subtitle-track":
                ApplyTrackSelection(TrackType.Subtitle, -2);
                ApplySecondarySubtitle(-1);
                break;
            case "subtitle-timing":
                _settings.SubtitleDelay = 0;
                _settings.SubtitlePosition = 100;
                _settings.SubtitleScale = 1;
                ApplyPersistedSubtitleSettings();
                break;
            case "subtitle-style":
                _settings.SubtitleFontSize = 55;
                _settings.SubtitleColor = "#FFFFFFFF";
                _settings.SubtitleBorderSize = 3;
                ApplyPersistedSubtitleSettings();
                break;
        }

        view.LoadFromSettings(_settings);
        SyncQuickSettingsValues();
        ScheduleSettingsSave();
    }

    // ------------------------------------------------------------- mpv apply

    private void ApplyMpvString(string name, string value)
    {
        try
        {
            if (_player is { IsRunning: true } &&
                !_player.TrySetPropertyString(name, value))
            {
                Trace($"sidebar property {name}={value} rejected");
            }
        }
        catch (Exception ex)
        {
            Trace($"sidebar property {name} failed: {ex.Message}");
        }
    }

    private static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private void ApplyVolumeSilent(double value)
    {
        _settings.Volume = QuickSettings.ClampVolume(value, _settings.MaxVolume);
        ApplyMpvString("volume", FormatDouble(_settings.Volume));
    }

    private void ApplyTrackSelection(TrackType type, int id)
    {
        string property = type switch
        {
            TrackType.Video => "vid",
            TrackType.Audio => "aid",
            _ => "sid",
        };
        string value = id switch
        {
            -2 => "auto",
            -1 => "no",
            _ => id.ToString(CultureInfo.InvariantCulture),
        };
        ApplyMpvString(property, value);
        if (type == TrackType.Subtitle)
        {
            ApplyMpvString("sub-visibility", QuickSettings.BooleanValue(id != -1));
        }
    }

    private void ApplySecondarySubtitle(int id)
    {
        _settings.SecondarySubtitleId = id < 0 ? null : id;
        ApplyMpvString(
            "secondary-sid",
            id < 0 ? "no" : id.ToString(CultureInfo.InvariantCulture));
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetSecondarySubtitle(id < 0 ? null : id);
        }
    }

    private void ApplyVideoFilters() => ApplyVideoFiltersCore(new List<string>());

    private void ApplyVideoFiltersCore(List<string> errors)
    {
        if (_player is null)
        {
            return;
        }

        VideoFilterGraphResult result = VideoFilterGraph.Build(
            _settings.VideoFilters,
            _videoWidth,
            _videoHeight,
            CreateVideoFilterLegacy());
        errors.AddRange(result.Errors);

        if (!_player.TrySetPropertyString("vf", result.Graph))
        {
            string message = result.Graph.Length == 0
                ? Loc.T("osd.videoFilterClearRefused")
                : Loc.T("osd.videoFilterGraphRejected") + FirstGraphPart(result.Graph);
            errors.Add(message);
            Trace(message);
        }

        // CPU video filters cannot consume d3d11 hardware frames. Force software
        // decoding while any vf entry is active, then restore hwdec when clear.
        bool needsSoftwareDecode = result.Graph.Length > 0;
        if (needsSoftwareDecode && _settings.VideoHardwareDecode)
        {
            ApplyMpvString("hwdec", "no");
            if (!_filterSoftwareDecode)
            {
                _filterSoftwareDecode = true;
                if (!string.IsNullOrEmpty(_player.GetPropertyString("path")))
                {
                    ReloadForVideoFilters();
                }

                ShowOsd(Loc.T("osd.videoFilters"), Loc.T("osd.softwareDecode"));
            }
        }
        else if (!needsSoftwareDecode && _filterSoftwareDecode)
        {
            _filterSoftwareDecode = false;
            ApplyMpvString("hwdec", EffectiveHwdecValue());
            if (!string.IsNullOrEmpty(_player.GetPropertyString("path")))
            {
                ReloadForVideoFilters();
            }
        }
    }

    private void ReloadForVideoFilters()
    {
        if (_player is null)
        {
            return;
        }

        string? path = _player.GetPropertyString("path");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _pendingFilterResumeTime = _player.GetPropertyDouble("time-pos");
        _pendingFilterResumePaused = _player.GetPropertyFlag("pause");
        _player.CommandAsync("loadfile", path, "replace");
    }

    private void ApplyCropFromSettings() => ApplyVideoFilters();

    private void ApplyPersistedVideoSettings()
    {
        ApplyMpvString(
            "video-aspect-override",
            QuickSettings.NormalizeAspect(_settings.VideoAspect));
        ApplyMpvString(
            "video-rotate",
            QuickSettings.NormalizeRotation(_settings.VideoRotation)
                .ToString(CultureInfo.InvariantCulture));
        ApplyMpvString("video-zoom", FormatDouble(QuickSettings.ClampZoom(_settings.VideoZoom)));
        ApplyMpvString("video-pan-x", FormatDouble(QuickSettings.ClampPan(_settings.VideoPanX)));
        ApplyMpvString("video-pan-y", FormatDouble(QuickSettings.ClampPan(_settings.VideoPanY)));
        ApplyMpvString(
            "deinterlace",
            QuickSettings.DeinterlaceValue(_settings.VideoDeinterlace));
        ApplyMpvString("hwdec", EffectiveHwdecValue());
        ApplyVideoFilters();
        ApplyCropFromSettings();
    }

    private void ApplyPersistedAudioSettings()
    {
        ApplyMpvString(
            "volume-max",
            FormatDouble(QuickSettings.ClampMaxVolume(_settings.MaxVolume)));
        ApplyVolumeSilent(_settings.Volume);
        ApplyMpvString("mute", QuickSettings.BooleanValue(_settings.Muted));
        ApplyMpvString(
            "audio-delay",
            FormatDouble(QuickSettings.ClampAudioDelay(_settings.AudioDelay)));
        if (!string.IsNullOrWhiteSpace(_settings.AudioDevice))
        {
            ApplyMpvString("audio-device", _settings.AudioDevice);
        }

        ApplyAudioFilters();
    }

    private void ApplyPersistedSubtitleSettings()
    {
        ApplyMpvString(
            "sub-visibility",
            QuickSettings.BooleanValue(_settings.SubtitleVisible));
        ApplyMpvString(
            "sub-delay",
            FormatDouble(QuickSettings.ClampSubtitleDelay(_settings.SubtitleDelay)));
        ApplyMpvString(
            "sub-pos",
            FormatDouble(QuickSettings.ClampSubtitlePosition(_settings.SubtitlePosition)));
        ApplyMpvString(
            "sub-scale",
            FormatDouble(QuickSettings.ClampSubtitleScale(_settings.SubtitleScale)));
        ApplyMpvString(
            "sub-font-size",
            FormatDouble(QuickSettings.ClampSubtitleFontSize(_settings.SubtitleFontSize)));
        ApplyMpvString("sub-color", QuickSettings.NormalizeColor(_settings.SubtitleColor));
        ApplyMpvString(
            "sub-border-size",
            FormatDouble(QuickSettings.ClampSubtitleBorder(_settings.SubtitleBorderSize)));
        ApplyMpvString("sub-ass-override", NormalizeAssOverride(_settings.SubtitleAssOverride));
        ApplyMpvString("sub-bold", QuickSettings.BooleanValue(_settings.SubtitleBold));
        ApplyMpvString("sub-italic", QuickSettings.BooleanValue(_settings.SubtitleItalic));
        ApplySecondarySubtitle(_settings.SecondarySubtitleId ?? -1);
    }

    private void ApplyPersistedQuickSettings()
    {
        ApplyPersistedVideoSettings();
        ApplyPersistedAudioSettings();
        ApplyPersistedSubtitleSettings();
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.LoadFromSettings(_settings);
            host.QuickSettings.SetMediaState(HasMedia());
        }

        RefreshSidebarContent();
    }

    // --------------------------------------------- player -> sidebar mirrors

    private void SyncSidebarVolume()
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetVolumeState(_oscVm.Volume, _oscVm.IsMuted);
        }
    }

    private void SyncSidebarSubtitleVisibility()
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetSubtitleVisibility(_settings.SubtitleVisible);
        }
    }

    private void SyncSidebarAudioDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetAudioDeviceName(name);
        }
    }
}
