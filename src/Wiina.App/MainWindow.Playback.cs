using System.Globalization;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Wiina.App.Controls;
using Wiina.App.Interop;
using Wiina.App.Localization;
using Wiina.App.Views;
using Wiina.App.Services;
using Wiina.Core.Audio;
using Wiina.Core.Files;
using Wiina.Core.History;
using Wiina.Core.Input;
using Wiina.Core.Models;
using Wiina.Core.Osd;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Core.Subtitles;
using Wiina.Core.Text;
using Wiina.Mpv;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wiina.App;

/// <summary>
/// Stage 3: real playback, file/URL opening, track/subtitle/chapter/playlist
/// control, input mapping, OSD, history/resume, media keys and .m3u persistence.
/// Kept as a partial file so MainWindow.xaml.cs stays about the shell/OSC.
/// </summary>
public sealed partial class MainWindow
{
    private readonly HistoryStore _historyStore = new();
    private KeyMap _keyMap = KeyMap.CreateDefault();
    private readonly List<TrackInfo> _trackList = new();
    private readonly List<ChapterInfo> _chapterList = new();
    private readonly List<MpvPlaylistItem> _playlistItems = new();
    private readonly List<string> _pendingSubtitles = new();

    private DispatcherQueueTimer? _clickTimer;
    private DispatcherQueueTimer? _suppressClickTimer;
    private DispatcherQueueTimer? _historyTimer;
    private DispatcherQueueTimer? _dragPump;

    private string? _currentPath;
    private string? _currentTitle;
    private int _currentChapterIndex = -1;
    private int _currentPlaylistIndex = -1;
    private int _abLoopStage;
    private double _abLoopA;
    private double _abLoopB;
    private bool _abLoopSeeking;
    private DateTime _lastAbLoopSeekUtc;
    private int _loopMode;
    private bool _resumeApplied;
    private bool _stage3Closed;
    private bool _windowDragCandidate;
    private bool _windowDragging;
    private bool _suppressClick;
    private bool _chapterChangeOsc;
    private bool _wantSubtitleSelection;
    private int _smartLoadGeneration;
    private string? _autoLoadedSubtitlePath;
    private string? _autoLoadedAudioPath;
    private Point _dragStartPoint;
    private int _dragStartCursorX;
    private int _dragStartCursorY;
    private int _windowStartX;
    private int _windowStartY;

