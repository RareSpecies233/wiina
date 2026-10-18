using System.Globalization;
using System.IO;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Wiina.App.Controls;
using Wiina.App.Interop;
using Wiina.App.Services;
using Wiina.Core.Files;
using Wiina.Core.History;
using Wiina.Core.Input;
using Wiina.Core.Models;
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
    private readonly KeyMap _keyMap = KeyMap.CreateDefault();
    private readonly List<TrackInfo> _trackList = new();
    private readonly List<ChapterInfo> _chapterList = new();
    private readonly List<MpvPlaylistItem> _playlistItems = new();
    private readonly List<string> _pendingSubtitles = new();

    private SmtcService? _smtc;
    private DispatcherQueueTimer? _clickTimer;
    private DispatcherQueueTimer? _suppressClickTimer;
    private DispatcherQueueTimer? _historyTimer;

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
    private bool _smtcInitialized;
    private bool _chapterChangeOsc;
    private bool _wantSubtitleSelection;
    private Point _dragStartPoint;
    private int _dragStartCursorX;
    private int _dragStartCursorY;
    private int _windowStartX;
    private int _windowStartY;

    private void InitializeStage3()
    {
        _settings.RecentUrls ??= new List<string>();

        TitleBar.MoreClicked += (_, _) => ShowMoreMenu();
        RootGrid.AllowDrop = true;
        RootGrid.DragOver += OnRootDragOver;
        RootGrid.Drop += OnRootDropAsync;
        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnRootKeyDown),
            handledEventsToo: false);

        VideoFrame.PointerPressed += OnVideoPointerPressed;
        VideoFrame.PointerMoved += OnVideoPointerMoved;
        VideoFrame.PointerReleased += OnVideoPointerReleased;
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
        OscFloating.Volume.WheelVolumeRequested += AdjustVolume;
        OscBottom.Volume.WheelVolumeRequested += AdjustVolume;
        TitleBar.TopVolumeControl.WheelVolumeRequested += AdjustVolume;

        _clickTimer = DispatcherQueue.CreateTimer();
        _clickTimer.IsRepeating = false;
        _clickTimer.Tick += (_, _) => ExecuteSingleClick();

        _suppressClickTimer = DispatcherQueue.CreateTimer();
        _suppressClickTimer.Interval = TimeSpan.FromMilliseconds(300);
        _suppressClickTimer.IsRepeating = false;
        _suppressClickTimer.Tick += (_, _) => _suppressClick = false;

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
    }

    private void InitializeSmtc()
    {
        if (_smtcInitialized)
        {
            return;
        }

        _smtcInitialized = true;
        _smtc = new SmtcService();
        if (!_smtc.Initialize(this))
        {
            _smtc.Dispose();
            _smtc = null;
            Trace("SystemMediaTransportControls unavailable");
            return;
        }

        _smtc.PlayRequested += () => DispatcherQueue.TryEnqueue(() => PlayOrPause(true));
        _smtc.PauseRequested += () => DispatcherQueue.TryEnqueue(() => PlayOrPause(false));
        _smtc.StopRequested += () => DispatcherQueue.TryEnqueue(StopPlayback);
        _smtc.NextRequested += () => DispatcherQueue.TryEnqueue(PlaylistNext);
        _smtc.PreviousRequested += () => DispatcherQueue.TryEnqueue(PlaylistPrevious);
        UpdateSmtc();
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
        _smtc?.Dispose();
        _smtc = null;
    }

    // ---------------------------------------------------------------- opening

    private void OpenStartupMedia()
    {
        Trace($"startup paths ({App.StartupPaths.Count}): {string.Join(" | ", App.StartupPaths)}");
        if (App.StartupPaths.Count > 0)
        {
            OpenPaths(App.StartupPaths);
            return;
        }

        ShowOsd("按 Ctrl+O 打开文件", "也可以拖放文件、文件夹或 URL");
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
            ShowOsd("打开文件失败", ex.Message);
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
            ShowOsd("打开文件夹失败", ex.Message);
        }
    }

    private async Task OpenUrlDialogAsync()
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/video",
            Text = _settings.RecentUrls.FirstOrDefault() ?? string.Empty,
        };
        var recent = new ComboBox
        {
            PlaceholderText = "最近打开的 URL",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _settings.RecentUrls.ToArray(),
        };
        recent.SelectionChanged += (_, _) =>
        {
            if (recent.SelectedItem is string selected)
            {
                urlBox.Text = selected;
            }
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "支持 mpv 原生协议（HTTP/HTTPS/RTP/RTSP/…）。",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
        });
        panel.Children.Add(urlBox);
        panel.Children.Add(recent);

        var dialog = new ContentDialog
        {
            Title = "打开 URL",
            Content = panel,
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string text = urlBox.Text.Trim();
        if (!TryNormalizeUrl(text, out string normalized))
        {
            ShowOsd("URL 无效", text);
            return;
        }

        AddRecentUrl(normalized);
        OpenPaths(new[] { normalized });
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
            ShowOsd("加载字幕失败", ex.Message);
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
            ShowOsd("打开播放列表失败", ex.Message);
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
                "M3U 播放列表",
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
            ShowOsd("已保存播放列表", file.Name);
        }
        catch (Exception ex)
        {
            Trace("save playlist failed: " + ex);
            ShowOsd("保存播放列表失败", ex.Message);
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

            string name = $"wiina-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            string path = Path.Combine(folder.Path, name);
            SendPlayerCommandAsync("screenshot-to-file", path, "subtitles");
            ShowOsd("截图已保存", path);
        }
        catch (Exception ex)
        {
            Trace("screenshot to folder failed: " + ex);
            ShowOsd("截图失败", ex.Message);
        }
    }

    private void InitializePicker(object picker)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void AddRecentUrl(string url)
    {
        _settings.RecentUrls ??= new List<string>();
        _settings.RecentUrls.RemoveAll(
            existing => string.Equals(existing, url, StringComparison.OrdinalIgnoreCase));
        _settings.RecentUrls.Insert(0, url);
        while (_settings.RecentUrls.Count > 10)
        {
            _settings.RecentUrls.RemoveAt(_settings.RecentUrls.Count - 1);
        }

        SaveSettings();
    }

    private void OpenPaths(IEnumerable<string> inputs)
    {
        var playable = new List<string>();
        var subtitles = new List<string>();
        var errors = new List<string>();

        foreach (string input in inputs
                     .Where(input => !string.IsNullOrWhiteSpace(input))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CollectMediaPaths(input.Trim('"'), playable, subtitles, errors, 0);
        }

        playable = playable
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
                subtitles.Count > 0 ? "请先播放媒体" : "没有可播放的媒体",
                errors.FirstOrDefault());
            return;
        }

        _pendingSubtitles.Clear();
        _pendingSubtitles.AddRange(subtitles.Distinct(StringComparer.OrdinalIgnoreCase));
        PlayEntries(playable);
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
            IReadOnlyList<string> files = MediaFileTypes.ExpandFolder(input);
            if (files.Count == 0)
            {
                errors.Add($"文件夹中没有媒体: {input}");
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
                    errors.Add($"播放列表读取失败: {Path.GetFileName(input)}");
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

            errors.Add($"不支持的格式: {Path.GetFileName(input)}");
            return;
        }

        if (Path.IsPathRooted(input) || input.Contains('\\'))
        {
            errors.Add($"找不到文件: {input}");
            return;
        }

        if (!input.Contains("://", StringComparison.Ordinal) &&
            MediaFileTypes.Classify(input) != MediaKind.Unknown)
        {
            errors.Add($"找不到文件: {input}");
            return;
        }

        if (TryNormalizeUrl(input, out string url))
        {
            playable.Add(url);
            return;
        }

        errors.Add($"找不到文件或 URL: {input}");
    }

    private void PlayEntries(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        _resumeApplied = false;
        _abLoopStage = 0;
        _abLoopA = 0;
        _abLoopB = 0;
        _oscVm.AbLoopA = null;
        _oscVm.AbLoopB = null;
        TitleBar.Title = "加载中…";
        Title = "Wiina";
        UpdateSmtc();
        Trace($"play entries ({entries.Count}): {string.Join(" | ", entries)}");

        SendPlayerCommandAsync("loadfile", entries[0], "replace");
        for (int i = 1; i < entries.Count; i++)
        {
            SendPlayerCommandAsync("loadfile", entries[i], "append");
        }

        ShowOsd(
            entries.Count > 1 ? $"正在打开 {entries.Count} 个文件" : "正在打开…",
            entries.Count > 1 ? Path.GetFileName(entries[0]) : null);
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "在 Wiina 中打开";
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
            ShowOsd("拖放打开失败", ex.Message);
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
            ShowOsd("播放命令失败", ex.Message);
        }
    }

    private bool HasMedia() =>
        _player is { IsRunning: true } &&
        !string.IsNullOrWhiteSpace(_player.GetPropertyString("path"));

    private void TogglePlayPause()
    {
        if (!HasMedia())
        {
            ShowOsd("没有正在播放的媒体", "按 Ctrl+O 打开文件");
            return;
        }

        bool paused = _player!.GetPropertyFlag("pause");
        Trace($"toggle pause, was paused={paused}");
        SendPlayerCommandAsync("set", "pause", paused ? "no" : "yes");
        ShowOsd(paused ? "播放" : "暂停");
    }

    private void PlayOrPause(bool play)
    {
        if (!HasMedia())
        {
            return;
        }

        SendPlayerCommandAsync("set", "pause", play ? "no" : "yes");
        ShowOsd(play ? "播放" : "暂停");
    }

    private void StopPlayback()
    {
        SaveCurrentHistory();
        SendPlayerCommandAsync("stop");
        _currentPath = null;
        _currentTitle = null;
        TitleBar.Title = "Wiina";
        Title = "Wiina";
        ShowOsd("已停止");
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
        ShowOsd(
            TimeFormatter.Format(target),
            _oscVm.Duration > 0 ? TimeFormatter.Format(_oscVm.Duration) : null);
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
            ShowOsd(
                TimeFormatter.Format(target),
                _oscVm.Duration > 0 ? TimeFormatter.Format(_oscVm.Duration) : null);
        }
    }

    private void SetVolume(double value)
    {
        double clamped = Math.Clamp(value, 0, 130);
        Trace($"set volume {clamped:0.##}");
        if (_player is { IsRunning: true })
        {
            _player.TrySetPropertyString(
                "volume",
                clamped.ToString("0.##", CultureInfo.InvariantCulture));
        }

        ShowOsd($"音量 {Math.Round(clamped)}%");
    }

    private void AdjustVolume(double delta)
    {
        if (_player is null)
        {
            return;
        }

        SetVolume(_oscVm.Volume + delta);
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
        ShowOsd(muted ? "静音" : "取消静音");
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

        ShowOsd($"速度 {clamped:0.##}x");
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
        ShowOsd(forward ? "下一帧" : "上一帧");
    }

    private void PreviousChapter() => ChangeChapter(-1);

    private void NextChapter() => ChangeChapter(1);

    private void ChangeChapter(int direction)
    {
        if (!HasMedia() || _chapterList.Count == 0)
        {
            ShowOsd("没有章节");
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
            ShowOsd("播放列表为空");
            return;
        }

        SendPlayerCommandAsync("playlist-prev");
    }

    private void PlaylistNext()
    {
        if (_playlistItems.Count == 0)
        {
            ShowOsd("播放列表为空");
            return;
        }

        SendPlayerCommandAsync("playlist-next");
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
                ShowOsd($"A 点 {TimeFormatter.Format(position)}", "再按一次设置 B 点");
                break;
            case 1:
                _abLoopB = position;
                _abLoopStage = 2;
                _abLoopSeeking = false;
                _oscVm.AbLoopB = position;
                ShowOsd(
                    $"B 点 {TimeFormatter.Format(position)}",
                    $"A-B 循环 {TimeFormatter.Format(_abLoopA)} – {TimeFormatter.Format(position)}");
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
        ShowOsd("A-B 循环已关闭");
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
                ShowOsd("循环：关闭");
                break;
            case 1:
                _player?.TrySetPropertyString("loop-playlist", "inf");
                _player?.TrySetPropertyString("loop-file", "no");
                ShowOsd("循环：列表循环");
                break;
            default:
                _player?.TrySetPropertyString("loop-playlist", "no");
                _player?.TrySetPropertyString("loop-file", "inf");
                ShowOsd("循环：单曲循环");
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
        ShowOsd(shuffle ? "随机播放：开启" : "随机播放：关闭");
    }

    private void CaptureScreenshot()
    {
        if (!HasMedia())
        {
            ShowOsd("没有正在播放的媒体");
            return;
        }

        try
        {
            string folder = GetScreenshotFolder();
            string path = Path.Combine(
                folder,
                $"wiina-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            Trace($"screenshot -> {path}");
            SendPlayerCommandAsync("screenshot-to-file", path, "subtitles");
            ShowOsd("截图已保存", path);
        }
        catch (Exception ex)
        {
            Trace("screenshot failed: " + ex);
            ShowOsd("截图失败", ex.Message);
        }
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
        ShowOsd(visible ? "字幕显示" : "字幕隐藏");
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
        ShowOsd($"字幕延迟 {next:+0.0;-0.0;0.0}s");
    }

    private void ResetSubtitleDelay()
    {
        _player?.TrySetPropertyString("sub-delay", "0");
        ShowOsd("字幕延迟 0.0s");
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
        ShowOsd($"字幕位置 {Math.Round(next)}");
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
        ShowOsd($"字幕缩放 {next:0.##}x");
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
        ShowOsd(direction > 0 ? "下一句字幕" : "上一句字幕");
    }

    private void SelectTrack(TrackInfo track)
    {
        string property = TrackProperty(track.Type);
        _player?.TrySetPropertyString(
            property,
            track.Id.ToString(CultureInfo.InvariantCulture));
        Trace($"select track {track.Type} {track.Id}");
        ShowOsd($"已切换{TrackTypeLabel(track.Type)}", track.DisplayName);
    }

    private void SetTrackSelection(TrackType type, string value, string label)
    {
        _player?.TrySetPropertyString(TrackProperty(type), value);
        ShowOsd($"已切换{TrackTypeLabel(type)}", label);
    }

    private static string TrackProperty(TrackType type) => type switch
    {
        TrackType.Video => "vid",
        TrackType.Audio => "aid",
        _ => "sid",
    };

    private static string TrackTypeLabel(TrackType type) => type switch
    {
        TrackType.Video => "视频轨道",
        TrackType.Audio => "音频轨道",
        _ => "字幕轨道",
    };

    private void SetSecondarySubtitle(int? id)
    {
        _player?.TrySetPropertyString(
            "secondary-sid",
            id is null ? "no" : id.Value.ToString(CultureInfo.InvariantCulture));
        ShowOsd(
            "次字幕",
            id is null ? "关闭" : $"轨道 #{id.Value}");
    }

    private void AddExternalSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowOsd("字幕文件不存在", path);
            return;
        }

        if (!HasMedia())
        {
            _pendingSubtitles.Add(path);
            ShowOsd("已加入待加载字幕", Path.GetFileName(path));
            return;
        }

        Trace($"sub-add {path}");
        SendPlayerCommandAsync("sub-add", path, "select");
        _wantSubtitleSelection = true;
        ShowOsd("已加载字幕", Path.GetFileName(path));
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
        ShowOsd("已加载外挂字幕", $"{_pendingSubtitles.Count} 个");
        _pendingSubtitles.Clear();
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
        });
    }

    private void OnPlaylistPositionChanged(int position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _currentPlaylistIndex = position;
            UpdateSidebarPlaylistPosition(position);
        });
    }

    private void OnAudioDeviceListChanged(IReadOnlyList<MpvAudioDevice> devices)
    {
        DispatcherQueue.TryEnqueue(RefreshSidebarDevices);
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

    private void OnVideoSizeChanged(int width, int height)
    {
        if (width <= 0 || height <= 0)
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
        });
    }

    private void OnFileLoadFailed(int error, string? message)
    {
        Trace($"file load failed: {error} {message}");
        DispatcherQueue.TryEnqueue(() =>
        {
            _pendingSubtitles.Clear();
            TitleBar.Title = "Wiina — 打开失败";
            Title = "Wiina";
            _currentPath = null;
            _currentTitle = null;
            ShowOsd(
                "无法打开媒体",
                string.IsNullOrWhiteSpace(message) ? $"mpv 错误 {error}" : message);
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
                ShowOsd("播放命令失败", message ?? description);
            }
            else if (description.StartsWith("screenshot", StringComparison.Ordinal))
            {
                ShowOsd("截图失败", message ?? description);
            }
        });
    }

    private void OnStage3NumberProperty(string name, double value)
    {
        if (name == "duration")
        {
            UpdateSmtc();
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
            FlushPendingSubtitles();
            UpdateSmtc();
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
            $"从 {TimeFormatter.Format(entry.Position)} 继续",
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
    }

    private void ShowOsd(string message, string? secondary = null)
    {
        Osd.Show(message, secondary);
    }

    private void UpdateSmtc()
    {
        _smtc?.Update(
            !string.IsNullOrWhiteSpace(_currentPath),
            _oscVm.IsPlaying,
            _currentTitle,
            _oscVm.Duration);
    }

    private static bool TryNormalizeUrl(string text, out string normalized)
    {
        normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("://", StringComparison.Ordinal))
        {
            // mpv owns protocol validation (http, rtsp, av://lavfi, edl, …).
            return !normalized.Contains(' ');
        }

        if (normalized.Contains(' ') || !normalized.Contains('.'))
        {
            return false;
        }

        normalized = "https://" + normalized;
        return Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) &&
            !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    // ------------------------------------------------------------------ menu

    private void ShowMoreMenu()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("打开文件…", OpenFilePickerAsync, "Ctrl+O"));
        flyout.Items.Add(Item("打开文件夹…", OpenFolderPickerAsync));
        flyout.Items.Add(Item("打开 URL…", OpenUrlDialogAsync, "Ctrl+Shift+O"));
        flyout.Items.Add(Item("打开播放列表…", OpenPlaylistPickerAsync));
        flyout.Items.Add(BuildRecentMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(Item("播放 / 暂停", TogglePlayPause, "Space"));
        flyout.Items.Add(Item("停止", StopPlayback, "Ctrl+."));
        flyout.Items.Add(Item("上一首", PlaylistPrevious, "Ctrl+←"));
        flyout.Items.Add(Item("下一首", PlaylistNext, "Ctrl+→"));
        flyout.Items.Add(BuildSpeedMenu());
        flyout.Items.Add(BuildLoopMenu());
        flyout.Items.Add(Item(
            _player?.GetPropertyFlag("shuffle") == true ? "随机播放：开" : "随机播放：关",
            ToggleShuffle));
        flyout.Items.Add(Item("A-B 循环", CycleAbLoop, "Ctrl+L"));
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(BuildChapterMenu());
        flyout.Items.Add(BuildTrackMenu());
        flyout.Items.Add(BuildSubtitleMenu());
        flyout.Items.Add(BuildPlaylistMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(Item("截图", CaptureScreenshot, "Ctrl+S"));
        flyout.Items.Add(Item("截图到文件夹…", ScreenshotToFolderPickerAsync));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(Item("设置…", ShowSettingsDialogAsync, "Ctrl+,"));
        flyout.Items.Add(Item("退出", () => Close(), "Q"));

        flyout.ShowAt(TitleBar);
    }

    private MenuFlyoutSubItem BuildRecentMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "最近打开" };
        IReadOnlyList<HistoryEntry> recent = _historyStore.Recent(10);
        if (recent.Count == 0)
        {
            sub.Items.Add(Item("（暂无记录）", () => { }, enabled: false));
            return sub;
        }

        foreach (HistoryEntry entry in recent)
        {
            string label = string.IsNullOrWhiteSpace(entry.Title)
                ? Path.GetFileName(entry.Path)
                : entry.Title;
            sub.Items.Add(Item(
                label,
                () => OpenPaths(new[] { entry.Path })));
        }

        return sub;
    }

    private MenuFlyoutSubItem BuildSpeedMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "速度" };
        foreach (double speed in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 })
        {
            double value = speed;
            sub.Items.Add(Item(
                $"{value:0.##}x",
                () => SetSpeed(value),
                isChecked: Math.Abs(_oscVm.Speed - value) < 0.01));
        }

        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(Item("重置 1x", ResetSpeed, "Ctrl+Backspace"));
        return sub;
    }

    private MenuFlyoutSubItem BuildLoopMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "循环模式" };
        string[] labels = { "关闭", "列表循环", "单曲循环" };
        for (int i = 0; i < labels.Length; i++)
        {
            int mode = i;
            sub.Items.Add(Item(
                labels[i],
                () => SetLoopMode(mode),
                isChecked: _loopMode == mode));
        }

        return sub;
    }

    private MenuFlyoutSubItem BuildChapterMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "章节" };
        if (_chapterList.Count == 0)
        {
            sub.Items.Add(Item("（无章节）", () => { }, enabled: false));
            return sub;
        }

        foreach (ChapterInfo chapter in _chapterList.Take(80))
        {
            ChapterInfo captured = chapter;
            sub.Items.Add(Item(
                $"{captured.Index + 1}. {captured.DisplayName}  {TimeFormatter.Format(captured.Time)}",
                () => JumpToChapter(captured.Index),
                isChecked: captured.Index == _currentChapterIndex));
        }

        return sub;
    }

    private MenuFlyoutSubItem BuildTrackMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "轨道" };
        sub.Items.Add(BuildTrackGroup(TrackType.Video));
        sub.Items.Add(BuildTrackGroup(TrackType.Audio));
        sub.Items.Add(BuildTrackGroup(TrackType.Subtitle));
        return sub;
    }

    private MenuFlyoutSubItem BuildTrackGroup(TrackType type)
    {
        var group = new MenuFlyoutSubItem { Text = TrackTypeLabel(type) };
        group.Items.Add(Item(
            "自动",
            () => SetTrackSelection(type, "auto", "自动"),
            isChecked: !_trackList.Any(track => track.Type == type && track.IsSelected)));
        group.Items.Add(Item(
            "关闭",
            () => SetTrackSelection(type, "no", "关闭")));

        foreach (TrackInfo track in _trackList.Where(track => track.Type == type))
        {
            TrackInfo captured = track;
            string label = captured.DisplayName;
            if (!string.IsNullOrWhiteSpace(captured.Codec))
            {
                label += $"  [{captured.Codec}]";
            }

            group.Items.Add(Item(
                label,
                () => SelectTrack(captured),
                isChecked: captured.IsSelected));
        }

        return group;
    }

    private MenuFlyoutSubItem BuildSubtitleMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "字幕" };
        bool hasSubtitles = _trackList.Any(track => track.Type == TrackType.Subtitle);
        sub.Items.Add(Item(
            "显示 / 隐藏字幕",
            ToggleSubtitleVisibility,
            "V",
            isChecked: _player?.GetPropertyFlag("sub-visibility") ?? true));
        sub.Items.Add(Item("加载外挂字幕…", OpenSubtitlePickerAsync));
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(Item("延迟 -0.1s", () => AdjustSubtitleDelay(-0.1), "Z"));
        sub.Items.Add(Item("延迟 +0.1s", () => AdjustSubtitleDelay(0.1), "X"));
        sub.Items.Add(Item("延迟归零", ResetSubtitleDelay, "C"));
        sub.Items.Add(Item("位置 +5", () => AdjustSubtitlePosition(5), "R"));
        sub.Items.Add(Item("位置 -5", () => AdjustSubtitlePosition(-5), "T"));
        sub.Items.Add(Item("放大", () => AdjustSubtitleScale(0.1), "G"));
        sub.Items.Add(Item("缩小", () => AdjustSubtitleScale(-0.1), "F"));
        sub.Items.Add(new MenuFlyoutSeparator());

        var secondary = new MenuFlyoutSubItem { Text = "次字幕" };
        secondary.Items.Add(Item("关闭", () => SetSecondarySubtitle(null)));
        foreach (TrackInfo track in _trackList.Where(
                     track => track.Type == TrackType.Subtitle))
        {
            TrackInfo captured = track;
            secondary.Items.Add(Item(
                captured.DisplayName,
                () => SetSecondarySubtitle(captured.Id)));
        }

        sub.Items.Add(secondary);
        if (!hasSubtitles)
        {
            sub.Items.Add(Item("（当前没有字幕轨道）", () => { }, enabled: false));
        }

        return sub;
    }

    private MenuFlyoutSubItem BuildPlaylistMenu()
    {
        var sub = new MenuFlyoutSubItem { Text = "播放列表" };
        if (_playlistItems.Count == 0)
        {
            sub.Items.Add(Item("（播放列表为空）", () => { }, enabled: false));
        }
        else
        {
            foreach (MpvPlaylistItem item in _playlistItems.Take(40))
            {
                MpvPlaylistItem captured = item;
                sub.Items.Add(Item(
                    $"{captured.Index + 1}. {captured.DisplayName}",
                    () => SendPlayerCommandAsync(
                        "set",
                        "playlist-pos",
                        captured.Index.ToString(CultureInfo.InvariantCulture)),
                    isChecked: captured.Index == _currentPlaylistIndex));
            }

            if (_playlistItems.Count > 40)
            {
                sub.Items.Add(Item(
                    $"（还有 {_playlistItems.Count - 40} 项，Stage 4 提供完整列表）",
                    () => { },
                    enabled: false));
            }
        }

        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(Item("添加文件到播放列表…", AddFilesToPlaylistAsync));
        sub.Items.Add(Item("保存播放列表…", SavePlaylistPickerAsync));
        sub.Items.Add(Item(
            "移除当前项",
            RemoveCurrentPlaylistItem,
            enabled: _currentPlaylistIndex >= 0));
        sub.Items.Add(Item(
            "当前项上移",
            () => MoveCurrentPlaylistItem(-1),
            enabled: _currentPlaylistIndex > 0));
        sub.Items.Add(Item(
            "当前项下移",
            () => MoveCurrentPlaylistItem(1),
            enabled: _currentPlaylistIndex >= 0 &&
                _currentPlaylistIndex < _playlistItems.Count - 1));
        return sub;
    }

    private async Task AddFilesToPlaylistAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            foreach (StorageFile file in files)
            {
                if (MediaFileTypes.IsPlayableMedia(file.Path))
                {
                    SendPlayerCommandAsync("loadfile", file.Path, "append");
                }
            }

            if (files.Count > 0)
            {
                ShowOsd($"已添加 {files.Count} 个文件到播放列表");
            }
        }
        catch (Exception ex)
        {
            Trace("append files failed: " + ex);
            ShowOsd("添加文件失败", ex.Message);
        }
    }

    private void RemoveCurrentPlaylistItem()
    {
        if (_currentPlaylistIndex < 0)
        {
            return;
        }

        SendPlayerCommandAsync(
            "playlist-remove",
            _currentPlaylistIndex.ToString(CultureInfo.InvariantCulture));
        ShowOsd("已从播放列表移除");
    }

    private void MoveCurrentPlaylistItem(int delta)
    {
        int from = _currentPlaylistIndex;
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _playlistItems.Count)
        {
            return;
        }

        SendPlayerCommandAsync(
            "playlist-move",
            from.ToString(CultureInfo.InvariantCulture),
            to.ToString(CultureInfo.InvariantCulture));
        ShowOsd(delta < 0 ? "已上移" : "已下移");
    }

    private MenuFlyoutItem Item(
        string text,
        Action action,
        string? shortcut = null,
        bool enabled = true,
        bool isChecked = false)
    {
        var item = new MenuFlyoutItem
        {
            Text = isChecked ? "✓ " + text : text,
            IsEnabled = enabled,
        };
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        item.Click += (_, _) => action();
        return item;
    }

    private MenuFlyoutItem Item(
        string text,
        Func<Task> action,
        string? shortcut = null,
        bool enabled = true)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            IsEnabled = enabled,
        };
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        item.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Trace("menu action failed: " + ex);
                ShowOsd("操作失败", ex.Message);
            }
        };
        return item;
    }

    private async Task ShowSettingsDialogAsync()
    {
        var resume = new CheckBox
        {
            Content = "打开文件时从上次位置继续",
            IsChecked = _settings.ResumePlayback,
        };
        var clickMode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "隐藏 OSC", "播放 / 暂停", "全屏", "无" },
            SelectedIndex = Math.Clamp(
                (int)_settings.SingleClickAction,
                0,
                3),
        };

        var options = new StackPanel { Spacing = 10, MinWidth = 420 };
        options.Children.Add(new TextBlock
        {
            Text = "播放",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        options.Children.Add(resume);
        options.Children.Add(new TextBlock { Text = "视频单击动作" });
        options.Children.Add(clickMode);

        var keys = new StackPanel { Spacing = 3 };
        keys.Children.Add(new TextBlock
        {
            Text = "默认键盘（Stage 3；Stage 7 提供编辑器）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
        });
        foreach ((string chord, string action) in _keyMap.Describe())
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chordText = new TextBlock
            {
                Text = chord,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono"),
            };
            var actionText = new TextBlock
            {
                Text = ActionLabel(action),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(actionText, 1);
            row.Children.Add(chordText);
            row.Children.Add(actionText);
            keys.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Content = keys,
            Height = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        options.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            Title = "设置（Stage 3 基础）",
            Content = options,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = RootGrid.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _settings.ResumePlayback = resume.IsChecked == true;
        _settings.SingleClickAction = (SingleClickAction)Math.Clamp(
            clickMode.SelectedIndex,
            0,
            3);
        SaveSettings();
        ShowOsd("设置已保存");
    }

    private static string ActionLabel(string action) => action switch
    {
        PlayerActions.PlayPause => "播放 / 暂停",
        PlayerActions.Stop => "停止",
        PlayerActions.SeekBack5 => "后退 5 秒",
        PlayerActions.SeekForward5 => "前进 5 秒",
        PlayerActions.SeekBack1 => "后退 1 秒",
        PlayerActions.SeekForward1 => "前进 1 秒",
        PlayerActions.VolumeUp5 => "音量 +5",
        PlayerActions.VolumeDown5 => "音量 -5",
        PlayerActions.VolumeUp1 => "音量 +1",
        PlayerActions.VolumeDown1 => "音量 -1",
        PlayerActions.Mute => "静音",
        PlayerActions.FramePrevious => "上一帧",
        PlayerActions.FrameNext => "下一帧",
        PlayerActions.SubtitleSeekPrevious => "上一句字幕",
        PlayerActions.SubtitleSeekNext => "下一句字幕",
        PlayerActions.Open => "打开文件",
        PlayerActions.OpenUrl => "打开 URL",
        PlayerActions.Settings => "设置",
        PlayerActions.Screenshot => "截图",
        PlayerActions.AbLoop => "A-B 循环",
        PlayerActions.PlaylistPrevious => "播放列表上一首",
        PlayerActions.PlaylistNext => "播放列表下一首",
        PlayerActions.SpeedHalf => "速度 ×0.5",
        PlayerActions.SpeedDouble => "速度 ×2",
        PlayerActions.SpeedReset => "速度复位",
        PlayerActions.ToggleFullscreen => "全屏",
        PlayerActions.ExitFullscreen => "退出全屏",
        PlayerActions.PictureInPicture => "画中画（占位）",
        PlayerActions.Sidebar => "侧边栏",
        PlayerActions.SidebarSettings => "快速设置侧栏",
        PlayerActions.Layout => "布局切换",
        PlayerActions.MusicMode => "音乐模式（占位）",
        PlayerActions.Theme => "主题切换",
        PlayerActions.OscPosition => "OSC 位置",
        PlayerActions.LoopMode => "循环模式",
        PlayerActions.ChapterPrevious => "上一章",
        PlayerActions.ChapterNext => "下一章",
        PlayerActions.SubtitleVisibility => "字幕显隐",
        PlayerActions.SubtitleDelayDecrease => "字幕延迟 -0.1",
        PlayerActions.SubtitleDelayIncrease => "字幕延迟 +0.1",
        PlayerActions.SubtitleDelayReset => "字幕延迟归零",
        PlayerActions.SubtitleScaleIncrease => "字幕放大",
        PlayerActions.SubtitleScaleDecrease => "字幕缩小",
        PlayerActions.SubtitlePositionUp => "字幕位置上移",
        PlayerActions.SubtitlePositionDown => "字幕位置下移",
        PlayerActions.WindowHalf => "窗口 0.5×",
        PlayerActions.WindowNormal => "窗口 1×",
        PlayerActions.WindowDouble => "窗口 2×",
        PlayerActions.WindowShrink => "缩小窗口",
        PlayerActions.WindowGrow => "放大窗口",
        PlayerActions.Quit => "退出",
        _ => action,
    };

    // ----------------------------------------------------------------- input

    private bool IsTextInputFocused()
    {
        object? focused = FocusManager.GetFocusedElement(RootGrid.XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        Trace($"key down {e.Key}");
        KeyModifiers modifiers = KeyModifiers.None;
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

        if (!KeyChordMapper.TryMap(e.Key, modifiers, out KeyChord chord) ||
            !_keyMap.TryGetAction(chord, out string? action) ||
            string.IsNullOrEmpty(action))
        {
            return;
        }

        if (ExecuteAction(action))
        {
            e.Handled = true;
        }
    }

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
                _ = ShowSettingsDialogAsync();
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
                if (AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)
                {
                    AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                    SyncFullscreenState();
                    DispatcherQueue.TryEnqueue(ApplyVideoLayout);
                }

                return true;
            case PlayerActions.PictureInPicture:
                ShowOsd("画中画将在 Stage 6 提供");
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
                ShowOsd("音乐模式将在 Stage 6 提供");
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
            case PlayerActions.Quit:
                Close();
                return true;
            default:
                return false;
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

        _suppressClick = false;
        _windowDragCandidate = true;
        _windowDragging = false;
        _dragStartPoint = e.GetCurrentPoint(RootGrid).Position;
        WindowInterop.TryGetCursorPosition(out _dragStartCursorX, out _dragStartCursorY);
        _windowStartX = AppWindow.Position.X;
        _windowStartY = AppWindow.Position.Y;
        RootGrid.Focus(FocusState.Pointer);
    }

    private void OnVideoPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowDragCandidate)
        {
            return;
        }

        Point current = e.GetCurrentPoint(RootGrid).Position;
        if (!_windowDragging)
        {
            double dx = current.X - _dragStartPoint.X;
            double dy = current.Y - _dragStartPoint.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 3)
            {
                return;
            }

            _windowDragging = true;
            VideoFrame.CapturePointer(e.Pointer);
            Trace("window drag started");
        }

        if (WindowInterop.TryGetCursorPosition(out int cursorX, out int cursorY))
        {
            AppWindow.Move(new PointInt32(
                _windowStartX + (cursorX - _dragStartCursorX),
                _windowStartY + (cursorY - _dragStartCursorY)));
        }
    }

    private void OnVideoPointerReleased(object sender, PointerRoutedEventArgs e)
    {
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
        _windowDragCandidate = false;
        _windowDragging = false;
    }

    private void OnVideoPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
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
        if (properties.IsHorizontalMouseWheel)
        {
            SeekRelative(direction * _settings.WheelSeekSeconds);
        }
        else
        {
            AdjustVolume(direction * _settings.WheelVolumeStep);
        }

        e.Handled = true;
    }
}