    private void InitializeStage3()
    {
        _settings.RecentUrls ??= new List<string>();

        TitleBar.MoreClicked += (_, _) => ShowApplicationMenu(TitleBar);
        RootGrid.AllowDrop = true;
        RootGrid.DragOver += OnRootDragOver;
        RootGrid.Drop += OnRootDropAsync;
        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnRootKeyDown),
            handledEventsToo: false);

        // Media chrome (OSC, titlebar, mini controls) must never own keyboard
        // focus, otherwise Space/Enter activate the focused button instead of
        // toggling playback or fullscreen. The recursive pass is only the
        // first line of defence: lazily created containers and hidden chrome
        // live outside it, so the keyboard-driven modes (mini player,
        // fullscreen) also intercept keys in the tunneling preview phase,
        // before a focused control can consume Space/Enter or move focus with
        // Tab.
        MakeMediaChromeKeyboardTransparent();
        RootGrid.AddHandler(
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(OnRootPreviewKeyDown),
            handledEventsToo: true);
        // Last line of defence for the keyboard-driven modes: whatever route
        // focus took (Tab, UIA, automation, a template part created after the
        // recursive pass), a chrome control must not keep it — Space/Enter
        // would activate that control instead of toggling playback.
        RootGrid.GotFocus += OnRootGotFocus;

        // handledEventsToo: the video surface (SwapChainPanel) marks pointer input as
        // handled once it owns the pointer, and a plain += subscription would then stop
        // receiving PointerMoved — the content drag would start and immediately freeze.
        VideoFrame.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnVideoPointerPressed), handledEventsToo: true);
        VideoFrame.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnVideoPointerMoved), handledEventsToo: true);
        VideoFrame.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnVideoPointerReleased), handledEventsToo: true);
        VideoFrame.PointerCaptureLost += OnVideoPointerCaptureLost;

        RootGrid.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnVideoPointerWheelChanged),
            handledEventsToo: true);
        VideoFrame.Tapped += OnVideoTapped;
        VideoFrame.DoubleTapped += OnVideoDoubleTapped;
        VideoFrame.RightTapped += OnVideoRightTapped;

        SubscribeWheel(OscFloating.Timeline);
        SubscribeWheel(OscBottom.Timeline);
        SubscribeWheel(TitleBar.TopTimeline);
        OscFloating.Volume.WheelVolumeRequested += AdjustUiVolume;
        OscBottom.Volume.WheelVolumeRequested += AdjustUiVolume;
        TitleBar.TopVolumeControl.WheelVolumeRequested += AdjustUiVolume;

        _clickTimer = DispatcherQueue.CreateTimer();
        _clickTimer.IsRepeating = false;
        _clickTimer.Tick += (_, _) => ExecuteSingleClick();

        _suppressClickTimer = DispatcherQueue.CreateTimer();
        _suppressClickTimer.Interval = TimeSpan.FromMilliseconds(300);
        _suppressClickTimer.IsRepeating = false;
        _suppressClickTimer.Tick += (_, _) => _suppressClick = false;

        // Content drag: poll the cursor while the mouse button is held. Moving the
        // window drops the XAML pointer capture, so PointerMoved alone cannot keep
        // the drag alive (the window stopped following the cursor after 1 px).
        _dragPump = DispatcherQueue.CreateTimer();
        _dragPump.Interval = TimeSpan.FromMilliseconds(16);
        _dragPump.IsRepeating = true;
        _dragPump.Tick += OnDragPumpTick;

        _historyTimer = DispatcherQueue.CreateTimer();
        _historyTimer.Interval = TimeSpan.FromSeconds(10);
        _historyTimer.IsRepeating = true;
        _historyTimer.Tick += (_, _) => SaveCurrentHistory();
        _historyTimer.Start();
    }

    private void SubscribeWheel(TimelineControl timeline) =>
        timeline.WheelSeekRequested += SeekRelative;

    private void InitializePlayerStage3(MpvPlayer player)
    {
        player.TrackListChanged += OnTrackListChanged;
        player.ChapterListChanged += OnChapterListChanged;
        player.PlaylistChanged += OnPlaylistChanged;
        player.AudioDeviceListChanged += OnAudioDeviceListChanged;
        player.PlaylistPositionChanged += OnPlaylistPositionChanged;
        player.ChapterChanged += OnChapterChanged;
        player.VideoSizeChanged += OnVideoSizeChanged;
        player.FileLoadFailed += OnFileLoadFailed;
        player.CommandFailed += OnCommandFailed;
        player.MetadataChanged += OnMetadataChanged;
    }

    private void OnClosedStage3()
    {
        if (_stage3Closed)
        {
            return;
        }

        _stage3Closed = true;
        SaveCurrentHistory();
        _historyTimer?.Stop();
        _clickTimer?.Stop();
        _suppressClickTimer?.Stop();

        // Unhook mpv callbacks before disposal: the event thread is still
        // running and must not re-enter a window that is tearing down.
        if (_player is { } player)
        {
            player.Log -= OnPlayerLog;
            player.DisplaySwapChainChanged -= OnDisplaySwapChainChanged;
            player.NumberPropertyChanged -= OnNumberPropertyChanged;
            player.FlagPropertyChanged -= OnFlagPropertyChanged;
            player.StringPropertyChanged -= OnStringPropertyChanged;
            player.FileLoaded -= OnFileLoaded;
            player.TrackListChanged -= OnTrackListChanged;
            player.ChapterListChanged -= OnChapterListChanged;
            player.PlaylistChanged -= OnPlaylistChanged;
            player.AudioDeviceListChanged -= OnAudioDeviceListChanged;
            player.PlaylistPositionChanged -= OnPlaylistPositionChanged;
            player.ChapterChanged -= OnChapterChanged;
            player.VideoSizeChanged -= OnVideoSizeChanged;
            player.FileLoadFailed -= OnFileLoadFailed;
            player.CommandFailed -= OnCommandFailed;
            player.MetadataChanged -= OnMetadataChanged;
        }
    }

    // ---------------------------------------------------------------- opening

    private void OpenStartupMedia()
    {
        Trace($"startup paths ({_startupPaths.Count}): {string.Join(" | ", _startupPaths)}");
        if (_startupPaths.Count > 0)
        {
            OpenPaths(_startupPaths);
            return;
        }

        ShowOsd(Loc.T("osd.controlsHint"), Loc.T("osd.controlsHintSecondary"));
    }

    private async Task OpenFilePickerAsync()
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

            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                OpenPaths(files.Select(file => file.Path));
            }
        }
        catch (Exception ex)
        {
            Trace("open file picker failed: " + ex);
            ShowOsd(Loc.T("osd.openFileFailed"), ex.Message);
        }
    }

    private async Task OpenFolderPickerAsync()
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);

            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                OpenPaths(new[] { folder.Path });
            }
        }
        catch (Exception ex)
        {
            Trace("open folder picker failed: " + ex);
            ShowOsd(Loc.T("osd.openFolderFailed"), ex.Message);
        }
    }

    private async Task OpenUrlDialogAsync(bool newWindow = false)
    {
        OpenUrlRequest? request = await Dialogs.OpenUrlAsync(RootGrid.XamlRoot, _settings);
        if (request is null)
        {
            return;
        }

        AddRecentUrl(request.Url);
        App.OpenMedia(new[] { request.Url }, newWindow || request.NewWindow);
    }

    private async Task OpenSubtitlePickerAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            foreach (string extension in new[]
                     {
                         ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup",
                     })
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                AddExternalSubtitle(file.Path);
            }
        }
        catch (Exception ex)
        {
            Trace("open subtitle picker failed: " + ex);
            ShowOsd(Loc.T("osd.loadSubtitleFailed"), ex.Message);
        }
    }

    private async Task OpenPlaylistPickerAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            picker.FileTypeFilter.Add(".m3u");
            picker.FileTypeFilter.Add(".m3u8");
            InitializePicker(picker);

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                OpenPaths(new[] { file.Path });
            }
        }
        catch (Exception ex)
        {
            Trace("open playlist failed: " + ex);
            ShowOsd(Loc.T("osd.openPlaylistFailed"), ex.Message);
        }
    }

    private async Task SavePlaylistPickerAsync()
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = "wiina-playlist",
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            picker.FileTypeChoices.Add(
                Loc.T("playlist.m3uFilter"),
                new List<string> { ".m3u" });
            InitializePicker(picker);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            Playlist playlist = Playlist.FromPaths(
                _playlistItems
                    .Select(item => item.Filename)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path!));
            await FileIO.WriteTextAsync(file, playlist.ToM3u());
            ShowOsd(Loc.T("osd.playlistSaved"), file.Name);
        }
        catch (Exception ex)
        {
            Trace("save playlist failed: " + ex);
            ShowOsd(Loc.T("osd.playlistSaveFailed"), ex.Message);
        }
    }

    private async Task ScreenshotToFolderPickerAsync()
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);

            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            string name = BuildScreenshotName(folder.Path);
            string path = Path.Combine(folder.Path, name);
            SendPlayerCommandAsync(
                "screenshot-to-file",
                path,
                _settings.ScreenshotWithSubtitles ? "subtitles" : "video");
            ShowOsd(Loc.T("osd.screenshotSaved"), path);
        }
        catch (Exception ex)
        {
            Trace("screenshot to folder failed: " + ex);
            ShowOsd(Loc.T("osd.screenshotFailed"), ex.Message);
        }
    }

    private void InitializePicker(object picker)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void AddRecentUrl(string url)
    {
        _settings.RecentUrls = RecentList
            .Push(_settings.RecentUrls, url)
            .ToList();
        SaveSettings();
    }

    private void OpenPaths(IEnumerable<string> inputs)
    {
        var playable = new List<string>();
        var subtitles = new List<string>();
        var errors = new List<string>();

        List<string> distinctInputs = inputs
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string input in distinctInputs)
        {
            CollectMediaPaths(input.Trim('"'), playable, subtitles, errors, 0);
        }

        // Opening a single video can pull the rest of its folder into the
        // playlist. The subtitle/audio sidecar helpers still run from the
        // regular open path, so only plain playable media is expanded here.
        int startIndex = 0;
        if (distinctInputs.Count == 1 && playable.Count == 1)
        {
            string single = distinctInputs[0].Trim('"');
            if (File.Exists(single) &&
                MediaFileTypes.IsPlayableMedia(single) &&
                _settings.AutoPlaylistMode != AutoPlaylistMode.Manual)
            {
                string? folder = Path.GetDirectoryName(single);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    bool recursive =
                        _settings.AutoPlaylistMode == AutoPlaylistMode.IncludeSubfolders;
                    IReadOnlyList<string> candidates = MediaFileTypes
                        .ExpandFolder(folder, recursive)
                        .Where(MediaFileTypes.IsPlayableMedia)
                        .ToArray();

                    // Use the whole naturally sorted folder as the queue and
                    // start at the opened file, so "next" is the following
                    // episode instead of the first file in the folder.
                    PlaylistExpansion? expansion =
                        PlaylistOrdering.ExpandSingleFile(single, candidates);
                    if (expansion is not null)
                    {
                        playable = expansion.Entries.ToList();
                        startIndex = expansion.CurrentIndex;
                        Trace(
                            $"auto playlist ({playable.Count}, current={startIndex}): " +
                            string.Join(" | ", playable));
                    }
                    else
                    {
                        // The opened file was not in the listing (permission or
                        // race); keep the old behavior and append the others.
                        playable.AddRange(candidates.Where(path => !string.Equals(
                            path, single, StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
        }

        playable = playable
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (startIndex >= playable.Count)
        {
            startIndex = 0;
        }

        if (playable.Count == 0)
        {
            if (subtitles.Count > 0 && HasMedia())
            {
                foreach (string subtitle in subtitles.Distinct(
                             StringComparer.OrdinalIgnoreCase))
                {
                    AddExternalSubtitle(subtitle);
                }

                return;
            }

            ShowOsd(
                subtitles.Count > 0 ? Loc.T("osd.pleasePlayMedia") : Loc.T("osd.noPlayableMedia"),
                errors.FirstOrDefault());
            return;
        }

        _pendingSubtitles.Clear();
        _pendingSubtitles.AddRange(subtitles.Distinct(StringComparer.OrdinalIgnoreCase));
        PlayEntries(playable, startIndex);
    }

    private void CollectMediaPaths(
        string input,
        List<string> playable,
        List<string> subtitles,
        List<string> errors,
        int depth)
    {
        if (depth > 4 || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (Directory.Exists(input))
        {
            IReadOnlyList<string> files = MediaFileTypes.ExpandFolder(
                input,
                recursive: _settings.AutoPlaylistMode == AutoPlaylistMode.IncludeSubfolders);
            if (files.Count == 0)
            {
                errors.Add(Loc.F("osd.folderNoMedia", input));
                return;
            }

            foreach (string file in files)
            {
                CollectMediaPaths(file, playable, subtitles, errors, depth + 1);
            }

            return;
        }

        if (File.Exists(input))
        {
            if (MediaFileTypes.IsPlaylist(input))
            {
                try
                {
                    Playlist playlist = Playlist.LoadM3u(input);
                    string directory = Path.GetDirectoryName(input) ?? string.Empty;
                    foreach (PlaylistItem item in playlist.Items)
                    {
                        string path = item.Path;
                        if (!Path.IsPathRooted(path) &&
                            !Uri.TryCreate(path, UriKind.Absolute, out _))
                        {
                            path = Path.Combine(directory, path);
                        }

                        CollectMediaPaths(path, playable, subtitles, errors, depth + 1);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(Loc.F("osd.playlistReadFailed", Path.GetFileName(input)));
                    Trace("m3u parse failed: " + ex.Message);
                }

                return;
            }

            if (MediaFileTypes.IsSubtitle(input))
            {
                subtitles.Add(input);
                return;
            }

            if (MediaFileTypes.IsPlayableMedia(input))
            {
                playable.Add(input);
                return;
            }

            errors.Add(Loc.F("osd.unsupportedFile", Path.GetFileName(input)));
            return;
        }

        if (Path.IsPathRooted(input) || input.Contains('\\'))
        {
            errors.Add(Loc.F("osd.fileNotFound", input));
            return;
        }

        if (!input.Contains("://", StringComparison.Ordinal) &&
            MediaFileTypes.Classify(input) != MediaKind.Unknown)
        {
            errors.Add(Loc.F("osd.fileNotFound", input));
            return;
        }

        if (TryNormalizeUrl(input, out string url))
        {
            playable.Add(url);
            return;
        }

        errors.Add(Loc.F("osd.urlNotFound", input));
    }

    private void PlayEntries(IReadOnlyList<string> entries, int startIndex = 0)
    {
        if (entries.Count == 0)
        {
            return;
        }

        startIndex = Math.Clamp(startIndex, 0, entries.Count - 1);
        string current = entries[startIndex];

        _resumeApplied = false;
        _abLoopStage = 0;
        _abLoopA = 0;
        _abLoopB = 0;
        _oscVm.AbLoopA = null;
        _oscVm.AbLoopB = null;
        TitleBar.Title = Loc.T("osd.loading");
        Title = "Wiina";
        UpdateSmtc();
        Trace($"play entries ({entries.Count}, current={startIndex}): {string.Join(" | ", entries)}");

        // Open the requested file first so playback starts there, then append
        // the rest of the queue in order. The appends would otherwise leave the
        // opened file at index 0; playlist-move restores the sorted queue
        // underneath the playing item, so next/previous follow the folder.
        SendPlayerCommandAsync("loadfile", current, "replace");
        for (int i = 0; i < entries.Count; i++)
        {
            if (i == startIndex)
            {
                continue;
            }

            SendPlayerCommandAsync("loadfile", entries[i], "append");
        }

        for (int i = 0; i < startIndex; i++)
        {
            SendPlayerCommandAsync(
                "playlist-move",
                (i + 1).ToString(CultureInfo.InvariantCulture),
                i.ToString(CultureInfo.InvariantCulture));
        }

        BeginStreamOpen(current);
        if (IsUrlPath(current))
        {
            ShowOsd(OsdMessages.NetworkOpening(ShortStreamName(current), Loc.CurrentLanguage));
        }
        else
        {
            ShowOsd(
                entries.Count > 1 ? Loc.F("osd.openingCount", entries.Count) : Loc.T("osd.opening"),
                entries.Count > 1 ? Path.GetFileName(current) : null);
        }
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Loc.T("dialogs.dropCaption");
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void OnRootDropAsync(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
            var paths = new List<string>();
            foreach (IStorageItem item in items)
            {
                switch (item)
                {
                    case StorageFile file:
                        paths.Add(file.Path);
                        break;
                    case StorageFolder folder:
                        paths.Add(folder.Path);
                        break;
                }
            }

            if (paths.Count > 0)
            {
                OpenPaths(paths);
            }
        }
        catch (Exception ex)
        {
            Trace("drop failed: " + ex);
            ShowOsd(Loc.T("osd.dropOpenFailed"), ex.Message);
        }
    }

    // -------------------------------------------------------------- playback

    private void SendPlayerCommandAsync(params string[] args)
    {
        try
        {
            if (_player is { IsRunning: true })
            {
                _player.CommandAsync(args);
            }
            else
            {
                Trace($"mpv command skipped (not running): {string.Join(' ', args)}");
            }
        }
        catch (Exception ex) when (ex is MpvException or InvalidOperationException)
        {
            Trace($"mpv command failed ({string.Join(' ', args)}): {ex.Message}");
            ShowOsd(Loc.T("osd.playbackCommandFailed"), ex.Message);
        }
    }

    private bool HasMedia() =>
        _player is { IsRunning: true } &&
        !string.IsNullOrWhiteSpace(_player.GetPropertyString("path"));

    private void TogglePlayPause()
    {
        if (!HasMedia())
        {
            ShowOsd(Loc.T("osd.noMediaPlaying"), Loc.T("osd.controlsHint"));
            return;
        }

        bool paused = _player!.GetPropertyFlag("pause");
        Trace($"toggle pause, was paused={paused}");
        SendPlayerCommandAsync("set", "pause", paused ? "no" : "yes");
        ShowOsd(OsdMessages.Pause(!paused, Loc.CurrentLanguage));
    }

    private void PlayOrPause(bool play)
    {
        if (!HasMedia())
        {
            return;
        }

        SendPlayerCommandAsync("set", "pause", play ? "no" : "yes");
        ShowOsd(OsdMessages.Pause(!play, Loc.CurrentLanguage));
    }

    private void StopPlayback()
    {
        SaveCurrentHistory();
        SendPlayerCommandAsync("stop");
        _currentPath = null;
        _currentTitle = null;
        TitleBar.Title = "Wiina";
        Title = "Wiina";
        EndStreamOpen();
        ShowOsd(Loc.T("osd.stopped"));
        UpdateSmtc();
    }

    private void SeekRelative(double seconds)
    {
        if (!HasMedia() || Math.Abs(seconds) < 0.001)
        {
            return;
        }

        double target = Math.Clamp(
            _oscVm.Position + seconds,
            0,
            _oscVm.Duration > 0 ? _oscVm.Duration : double.MaxValue);
        Trace($"seek relative {seconds:0.###}");
        SendPlayerCommandAsync(
            "seek",
            seconds.ToString("0.###", CultureInfo.InvariantCulture),
            "relative");
        ShowOsd(OsdMessages.Seek(target, _oscVm.Duration));
    }

    private void SeekAbsolute(double seconds, bool showOsd = true)
    {
        if (!HasMedia())
        {
            return;
        }

        double target = Math.Clamp(
            seconds,
            0,
            _oscVm.Duration > 0 ? _oscVm.Duration : double.MaxValue);
        SendPlayerCommandAsync(
            "seek",
            target.ToString("0.###", CultureInfo.InvariantCulture),
            "absolute");
        if (showOsd)
        {
            ShowOsd(OsdMessages.Seek(target, _oscVm.Duration));
        }
    }

    private void SetVolume(double value)
    {
        double clamped = VolumeMapping.ClampActual(value, _settings.MaxVolume);
        Trace($"set volume {clamped:0.##}");
        _oscVm.SetVolumeFromPlayer(clamped);
        if (_player is { IsRunning: true })
        {
            _player.TrySetPropertyString(
                "volume",
                clamped.ToString("0.##", CultureInfo.InvariantCulture));
        }

        ShowOsd(OsdMessages.Volume(clamped, Loc.CurrentLanguage));
    }

    /// <summary>Keyboard/menu volume: may exceed 100% up to the configured limit.</summary>
    private void AdjustVolume(double delta)
    {
        if (_player is null)
        {
            return;
        }

        SetVolume(_oscVm.Volume + delta);
    }

    /// <summary>Slider/wheel volume: the UI stops at 100%, so amplification is keyboard-only.</summary>
    private void AdjustUiVolume(double delta)
    {
        if (_player is null)
        {
            return;
        }

        SetVolume(VolumeMapping.ApplyWheelDelta(_oscVm.Volume, delta));
    }

    private void ToggleMute()
    {
        if (_player is null)
        {
            return;
        }

        bool muted = !_oscVm.IsMuted;
        Trace($"set mute {muted}");
        _player.TrySetPropertyString("mute", muted ? "yes" : "no");
        ShowOsd(OsdMessages.Mute(muted, Loc.CurrentLanguage));
    }

    private void SetSpeed(double speed)
    {
        double clamped = Math.Clamp(speed, 0.05, 4.0);
        Trace($"set speed {clamped:0.###}");
        if (_player is { IsRunning: true })
        {
            _player.TrySetPropertyString(
                "speed",
                clamped.ToString("0.###", CultureInfo.InvariantCulture));
        }

        ShowOsd(OsdMessages.Speed(clamped, Loc.CurrentLanguage));
    }

    private void MultiplySpeed(double factor)
    {
        if (_player is null)
        {
            return;
        }

        SetSpeed(_oscVm.Speed * factor);
    }

    private void ResetSpeed() => SetSpeed(1.0);

    private void FrameStep(bool forward)
    {
        if (!HasMedia())
        {
            return;
        }

        SendPlayerCommandAsync("set", "pause", "yes");
        SendPlayerCommandAsync(forward ? "frame-step" : "frame-back-step");
        Trace($"frame step {forward}");
        ShowOsd(forward ? Loc.T("osd.nextFrame") : Loc.T("osd.prevFrame"));
    }

    private void PreviousChapter() => ChangeChapter(-1);

    private void NextChapter() => ChangeChapter(1);

    private void ChangeChapter(int direction)
    {
        if (!HasMedia() || _chapterList.Count == 0)
        {
            ShowOsd(Loc.T("osd.noChapters"));
            return;
        }

        _chapterChangeOsc = true;
        SendPlayerCommandAsync("add", "chapter", direction.ToString(CultureInfo.InvariantCulture));
    }

    private void JumpToChapter(int index)
    {
        if (!HasMedia() || index < 0 || index >= _chapterList.Count)
        {
            return;
        }

        SendPlayerCommandAsync(
            "set",
            "chapter",
            index.ToString(CultureInfo.InvariantCulture));
        ShowOsd(
            _chapterList[index].DisplayName,
            $"{index + 1}/{_chapterList.Count} · {TimeFormatter.Format(_chapterList[index].Time)}");
    }

    private void PlaylistPrevious()
    {
        if (_playlistItems.Count == 0)
        {
            ShowOsd(Loc.T("osd.playlistEmpty"));
            return;
        }

        SendPlayerCommandAsync("playlist-prev");
    }

    private void PlaylistNext()
    {
        if (_playlistItems.Count == 0)
        {
            ShowOsd(Loc.T("osd.playlistEmpty"));
            return;
        }

        SendPlayerCommandAsync("playlist-next");
    }

    /// <summary>
    /// OSC transport arrows. The default is playlist skip; when the user picks
    /// 快退/快进 in settings they seek inside the current file instead, with
    /// Ctrl stretching the step to 30 seconds.
    /// </summary>
    private void OscPrevious()
    {
        if (_settings.OscPrevNextMode == OscPrevNextMode.Seek)
        {
            SeekRelative(WindowInterop.IsKeyDown(VirtualKey.Control) ? -30 : -5);
            return;
        }

        PlaylistPrevious();
    }

    private void OscNext()
    {
        if (_settings.OscPrevNextMode == OscPrevNextMode.Seek)
        {
            SeekRelative(WindowInterop.IsKeyDown(VirtualKey.Control) ? 30 : 5);
            return;
        }

        PlaylistNext();
    }

    private void CycleAbLoop()
    {
        if (!HasMedia())
        {
            return;
        }

        double position = _oscVm.Position;
        Trace($"A-B loop stage {_abLoopStage} at {position:0.###}");
        switch (_abLoopStage)
        {
            case 0:
                _abLoopA = position;
                _abLoopStage = 1;
                _oscVm.AbLoopA = position;
                ShowOsd(Loc.F("osd.abPoint", TimeFormatter.Format(position)), Loc.T("osd.abSetB"));
                break;
            case 1:
                _abLoopB = position;
                _abLoopStage = 2;
                _abLoopSeeking = false;
                _oscVm.AbLoopB = position;
                ShowOsd(
                    Loc.F("osd.pointB", TimeFormatter.Format(position)),
                    Loc.F(
                        "osd.abLoopRange",
                        TimeFormatter.Format(_abLoopA),
                        TimeFormatter.Format(position)));
                break;
            default:
                ClearAbLoop();
                break;
        }
    }

    private void ClearAbLoop()
    {
        _abLoopStage = 0;
        _abLoopA = 0;
        _abLoopB = 0;
        _abLoopSeeking = false;
        _oscVm.AbLoopA = null;
        _oscVm.AbLoopB = null;
        ShowOsd(Loc.T("osd.abOff"));
    }

    /// <summary>
    /// App-side A-B loop. mpv's ab-loop property/command hangs in the
    /// composition build after a few iterations, so seek back ourselves.
    /// </summary>
    private void MaintainAbLoop(double position)
    {
        if (_abLoopStage != 2 || _abLoopB <= _abLoopA)
        {
            return;
        }

        if (position < _abLoopA + 0.15)
        {
            _abLoopSeeking = false;
            return;
        }

        if (position < _abLoopB - 0.05)
        {
            return;
        }

        if (_abLoopSeeking ||
            (DateTime.UtcNow - _lastAbLoopSeekUtc).TotalMilliseconds < 350)
        {
            return;
        }

        _abLoopSeeking = true;
        _lastAbLoopSeekUtc = DateTime.UtcNow;
        Trace($"A-B loop seek to {_abLoopA:0.###} from {position:0.###}");
        SendPlayerCommandAsync(
            "seek",
            _abLoopA.ToString("0.###", CultureInfo.InvariantCulture),
            "absolute");
    }

    private void CycleLoopMode() => SetLoopMode((_loopMode + 1) % 3);

    private void SetLoopMode(int mode)
    {
        _loopMode = Math.Clamp(mode, 0, 2);
        switch (_loopMode)
        {
            case 0:
                _player?.TrySetPropertyString("loop-playlist", "no");
                _player?.TrySetPropertyString("loop-file", "no");
                ShowOsd(Loc.T("osd.loopOff"));
                break;
            case 1:
                _player?.TrySetPropertyString("loop-playlist", "inf");
                _player?.TrySetPropertyString("loop-file", "no");
                ShowOsd(Loc.T("osd.loopList"));
                break;
            default:
                _player?.TrySetPropertyString("loop-playlist", "no");
                _player?.TrySetPropertyString("loop-file", "inf");
                ShowOsd(Loc.T("osd.loopOne"));
                break;
        }
    }

    private void ToggleShuffle()
    {
        if (_player is null)
        {
            return;
        }

        bool shuffle = !_player.GetPropertyFlag("shuffle");
        _player.TrySetPropertyString("shuffle", shuffle ? "yes" : "no");
        ShowOsd(shuffle ? Loc.T("osd.shuffleOn") : Loc.T("osd.shuffleOff"));
    }

    private void CaptureScreenshot()
    {
        if (!HasMedia())
        {
            ShowOsd(Loc.T("osd.noMediaPlaying"));
            return;
        }

        try
        {
            string folder = GetScreenshotFolder();
            string path = Path.Combine(folder, BuildScreenshotName(folder));
            Trace($"screenshot -> {path}");
            SendPlayerCommandAsync(
                "screenshot-to-file",
                path,
                _settings.ScreenshotWithSubtitles ? "subtitles" : "video");
            ShowOsd(Loc.T("osd.screenshotSaved"), path);
        }
        catch (Exception ex)
        {
            Trace("screenshot failed: " + ex);
            ShowOsd(Loc.T("osd.screenshotFailed"), ex.Message);
        }
    }

    /// <summary>
    /// Names a capture after the playing video plus the playback position to
    /// the millisecond (<c>xxx-1-28-39-980.png</c>), so a folder of screenshots
    /// can be sorted back into the film. Falls back to the legacy
    /// <c>wiina-yyyyMMdd-HHmmss.png</c> name when nothing is playing.
    /// </summary>
    private string BuildScreenshotName(string folder)
    {
        double position = _player?.GetPropertyDouble("time-pos") ?? 0;
        return ScreenshotNaming.BuildUniqueFileName(
            _currentPath,
            position,
            DateTimeOffset.Now,
            candidate => File.Exists(Path.Combine(folder, candidate)));
    }

    private string GetScreenshotFolder()
    {
        string folder = _settings.ScreenshotFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            string pictures = Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures);
            folder = Path.Combine(
                string.IsNullOrWhiteSpace(pictures) ? Path.GetTempPath() : pictures,
                "Wiina");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    // ----------------------------------------------------- tracks/subtitles

    private void ToggleSubtitleVisibility()
    {
        if (_player is null)
        {
            return;
        }

        bool visible = !_player.GetPropertyFlag("sub-visibility");
        _player.TrySetPropertyString("sub-visibility", visible ? "yes" : "no");
        ShowOsd(visible ? Loc.T("osd.subtitleShown") : Loc.T("osd.subtitleHidden"));
    }

    private void AdjustSubtitleDelay(double delta)
    {
        if (_player is null)
        {
            return;
        }

        double next = _player.GetPropertyDouble("sub-delay") + delta;
        _player.TrySetPropertyString(
            "sub-delay",
            next.ToString("0.###", CultureInfo.InvariantCulture));
        ShowOsd(Loc.F("osd.subtitleDelay", next.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "s"));
    }

    private void ResetSubtitleDelay()
    {
        _player?.TrySetPropertyString("sub-delay", "0");
        ShowOsd(Loc.F("osd.subtitleDelay", "0.0s"));
    }

    private void AdjustSubtitlePosition(double delta)
    {
        if (_player is null)
        {
            return;
        }

        double current = _player.GetPropertyDouble("sub-pos");
        if (current <= 0)
        {
            current = 100;
        }

        double next = Math.Clamp(current + delta, 0, 150);
        _player.TrySetPropertyString(
            "sub-pos",
            Math.Round(next).ToString(CultureInfo.InvariantCulture));
        ShowOsd(Loc.F("osd.subtitlePosition", Math.Round(next)));
    }

    private void AdjustSubtitleScale(double delta)
    {
        if (_player is null)
        {
            return;
        }

        double current = _player.GetPropertyDouble("sub-scale");
        if (current <= 0)
        {
            current = 1;
        }

        double next = Math.Clamp(current + delta, 0.1, 10);
        _player.TrySetPropertyString(
            "sub-scale",
            next.ToString("0.###", CultureInfo.InvariantCulture));
        ShowOsd(Loc.F("osd.subtitleScale", next.ToString("0.##", CultureInfo.InvariantCulture) + "x"));
    }

    private void SubtitleSeek(int direction)
    {
        if (!HasMedia())
        {
            return;
        }

        SendPlayerCommandAsync(
            "sub-seek",
            direction.ToString(CultureInfo.InvariantCulture));
        ShowOsd(direction > 0 ? Loc.T("osd.subtitleNext") : Loc.T("osd.subtitlePrev"));
    }

    private void SelectTrack(TrackInfo track)
    {
        string property = TrackProperty(track.Type);
        _player?.TrySetPropertyString(
            property,
            track.Id.ToString(CultureInfo.InvariantCulture));
        Trace($"select track {track.Type} {track.Id}");
        ShowOsd(Loc.F("osd.track", TrackTypeLabel(track.Type)), track.DisplayName);
    }

    private void SetTrackSelection(TrackType type, string value, string label)
    {
        _player?.TrySetPropertyString(TrackProperty(type), value);
        ShowOsd(Loc.F("osd.track", TrackTypeLabel(type)), label);
    }

    private static string TrackProperty(TrackType type) => type switch
    {
        TrackType.Video => "vid",
        TrackType.Audio => "aid",
        _ => "sid",
    };

    private static string TrackTypeLabel(TrackType type) => type switch
    {
        TrackType.Video => Loc.T("osd.trackVideo"),
        TrackType.Audio => Loc.T("osd.trackAudio"),
        _ => Loc.T("osd.trackSubtitle"),
    };

    private void SetSecondarySubtitle(int? id)
    {
        _player?.TrySetPropertyString(
            "secondary-sid",
            id is null ? "no" : id.Value.ToString(CultureInfo.InvariantCulture));
        ShowOsd(
            Loc.T("osd.secondarySubtitle"),
            id is null ? Loc.T("menu.off") : Loc.F("osd.trackNumber", id.Value));
    }

    private void AddExternalSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowOsd(Loc.T("osd.subtitleFileMissing"), path);
            return;
        }

        if (!HasMedia())
        {
            _pendingSubtitles.Add(path);
            ShowOsd(Loc.T("osd.subtitleQueued"), Path.GetFileName(path));
            return;
        }

        Trace($"sub-add {path}");
        SendPlayerCommandAsync("sub-add", path, "select");
        _wantSubtitleSelection = true;
        ShowOsd(Loc.T("osd.subtitleLoaded"), Path.GetFileName(path));
    }

    private void FlushPendingSubtitles()
    {
        if (_pendingSubtitles.Count == 0)
        {
            return;
        }

        foreach (string path in _pendingSubtitles)
        {
            SendPlayerCommandAsync("sub-add", path, "select");
        }

        _wantSubtitleSelection = true;
        SendPlayerCommandAsync("set", "sub-visibility", "yes");
        Trace(
            $"subtitles flushed; sid={_player?.GetPropertyInt64("sid")} " +
            $"visible={_player?.GetPropertyFlag("sub-visibility")}");
        ShowOsd(Loc.T("osd.subtitlesLoaded"), Loc.F("osd.fileCount", _pendingSubtitles.Count));
        _pendingSubtitles.Clear();
    }

    // ------------------------------------------------------ smart sidecars

    /// <summary>
    /// Starts background sidecar discovery for the freshly loaded file. The
    /// pure filesystem lookup runs off the UI thread; the (small) apply step is
    /// marshalled back and re-validated so a fast next/previous cannot load a
    /// stale subtitle or audio track.
    /// </summary>
    private void ScheduleSmartSidecarLoad(string? path, bool hadPendingSubtitles)
    {
        int generation = ++_smartLoadGeneration;
        _autoLoadedSubtitlePath = null;
        _autoLoadedAudioPath = null;
        if (string.IsNullOrWhiteSpace(path) || IsUrlPath(path))
        {
            return;
        }

        bool findSubtitle = _settings.AutoLoadSmartSubtitles && !hadPendingSubtitles;
        bool findAudio = _settings.AutoLoadExternalAudio;
        if (!findSubtitle && !findAudio)
        {
            return;
        }

        string mediaPath = path;
        string preferred = _settings.PreferredSubtitleLanguage;
        _ = Task.Run(() =>
        {
            SubtitleCandidate? subtitle = null;
            ExternalAudioCandidate? audio = null;
            try
            {
                if (findSubtitle)
                {
                    subtitle = SmartSubtitleFinder.FindBest(mediaPath, preferred);
                }

                if (findAudio)
                {
                    audio = ExternalAudioFinder.FindBest(mediaPath);
                }
            }
            catch (Exception ex)
            {
                Trace("sidecar discovery failed: " + ex.Message);
            }

            if (subtitle is null && audio is null)
            {
                Trace("smart sidecar discovery found nothing");
                return;
            }

            Trace(
                $"smart sidecar discovery: subtitle={subtitle?.Path ?? "<none>"} " +
                $"audio={audio?.Path ?? "<none>"} preferred={preferred}");

            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _smartLoadGeneration || !IsCurrentMedia(mediaPath))
                {
                    return;
                }

                if (subtitle is not null)
                {
                    ApplyAutoSubtitle(subtitle.Path);
                }

                if (audio is not null)
                {
                    ApplyAutoAudio(audio.Path);
                }
            });
        });
    }

    private void ApplyAutoSubtitle(string path)
    {
        if (!_settings.AutoLoadSmartSubtitles || _pendingSubtitles.Count > 0)
        {
            return;
        }

        string fullPath = SafeFullPath(path);
        if (string.Equals(_autoLoadedSubtitlePath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // mpv's sub-auto may already have added this exact sidecar. Select the
        // existing track instead of adding a duplicate, so the preferred
        // language still wins when mpv picked a different sidecar first.
        string fileName = Path.GetFileName(path);
        string mediaBase = Path.GetFileNameWithoutExtension(_currentPath ?? string.Empty);
        IReadOnlyList<TrackInfo> tracks = _player?.GetTracks() ?? _trackList;
        Trace(
            $"smart subtitle candidate {fileName}; " +
            "external=" + string.Join(
                " | ",
                tracks.Where(track => track.Type == TrackType.Subtitle && track.IsExternal)
                      .Select(track => $"#{track.Id} '{track.Title}' sel={track.IsSelected}")));
        TrackInfo? existing = tracks.FirstOrDefault(track =>
            track.Type == TrackType.Subtitle &&
            track.IsExternal &&
            !string.IsNullOrWhiteSpace(track.Title) &&
            (string.Equals(track.Title, fileName, StringComparison.OrdinalIgnoreCase) ||
             (mediaBase.Length > 0 &&
              string.Equals(
                  fileName,
                  mediaBase + "." + track.Title,
                  StringComparison.OrdinalIgnoreCase))));
        if (existing is not null)
        {
            Trace($"smart subtitle use existing sid={existing.Id} selected={existing.IsSelected}");
            _autoLoadedSubtitlePath = fullPath;
            if (!existing.IsSelected)
            {
                if (_player?.TrySetPropertyString("sid", existing.Id.ToString()) == true)
                {
                    ShowOsd(Loc.F("osd.autoSubtitleLoaded", fileName));
                }
                else
                {
                    Trace($"smart sid switch failed for {path}");
                }
            }
            else
            {
                ShowOsd(Loc.F("osd.autoSubtitleLoaded", fileName));
            }

            return;
        }

        if (HasSelectedExternalTrack(TrackType.Subtitle))
        {
            Trace("smart subtitle skipped: another external subtitle is active");
            return;
        }

        _autoLoadedSubtitlePath = fullPath;
        Trace($"smart sub-add {path}");
        SendPlayerCommandAsync("sub-add", path, "select");
        _wantSubtitleSelection = true;
        ShowOsd(Loc.F("osd.autoSubtitleLoaded", Path.GetFileName(path)));
    }

    private void ApplyAutoAudio(string path)
    {
        if (!_settings.AutoLoadExternalAudio || HasSelectedExternalTrack(TrackType.Audio))
        {
            return;
        }

        string fullPath = SafeFullPath(path);
        if (string.Equals(_autoLoadedAudioPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _autoLoadedAudioPath = fullPath;
        Trace($"smart audio-add {path}");
        AddExternalAudioCore(path, automatic: true);
    }

    private async Task OpenExternalAudioPickerAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            foreach (string extension in ExternalAudioFinder.PickerExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            InitializePicker(picker);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                AddExternalAudio(file.Path);
            }
        }
        catch (Exception ex)
        {
            Trace("open external audio picker failed: " + ex);
            ShowOsd(Loc.T("osd.loadAudioFailed"), ex.Message);
        }
    }

    private void AddExternalAudio(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowOsd(Loc.T("osd.audioFileMissing"), path ?? string.Empty);
            return;
        }

        if (!HasMedia())
        {
            ShowOsd(Loc.T("osd.noMediaForAudio"));
            return;
        }

        _autoLoadedAudioPath = SafeFullPath(path);
        AddExternalAudioCore(path, automatic: false);
    }

    private void AddExternalAudioCore(string path, bool automatic)
    {
        try
        {
            if (_player is not { IsRunning: true })
            {
                ShowOsd(Loc.T("osd.loadAudioFailed"));
                return;
            }

            _player.Command("audio-add", path, "select");
        }
        catch (Exception ex) when (ex is MpvException or InvalidOperationException)
        {
            Trace($"audio-add failed, falling back to audio-file: {ex.Message}");
            if (_player?.TrySetPropertyString("audio-file", path) != true)
            {
                ShowOsd(Loc.T("osd.loadAudioFailed"), ex.Message);
                return;
            }
        }

        _autoLoadedAudioPath = SafeFullPath(path);
        ShowOsd(
            automatic ? Loc.F("osd.autoAudioLoaded", Path.GetFileName(path))
                      : Loc.F("osd.externalAudioLoaded", Path.GetFileName(path)));
    }

    private bool HasSelectedExternalTrack(TrackType type)
    {
        IReadOnlyList<TrackInfo> tracks = _player?.GetTracks() ?? _trackList;
        return tracks.Any(track => track.Type == type && track.IsExternal && track.IsSelected);
    }

    private bool IsCurrentMedia(string path)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            return false;
        }

        return string.Equals(
            SafeFullPath(path),
            SafeFullPath(_currentPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    // ------------------------------------------------------ event handlers

    private void OnTrackListChanged(IReadOnlyList<TrackInfo> tracks)
    {
        Trace($"track-list: {string.Join(", ", tracks.Select(track => $"{track.Type}#{track.Id}"))}; sid={_player?.GetPropertyInt64("sid")}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _trackList.Clear();
            _trackList.AddRange(tracks);
            if (_wantSubtitleSelection)
            {
                TrackInfo? subtitle = tracks.LastOrDefault(
                    track => track.Type == TrackType.Subtitle);
                if (subtitle is not null)
                {
                    _wantSubtitleSelection = false;
                    SendPlayerCommandAsync(
                        "set",
                        "sid",
                        subtitle.Id.ToString(CultureInfo.InvariantCulture));
                    SendPlayerCommandAsync("set", "sub-visibility", "yes");
                    Trace(
                        $"selected subtitle #{subtitle.Id}; " +
                        $"visible={_player?.GetPropertyFlag("sub-visibility")}");
                }
            }

            RefreshSidebarTracks();
            UpdateAlbumArtState();
            UpdateMiniPlayerState();
        });
    }

    private void OnChapterListChanged(IReadOnlyList<ChapterInfo> chapters)
    {
        Trace($"chapter-list: {chapters.Count}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _chapterList.Clear();
            _chapterList.AddRange(chapters);
            _chapters = _chapterList;
            _oscVm.Chapters = _chapterList.Select(chapter => chapter.Time).ToArray();
            UpdateTimelines();
            RefreshSidebarChapters();
        });
    }

    private void OnPlaylistChanged(IReadOnlyList<MpvPlaylistItem> items)
    {
        Trace($"playlist: {items.Count}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _playlistItems.Clear();
            _playlistItems.AddRange(items);
            int current = items.ToList().FindIndex(item => item.IsCurrent || item.IsPlaying);
            if (current >= 0)
            {
                _currentPlaylistIndex = current;
            }

            RefreshSidebarPlaylist();
            UpdateMiniPlaylist();
        });
    }

    private void OnPlaylistPositionChanged(int position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _currentPlaylistIndex = position;
            UpdateSidebarPlaylistPosition(position);
            UpdateMiniPlaylist();
        });
    }

    private void OnAudioDeviceListChanged(IReadOnlyList<MpvAudioDevice> devices)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _audioDevices.Clear();
            _audioDevices.AddRange(devices);
            RefreshSidebarDevices();
        });
    }

    private void OnChapterChanged(int chapter)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _currentChapterIndex = chapter;
            UpdateSidebarChapter(chapter);
            if (_chapterChangeOsc && chapter >= 0 && chapter < _chapterList.Count)
            {
                ChapterInfo info = _chapterList[chapter];
                ShowOsd(
                    info.DisplayName,
                    $"{chapter + 1}/{_chapterList.Count} · {TimeFormatter.Format(info.Time)}");
            }

            _chapterChangeOsc = false;
        });
    }

    private void OnMetadataChanged(IReadOnlyDictionary<string, string> metadata)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _artist = metadata.TryGetValue("artist", out string? artist) ? artist : null;
            _album = metadata.TryGetValue("album", out string? album) ? album : null;
            if (metadata.TryGetValue("title", out string? title) &&
                !string.IsNullOrWhiteSpace(title))
            {
                _currentTitle = title;
                SetWindowTitle(title);
            }

            UpdateMiniPlayerState();
        });
    }

    private void OnVideoSizeChanged(int width, int height)
    {        if (width <= 0 || height <= 0)
        {
            return;
        }

        Trace($"video size: {width}x{height}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _videoWidth = width;
            _videoHeight = height;
            _videoAspect = (double)width / height;
            foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
            {
                host.QuickSettings.SetVideoSize(width, height);
            }

            ApplyCropFromSettings();
            TryApplyInitialVideoSize();
        });
    }

    /// <summary>
    /// Sizes the window to the first video's display resolution once. The frame
    /// size maps 1:1 to physical window pixels; oversized frames are halved and
    /// then capped to the monitor work area. Never overrides a user resize.
    /// </summary>
    private void TryApplyInitialVideoSize()
    {
        if (_initialVideoSizingDone)
        {
            return;
        }

        if (_userResizedWindow || _mode.Current != PlayerMode.Normal || IsFullscreen)
        {
            Trace($"initial size skipped: userResized={_userResizedWindow} mode={_mode.Current} fullscreen={IsFullscreen}");
            _initialVideoSizingDone = true;
            return;
        }

        if (_videoWidth <= 0 || _videoHeight <= 0)
        {
            return;
        }

        int displayWidth = (int)(_player?.GetPropertyInt64("video-params/dw") ?? 0);
        int displayHeight = (int)(_player?.GetPropertyInt64("video-params/dh") ?? 0);
        if (displayWidth <= 0)
        {
            displayWidth = (int)(_player?.GetPropertyInt64("dwidth") ?? 0);
        }

        if (displayHeight <= 0)
        {
            displayHeight = (int)(_player?.GetPropertyInt64("dheight") ?? 0);
        }

        if (displayWidth <= 0)
        {
            displayWidth = _videoWidth;
        }

        if (displayHeight <= 0)
        {
            displayHeight = _videoHeight;
        }

        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 workArea = display.WorkArea;
        DipSize? size = WindowSizing.InitialVideoWindowSize(
            displayWidth, displayHeight, scale, workArea.Width, workArea.Height);
        if (size is null)
        {
            return;
        }

        (int physicalWidth, int physicalHeight) = WindowSizing.ToPhysical(size.Value, scale);
        physicalWidth = Math.Min(physicalWidth, workArea.Width);
        physicalHeight = Math.Min(physicalHeight, workArea.Height);

        _initialVideoSizingDone = true;
        AppWindow.Resize(new SizeInt32(physicalWidth, physicalHeight));

        PointInt32 position = AppWindow.Position;
        int x = Math.Clamp(
            position.X, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - physicalWidth));
        int y = Math.Clamp(
            position.Y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - physicalHeight));
        if (x != position.X || y != position.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }

        Trace($"initial video window: {displayWidth}x{displayHeight} -> {physicalWidth}x{physicalHeight}px ({scale:0.##}x)");
    }

    private void OnFileLoadFailed(int error, string? message)
    {
        Trace($"file load failed: {error} {message}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _smartLoadGeneration++;
            _autoLoadedSubtitlePath = null;
            _autoLoadedAudioPath = null;
            _pendingSubtitles.Clear();
            TitleBar.Title = Loc.T("osd.openFailedTitle");
            Title = "Wiina";
            _currentPath = null;
            _currentTitle = null;
            EndStreamOpen();
            ShowOsd(OsdMessages.Error(
                Loc.T("osd.cannotOpenMedia"),
                string.IsNullOrWhiteSpace(message) ? Loc.F("osd.mpvError", error) : message));
            UpdateSmtc();
        });
    }

    private void OnCommandFailed(string description, string? message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Trace($"mpv async command failed ({description}): {message}");
            if (description.StartsWith("loadfile", StringComparison.Ordinal) ||
                description.StartsWith("loadlist", StringComparison.Ordinal) ||
                description.StartsWith("sub-add", StringComparison.Ordinal) ||
                description.StartsWith("sub-seek", StringComparison.Ordinal))
            {
                ShowOsd(Loc.T("osd.playbackCommandFailed"), message ?? description);
            }
            else if (description.StartsWith("screenshot", StringComparison.Ordinal))
            {
                ShowOsd(Loc.T("osd.screenshotFailed"), message ?? description);
            }
        });
    }

    private void OnStage3NumberProperty(string name, double value)
    {
        if (name == "duration")
        {
            UpdateSmtc();
            StartThumbnailPreGeneration();
        }
    }

    private void OnStage3FlagProperty(string name, bool value)
    {
        if (name == "eof-reached" && value)
        {
            SaveCurrentHistory();
        }
        else if (name == "sub-visibility")
        {
            _settings.SubtitleVisible = value;
            SyncSidebarSubtitleVisibility();
        }
    }

    private void OnStage3StringProperty(string name, string? value)
    {
        if (name == "audio-device")
        {
            _currentAudioDevice = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                _settings.AudioDevice = value;
            }

            SyncSidebarAudioDevice(value);
        }
    }

    private void OnMediaLoadedStage3()
    {
        if (_player is null)
        {
            return;
        }

        Trace("OnMediaLoadedStage3 begin");
        try
        {
            _currentPath = _player.GetPropertyString("path");
            Trace($"file loaded: path={_currentPath} title={_currentTitle}");
            if (string.IsNullOrWhiteSpace(_currentTitle) &&
                !string.IsNullOrWhiteSpace(_currentPath))
            {
                _currentTitle = Path.GetFileName(_currentPath);
                SetWindowTitle(_currentTitle);
            }

            _resumeApplied = false;
            TryResumePlayback();
            bool hadPendingSubtitles = _pendingSubtitles.Count > 0;
            FlushPendingSubtitles();
            ScheduleSmartSidecarLoad(_currentPath, hadPendingSubtitles);
            UpdateAlbumArtState();
            UpdateMiniPlayerState();
            UpdateMiniPlaylist();
            UpdateSmtc();
            StartThumbnailPreGeneration();
        }
        catch (Exception ex)
        {
            Trace("OnMediaLoadedStage3 failed: " + ex);
        }

        Trace("OnMediaLoadedStage3 end");
    }

    private void TryResumePlayback()
    {
        if (!_settings.ResumePlayback ||
            _resumeApplied ||
            _player is null ||
            string.IsNullOrWhiteSpace(_currentPath) ||
            IsUrlPath(_currentPath))
        {
            return;
        }

        double duration = _player.GetPropertyDouble("duration");
        HistoryEntry? entry = _historyStore.Find(_currentPath);
        if (entry is null || entry.Position < 5 || duration <= 0 ||
            entry.Position >= duration - 5)
        {
            return;
        }

        _resumeApplied = true;
        Trace($"resume at {entry.Position:0.###}");
        SendPlayerCommandAsync(
            "seek",
            entry.Position.ToString("0.###", CultureInfo.InvariantCulture),
            "absolute");
        ShowOsd(
            Loc.F("osd.resumeFrom", TimeFormatter.Format(entry.Position)),
            _currentTitle);
    }

    private static bool IsUrlPath(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && !uri.IsFile;

    private void SaveCurrentHistory()
    {
        try
        {
            if (_player is null ||
                string.IsNullOrWhiteSpace(_currentPath) ||
                !_player.IsRunning)
            {
                return;
            }

            double duration = _player.GetPropertyDouble("duration");
            double position = _player.GetPropertyDouble("time-pos");
            if (duration <= 0 || position <= 0)
            {
                return;
            }

            _historyStore.Record(new HistoryEntry
            {
                Path = _currentPath,
                Title = _currentTitle ?? Path.GetFileName(_currentPath),
                Duration = duration,
                Position = position >= duration - 3 ? 0 : position,
                LastPlayedUtc = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            Trace("save history failed: " + ex.Message);
        }
    }

    private void SetWindowTitle(string? title)
    {
        string resolved = string.IsNullOrWhiteSpace(title) ? "Wiina" : title;
        TitleBar.Title = resolved;
        Title = resolved;
        App.UpdateWindowTitle(this, resolved);
        _pipWindow?.SetTitle(resolved);
        UpdateMiniPlayerState();
    }

    private void ShowOsd(string message, string? secondary = null)
    {
        Osd.Show(message, secondary);
    }

    private void UpdateSmtc()
    {
        App.PublishMediaState(
            this,
            !string.IsNullOrWhiteSpace(_currentPath),
            _oscVm.IsPlaying,
            _currentTitle,
            _oscVm.Duration);
    }

    private static bool TryNormalizeUrl(string text, out string normalized)
    {
        if (UrlValidation.TryNormalize(text, out normalized))
        {
            return true;
        }

        // mpv also accepts synthetic protocols that are not HTTP URLs.
        string candidate = text.Trim();
        if (candidate.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("av://", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("edl://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = candidate;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    // ----------------------------------------------------------------- input

    private bool IsTextInputFocused()
    {
        object? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox;
    }

    /// <summary>
    /// Removes keyboard focusability from the media chrome (OSC, titlebar, mini
    /// player) so Space/Enter keep their player meaning: with a focused button,
    /// Space presses that button instead of pausing playback. Buttons stay fully
    /// clickable, and every chrome action also has a keyboard shortcut, so
    /// nothing becomes unreachable.
    /// </summary>
    private void MakeMediaChromeKeyboardTransparent()
    {
        foreach (FrameworkElement root in new FrameworkElement[] { OscBottom, OscFloating, TitleBar, MiniPlayer })
        {
            MakeNonFocusable(root);
            // Items created by templates/lists appear after layout.
            root.Loaded += (_, _) => MakeNonFocusable(root);
        }
    }

    private static void MakeNonFocusable(DependencyObject root)
    {
        if (root is Button button)
        {
            button.IsTabStop = false;
            button.AllowFocusOnInteraction = false;
        }
        else if (root is Slider slider)
        {
            // Sliders must stay draggable; they only need to stay out of the
            // Tab order (a focused slider still lets Space bubble to the player).
            slider.IsTabStop = false;
        }
        else if (root is Control control)
        {
            // Containers such as ListView, ScrollViewer and realised items are
            // tab stops in their own right, and a lazily created item never
            // passes through this walk in time. The preview handler is the
            // backstop; this just keeps every realised control out of the order.
            control.IsTabStop = false;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            MakeNonFocusable(VisualTreeHelper.GetChild(root, i));
        }
    }

    /// <summary>
    /// Mini mode and fullscreen are keyboard-driven, so no chrome control may
    /// keep focus or see a key before the player does. This runs in the
    /// tunneling preview phase: Tab is swallowed and focus is forced back to
    /// the root, while every other key goes through the same key map as the
    /// root handler, so Space/Enter keep pausing/entering fullscreen even when
    /// WinUI managed to focus a button or a lazily realised playlist container.
    /// </summary>
    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!InMiniMode && !IsFullscreen)
        {
            return;
        }

        if (IsTextInputFocused())
        {
            return;
        }

        if (e.Key == VirtualKey.Tab)
        {
            Trace($"mini tab: before={DescribeFocusedElement()}");
            e.Handled = true;
            RootGrid.Focus(FocusState.Programmatic);
            Trace($"mini tab: after={DescribeFocusedElement()}");
            return;
        }

        Trace($"key down {e.Key} (preview)");
        if (TryExecuteShortcut(e.Key))
        {
            // Keep the key away from whatever still holds focus: a button would
            // treat Space/Enter as "press me" and a list would scroll instead.
            e.Handled = true;
        }
    }

    /// <summary>
    /// Focus guard for the keyboard-driven modes. Whatever moved focus onto a
    /// chrome control (Tab in a lazily realised list, automation, a template
    /// part created after the recursive pass), the player keeps the keyboard:
    /// focus is pushed back to the root so Space/Enter can never press the
    /// focused button. Pointer input is unaffected: a click still reaches the
    /// control, it just does not keep keyboard focus.
    /// </summary>
    private void OnRootGotFocus(object sender, RoutedEventArgs e)
    {
        if (!InMiniMode && !IsFullscreen)
        {
            return;
        }

        object? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        if (focused is null || ReferenceEquals(focused, RootGrid))
        {
            return;
        }

        if (focused is not Control control || control is TextBox or RichEditBox or AutoSuggestBox or NumberBox)
        {
            // Panels and non-focusable elements (or a legitimate text editor)
            // are not the problem this guard is about.
            return;
        }

        Trace($"focus guard: {DescribeFocusedElement()} -> RootGrid");
        RootGrid.Focus(FocusState.Programmatic);
    }

    private string DescribeFocusedElement()
    {
        try
        {
            object? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
            if (focused is null)
            {
                return "<null>";
            }

            if (focused is FrameworkElement element)
            {
                string name = string.IsNullOrEmpty(element.Name) ? "-" : element.Name;
                string id = AutomationProperties.GetAutomationId(element);
                id = string.IsNullOrEmpty(id) ? "-" : id;
                return $"{element.GetType().Name} name={name} id={id}";
            }

            return focused.GetType().Name;
        }
        catch (Exception ex)
        {
            return $"<{ex.GetType().Name}>";
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        Trace($"key down {e.Key}");
        if (TryExecuteShortcut(e.Key))
        {
            e.Handled = true;
        }
    }

    private bool TryExecuteShortcut(VirtualKey key)
    {        KeyModifiers modifiers = KeyModifiers.None;
        if (WindowInterop.IsKeyDown(VirtualKey.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.Menu))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (WindowInterop.IsKeyDown(VirtualKey.LeftWindows) ||
            WindowInterop.IsKeyDown(VirtualKey.RightWindows))
        {
            modifiers |= KeyModifiers.Windows;
        }

        if (!KeyChordMapper.TryMap(key, modifiers, out KeyChord chord) ||
            !_keyMap.TryGetAction(chord, out string? action) ||
            string.IsNullOrEmpty(action))
        {
            return false;
        }

        Trace($"action {action} ({chord})");
        return ExecuteAction(action);
    }

    /// <summary>
    /// Task-58: the picture-in-picture window forwards keys here so a focused PiP
    /// surface shares this window's key map and action executor instead of
    /// keeping a second copy of the binding table. Returns true when a binding
    /// ran (the caller marks the key handled); false means the key is unbound and
    /// must stay untouched for IME / system shortcuts.
    /// </summary>
    internal bool TryForwardShortcutKey(VirtualKey key) => TryExecuteShortcut(key);

    private bool ExecuteAction(string action)
    {
        switch (action)
        {
            case PlayerActions.PlayPause:
                TogglePlayPause();
                return true;
            case PlayerActions.Stop:
                StopPlayback();
                return true;
            case PlayerActions.SeekBack5:
                SeekRelative(-5);
                return true;
            case PlayerActions.SeekForward5:
                SeekRelative(5);
                return true;
            case PlayerActions.SeekBack1:
                SeekRelative(-1);
                return true;
            case PlayerActions.SeekForward1:
                SeekRelative(1);
                return true;
            case PlayerActions.VolumeUp5:
                AdjustVolume(5);
                return true;
            case PlayerActions.VolumeDown5:
                AdjustVolume(-5);
                return true;
            case PlayerActions.VolumeUp1:
                AdjustVolume(1);
                return true;
            case PlayerActions.VolumeDown1:
                AdjustVolume(-1);
                return true;
            case PlayerActions.Mute:
                ToggleMute();
                return true;
            case PlayerActions.FramePrevious:
                FrameStep(false);
                return true;
            case PlayerActions.FrameNext:
                FrameStep(true);
                return true;
            case PlayerActions.SubtitleSeekPrevious:
                SubtitleSeek(-1);
                return true;
            case PlayerActions.SubtitleSeekNext:
                SubtitleSeek(1);
                return true;
            case PlayerActions.Open:
                _ = OpenFilePickerAsync();
                return true;
            case PlayerActions.OpenUrl:
                _ = OpenUrlDialogAsync();
                return true;
            case PlayerActions.Settings:
                App.ShowSettingsWindow();
                return true;
            case PlayerActions.Screenshot:
                CaptureScreenshot();
                return true;
            case PlayerActions.AbLoop:
                CycleAbLoop();
                return true;
            case PlayerActions.PlaylistPrevious:
                PlaylistPrevious();
                return true;
            case PlayerActions.PlaylistNext:
                PlaylistNext();
                return true;
            case PlayerActions.SpeedHalf:
                MultiplySpeed(0.5);
                return true;
            case PlayerActions.SpeedDouble:
                MultiplySpeed(2.0);
                return true;
            case PlayerActions.SpeedReset:
                ResetSpeed();
                return true;
            case PlayerActions.ToggleFullscreen:
                ToggleFullscreen();
                return true;
            case PlayerActions.ExitFullscreen:
                if (_mode.Current != PlayerMode.Normal ||
                    AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    RequestMode(PlayerMode.Normal);
                }

                return true;
            case PlayerActions.PictureInPicture:
                RequestMode(
                    _mode.Current == PlayerMode.PiP ? PlayerMode.Normal : PlayerMode.PiP);
                return true;
            case PlayerActions.Sidebar:
                ToggleSidebar();
                return true;
            case PlayerActions.SidebarSettings:
                ShowSettingsSidebar();
                return true;
            case PlayerActions.Layout:
                ToggleLayout();
                return true;
            case PlayerActions.MusicMode:
                RequestMode(
                    _mode.Current == PlayerMode.Mini ? PlayerMode.Normal : PlayerMode.Mini);
                return true;
            case PlayerActions.Theme:
                CycleTheme();
                return true;
            case PlayerActions.OscPosition:
                CycleOsc();
                return true;
            case PlayerActions.LoopMode:
                CycleLoopMode();
                return true;
            case PlayerActions.ChapterPrevious:
                PreviousChapter();
                return true;
            case PlayerActions.ChapterNext:
                NextChapter();
                return true;
            case PlayerActions.SubtitleVisibility:
                ToggleSubtitleVisibility();
                return true;
            case PlayerActions.SubtitleDelayDecrease:
                AdjustSubtitleDelay(-0.1);
                return true;
            case PlayerActions.SubtitleDelayIncrease:
                AdjustSubtitleDelay(0.1);
                return true;
            case PlayerActions.SubtitleDelayReset:
                ResetSubtitleDelay();
                return true;
            case PlayerActions.SubtitleScaleIncrease:
                AdjustSubtitleScale(0.1);
                return true;
            case PlayerActions.SubtitleScaleDecrease:
                AdjustSubtitleScale(-0.1);
                return true;
            case PlayerActions.SubtitlePositionUp:
                AdjustSubtitlePosition(5);
                return true;
            case PlayerActions.SubtitlePositionDown:
                AdjustSubtitlePosition(-5);
                return true;
            case PlayerActions.WindowHalf:
                ResizeToScale(0.5);
                return true;
            case PlayerActions.WindowNormal:
                ResizeToScale(1.0);
                return true;
            case PlayerActions.WindowDouble:
                ResizeToScale(2.0);
                return true;
            case PlayerActions.WindowShrink:
                ScaleWindow(0.9);
                return true;
            case PlayerActions.WindowGrow:
                ScaleWindow(1.1);
                return true;
            case PlayerActions.NewWindow:
                NewWindow();
                return true;
            case PlayerActions.OpenInNewWindow:
                _ = OpenInNewWindowAsync();
                return true;
            case PlayerActions.CloseWindow:
                CloseCurrentWindow();
                return true;
            case PlayerActions.Quit:
                Close();
                return true;
            default:
                return TryExecuteShortcutAction(action);
        }
    }

    private void ScaleWindow(double factor)
    {
        SizeInt32 size = AppWindow.Size;
        AppWindow.Resize(new SizeInt32(
            Math.Max(285, (int)Math.Round(size.Width * factor)),
            Math.Max(120, (int)Math.Round(size.Height * factor))));
    }

    private void ExecuteSingleClick()
    {
        if (_suppressClick || _windowDragging)
        {
            return;
        }

        switch (_settings.SingleClickAction)
        {
            case SingleClickAction.HideOsc:
                Trace($"single click hideOsc (shown={_chromeShown})");
                if (_chromeShown)
                {
                    HideChrome(force: true);
                }
                else
                {
                    ShowChrome();
                }

                break;
            case SingleClickAction.TogglePlayPause:
                TogglePlayPause();
                break;
            case SingleClickAction.Fullscreen:
                ToggleFullscreen();
                break;
        }
    }

    private void OnVideoTapped(object sender, TappedRoutedEventArgs e)
    {
        Trace("video tapped");
        if (HasVisibleSidebar &&
            _settings.Layout == WindowLayoutMode.EdgeToEdge)
        {
            // Spec §7.2/§7.8: edge-to-edge consumes the click and closes the
            // sidebars; docked layout leaves them open.
            HideSidebar();
            e.Handled = true;
            return;
        }

        if (_suppressClick || _windowDragging)
        {
            return;
        }

        _clickTimer?.Stop();
        if (_clickTimer is not null)
        {
            _clickTimer.Interval = TimeSpan.FromMilliseconds(
                WindowInterop.GetDoubleClickMilliseconds());
            _clickTimer.Start();
        }
    }

    private void OnVideoDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _clickTimer?.Stop();
        ToggleFullscreen();
        e.Handled = true;
    }

    private void OnVideoRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        _clickTimer?.Stop();
        TogglePlayPause();
        e.Handled = true;
    }

    private void OnVideoPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(VideoFrame);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Fullscreen has no draggable frame: moving or resizing the window would
        // make Windows drop the fullscreen presenter, so a drag on the video
        // must stay a click. (Clicks/taps are handled by Tapped, not here.)
        if (IsFullscreen)
        {
            RootGrid.Focus(FocusState.Pointer);
            return;
        }

        _suppressClick = false;
        _windowDragCandidate = true;
        _windowDragging = false;
        _dragStartPoint = e.GetCurrentPoint(RootGrid).Position;
        WindowInterop.TryGetCursorPosition(out _dragStartCursorX, out _dragStartCursorY);
        _windowStartX = AppWindow.Position.X;
        _windowStartY = AppWindow.Position.Y;
        // The video surface is a second drag path (issue 4); it shares the edge
        // magnet with the native title-bar drag, starting from a clean gesture.
        _snapSession.Reset();
        RootGrid.Focus(FocusState.Pointer);
        _dragPump?.Start();
    }

    private void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowDragCandidate && !_windowDragging)
        {
            return;
        }

        DragWindowFromCursor();
    }

    /// <summary>
    /// One content-drag step, driven either by a pointer message or by the drag
    /// pump. Moving the window can make the XAML pointer lose its capture, so the
    /// drag must not depend on receiving any further PointerMoved messages: the
    /// pump keeps polling the cursor while the left button is down.
    /// </summary>
    private void DragWindowFromCursor()
    {
        // Mode may have switched to fullscreen mid-gesture (shortcut, OSC, or
        // media end); never move the window from a fullscreen frame.
        if (IsFullscreen)
        {
            _windowDragCandidate = false;
            _windowDragging = false;
            _dragPump?.Stop();
            return;
        }

        if (!WindowInterop.TryGetCursorPosition(out int cursorX, out int cursorY))
        {
            return;
        }

        if (!_windowDragging)
        {
            double dx = cursorX - _dragStartCursorX;
            double dy = cursorY - _dragStartCursorY;
            double threshold = 3 * Math.Max(0.1, DisplayScale);
            if (Math.Sqrt((dx * dx) + (dy * dy)) < threshold)
            {
                return;
            }

            _windowDragging = true;
            Trace("window drag started");

            // A maximized window must restore like a native title-bar drag; the
            // video surface is our own drag path, so it has to restore itself.
            // The restore rebases the gesture, so the delta is taken afterwards.
            TryRestoreFromMaximized(cursorX, cursorY);
        }

        int x = _windowStartX + (cursorX - _dragStartCursorX);
        int y = _windowStartY + (cursorY - _dragStartCursorY);
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

        // Do not re-issue Move while the magnet holds the window in place:
        // an unchanged position is already on screen, and moving it again
        // would only add work (and a chance of a DWM flicker) per pointer
        // message.
        PointInt32 currentPosition = AppWindow.Position;
        if (x != currentPosition.X || y != currentPosition.Y)
        {
            AppWindow.Move(new PointInt32(x, y));
        }
    }

    /// <summary>
    /// Keeps the content drag alive across the window moves it performs: those
    /// moves drop the XAML pointer capture, so waiting for PointerMoved alone
    /// made the window stop following the cursor after the first pixel.
    /// </summary>
    private void OnDragPumpTick(DispatcherQueueTimer sender, object args)
    {
        if (!_windowDragCandidate && !_windowDragging)
        {
            sender.Stop();
            return;
        }

        if (!WindowInterop.IsKeyDown(VirtualKey.LeftButton))
        {
            sender.Stop();
            _windowDragCandidate = false;
            _windowDragging = false;
            return;
        }

        DragWindowFromCursor();
    }

    private void OnVideoPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragPump?.Stop();

        if (_windowDragging)
        {
            _suppressClick = true;
            _suppressClickTimer?.Stop();
            _suppressClickTimer?.Start();
        }

        _windowDragCandidate = false;
        _windowDragging = false;
        try
        {
            VideoFrame.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // Pointer may already be gone.
        }
    }

    private void OnVideoPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // The drag itself moves the window, which drops the XAML pointer capture;
        // that must not cancel an in-flight drag. The drag pump owns the gesture
        // from now on and finishes when the left button is released.
        if (_windowDragging)
        {
            return;
        }

        _windowDragCandidate = false;
        _dragPump?.Stop();
    }

    private void OnVideoPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Mini mode handles its own wheel-to-volume bubble; PiP has no video.
        if (_mode.Current != PlayerMode.Normal)
        {
            return;
        }

        Point point = e.GetCurrentPoint(RootGrid).Position;
        if (IsOverChrome(point))
        {
            return;
        }

        var properties = e.GetCurrentPoint(RootGrid).Properties;
        int delta = properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        int direction = Math.Sign(delta);
        if (_settings.InvertWheelDirection)
        {
            direction = -direction;
        }
        if (properties.IsHorizontalMouseWheel)
        {
            SeekRelative(direction * _settings.WheelSeekSeconds);
        }
        else
        {
            AdjustUiVolume(direction * _settings.WheelVolumeStep);
        }

        e.Handled = true;
    }
}



