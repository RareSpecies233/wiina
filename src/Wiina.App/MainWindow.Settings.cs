using System.Globalization;
using System.IO;
using Microsoft.UI.Windowing;
using Wiina.App.Views;
using Wiina.Core.Input;
using Wiina.Core.Menu;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.Text;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Stage 7 bridge between the shared <see cref="AppSettings"/> edited by the
/// settings window and this player window (mpv properties, chrome, menus and
/// the live inspecor snapshot).
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<MpvAudioDevice> _audioDevices = new();

    public string? CurrentTitle => _currentTitle;

    /// <summary>Opens files/URLs coming from the welcome window or another window.</summary>
    public void OpenExternalPaths(IEnumerable<string> paths) => OpenPaths(paths);

    /// <summary>Rebuilds the per-window lookup from the persisted key bindings.</summary>
    public void RebuildKeyMap()
    {
        _keyMap = KeyBindings.BuildKeyMap(_settings.KeyBindings);
    }

    public void ToggleTechnicalOsd() => TogglePersistentOsd();

    public void ClearThumbnailCache()
    {
        try
        {
            _thumbnailService?.Dispose();
        }
        catch
        {
            // Cache disposal is best-effort.
        }

        _thumbnailService = null;
        HideTimePreview();
    }

    public string GetRendererInfo()
    {
        if (_player is null)
        {
            return "播放引擎未启动";
        }

        string api = _player.GetPropertyString("gpu-api") ?? "—";
        string context = _player.GetPropertyString("gpu-context") ?? "—";
        string hwdec = _player.GetPropertyString("hwdec-current") ?? "—";
        return $"gpu-api={api} · gpu-context={context} · hwdec={hwdec}";
    }

    /// <summary>
    /// Applies one settings row immediately (called after the shared settings
    /// object was updated). Rows that only affect future windows are no-ops.
    /// </summary>
    public void ApplySettingsRow(string rowId)
    {
        switch (rowId)
        {
            case "general.theme":
                ApplyTheme();
                break;
            case "ui.layout":
                ApplyLayout();
                break;
            case "ui.always-on-top":
                TitleBar.IsPinned = _settings.AlwaysOnTop;
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
                }

                break;
            case "ui.sidebar-side":
                ApplySidebarSide();
                ApplyLayout();
                break;
            case "ui.sidebar-width":
                SetSidebarWidth(_settings.SidebarWidth);
                break;
            case "ui.osc-position":
                ApplyLayout();
                break;
            case "ui.osc-autohide":
                _oscState.AutoHideEnabled = _settings.OscAutoHide;
                ResetAutoHideTimer();
                break;
            case "ui.osd-enabled":
            case "ui.osd-position":
            case "ui.osd-size":
            case "ui.osd-duration":
            case "ui.fullscreen-clock":
            case "ui.fullscreen-battery":
            case "ui.fullscreen-title":
                ApplyStage5Settings();
                UpdateOsdPlacement();
                UpdateFullscreenStatus();
                break;
            case "ui.osd-persistent":
                if (_settings.OsdPersistent)
                {
                    ShowPersistentOsd();
                }
                else
                {
                    ClosePersistentOsd();
                }

                break;
            case "ui.thumbnail-preview":
                if (!_settings.ThumbnailPreviewEnabled)
                {
                    HideTimePreview();
                }

                break;

            // ---- mpv-backed rows -------------------------------------------
            case "video.hwdec":
                ApplyMpvString("hwdec", QuickSettings.HwdecValue(_settings.VideoHardwareDecode));
                break;
            case "video.deinterlace":
                ApplyMpvString("deinterlace", QuickSettings.DeinterlaceValue(_settings.VideoDeinterlace));
                break;
            case "video.aspect":
                ApplyMpvString("video-aspect-override", _settings.VideoAspect);
                break;
            case "video.crop":
                ApplyCropFromSettings();
                break;
            case "video.rotation":
                ApplyMpvString("video-rotate", _settings.VideoRotation.ToString(CultureInfo.InvariantCulture));
                break;
            case "video.flip-h":
            case "video.flip-v":
                ApplyVideoFilters();
                break;
            case "video.zoom":
                ApplyMpvString("video-zoom", FormatDouble(_settings.VideoZoom));
                break;
            case "video.pan-x":
                ApplyMpvString("video-pan-x", FormatDouble(_settings.VideoPanX));
                break;
            case "video.pan-y":
                ApplyMpvString("video-pan-y", FormatDouble(_settings.VideoPanY));
                break;
            case "audio.volume":
                ApplyVolumeSilent(_settings.Volume);
                break;
            case "audio.max-volume":
                ApplyMpvString("volume-max", FormatDouble(_settings.MaxVolume));
                _settings.Volume = QuickSettings.ClampVolume(_settings.Volume, _settings.MaxVolume);
                ApplyVolumeSilent(_settings.Volume);
                break;
            case "audio.mute":
                ApplyMpvString("mute", QuickSettings.BooleanValue(_settings.Muted));
                break;
            case "audio.delay":
                ApplyMpvString("audio-delay", FormatDouble(_settings.AudioDelay));
                break;
            case "audio.device":
                ApplyMpvString("audio-device", _settings.AudioDevice);
                break;
            case "subtitles.visible":
                ApplyMpvString("sub-visibility", QuickSettings.BooleanValue(_settings.SubtitleVisible));
                break;
            case "subtitles.delay":
                ApplyMpvString("sub-delay", FormatDouble(_settings.SubtitleDelay));
                break;
            case "subtitles.position":
                ApplyMpvString("sub-pos", FormatDouble(_settings.SubtitlePosition));
                break;
            case "subtitles.scale":
                ApplyMpvString("sub-scale", FormatDouble(_settings.SubtitleScale));
                break;
            case "subtitles.font-size":
                ApplyMpvString("sub-font-size", FormatDouble(_settings.SubtitleFontSize));
                break;
            case "subtitles.font":
                ApplyMpvString("sub-font", _settings.SubtitleFont);
                break;
            case "subtitles.color":
                ApplyMpvString("sub-color", _settings.SubtitleColor);
                break;
            case "subtitles.border":
                ApplyMpvString("sub-border-size", FormatDouble(_settings.SubtitleBorderSize));
                break;
            case "network.cache":
                ApplyMpvString("demuxer-max-bytes", CacheBytes(_settings.CacheSizeMb));
                break;
            case "network.cache-back":
                ApplyMpvString("demuxer-max-back-bytes", CacheBytes(_settings.CacheBackMb));
                break;
            case "network.proxy":
                ApplyMpvString("http-proxy", _settings.Proxy);
                break;
            case "network.user-agent":
                ApplyMpvString("user-agent", _settings.UserAgent);
                break;
            case "network.timeout":
                ApplyMpvString("network-timeout", FormatDouble(_settings.NetworkTimeoutSeconds));
                break;
            case "advanced.mpv-log":
                _player?.SetLogLevel(_settings.MpvLogLevel);
                break;
        }
    }

    /// <summary>Re-applies persisted Stage 7 mpv settings after engine start.</summary>
    public void ApplyStage7Settings()
    {
        if (_player is null)
        {
            return;
        }

        ApplyMpvString("demuxer-max-bytes", CacheBytes(_settings.CacheSizeMb));
        ApplyMpvString("demuxer-max-back-bytes", CacheBytes(_settings.CacheBackMb));
        if (!string.IsNullOrWhiteSpace(_settings.Proxy))
        {
            ApplyMpvString("http-proxy", _settings.Proxy);
        }

        if (!string.IsNullOrWhiteSpace(_settings.UserAgent))
        {
            ApplyMpvString("user-agent", _settings.UserAgent);
        }

        ApplyMpvString("network-timeout", FormatDouble(_settings.NetworkTimeoutSeconds));
        ApplyMpvString("sub-font", FontNameValidation.Normalize(_settings.SubtitleFont));
        ApplyMpvString("sub-font-size", FormatDouble(_settings.SubtitleFontSize));
        ApplyMpvString("sub-color", QuickSettings.NormalizeColor(_settings.SubtitleColor));
        ApplyMpvString("sub-border-size", FormatDouble(_settings.SubtitleBorderSize));
        ApplyMpvString("audio-device", _settings.AudioDevice);
        ApplyMpvString("volume-max", FormatDouble(_settings.MaxVolume));
        _player.SetLogLevel(_settings.MpvLogLevel);
    }

    /// <summary>Snapshot used by the application menu's enable/check rules.</summary>
    public MenuState BuildMenuState() => new()
    {
        HasMedia = HasMedia(),
        IsPlaying = HasMedia() && _oscVm.IsPlaying,
        HasVideo = _trackList.Any(track => track.Type == TrackType.Video),
        HasAudioTrack = _trackList.Any(track => track.Type == TrackType.Audio),
        HasSubtitleTrack = _trackList.Any(track => track.Type == TrackType.Subtitle),
        SubtitlesVisible = _player?.GetPropertyFlag("sub-visibility") ?? _settings.SubtitleVisible,
        PlaylistCount = Math.Max(_playlistItems.Count, _player?.GetPlaylist().Count ?? 0),
        ChapterCount = _chapterList.Count,
        IsFullscreen = _mode.Current == PlayerMode.Fullscreen ||
            AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen,
        IsPip = InPipMode,
        IsMini = InMiniMode,
        AlwaysOnTop = _settings.AlwaysOnTop,
        LockAspect = _settings.LockAspectRatio,
        Shuffle = _player?.GetPropertyFlag("shuffle") ?? false,
        AbLoopActive = _abLoopStage >= 2,
        Muted = _player?.GetPropertyFlag("mute") ?? _settings.Muted,
        LoopMode = _loopMode,
        Speed = _oscVm.Speed,
        Aspect = QuickSettings.NormalizeAspect(_settings.VideoAspect),
        CropAspect = _settings.VideoCropAspect,
        Rotation = QuickSettings.NormalizeRotation(_settings.VideoRotation),
        FlipHorizontal = _settings.VideoFlipHorizontal,
        FlipVertical = _settings.VideoFlipVertical,
        Deinterlace = _settings.VideoDeinterlace,
        HasAudioDevice = _audioDevices.Count > 0 || _player is not null,
        PluginHostAvailable = false,
    };

    /// <summary>Technical rows for the inspector window (live mpv properties).</summary>
    public IReadOnlyList<InspectorRow> GetInspectorSnapshot()
    {
        var rows = new List<InspectorRow>();
        if (_player is null)
        {
            return rows;
        }

        void Add(string section, string name, string? value) =>
            rows.Add(new InspectorRow(
                section,
                name,
                string.IsNullOrWhiteSpace(value) ? "—" : value));

        Add("文件", "路径", _player.GetPropertyString("path") ?? _currentPath);
        Add("文件", "标题", _player.GetPropertyString("media-title") ?? _currentTitle);
        Add("文件", "容器", _player.GetPropertyString("file-format"));
        long fileSize = _player.GetPropertyInt64("file-size");
        Add("文件", "大小", fileSize > 0 ? FormatBytes(fileSize) : "—");

        double duration = _player.GetPropertyDouble("duration");
        Add("播放", "时长", duration > 0 ? TimeFormatter.Format(duration) : "直播 / 未知");
        Add("播放", "位置", TimeFormatter.Format(_player.GetPropertyDouble("time-pos")));
        Add("播放", "状态", _player.GetPropertyFlag("pause") ? "暂停" : "播放中");
        Add("播放", "速度", $"{_player.GetPropertyDouble("speed"):0.##}×");
        Add("播放", "缓存", TimeFormatter.Format(_player.GetPropertyDouble("demuxer-cache-time")));
        Add("播放", "缓冲进度", $"{_player.GetPropertyDouble("cache-buffering-state"):0}%");

        int width = (int)_player.GetPropertyInt64("width");
        int height = (int)_player.GetPropertyInt64("height");
        Add("视频", "编码", _player.GetPropertyString("video-codec"));
        Add("视频", "分辨率", width > 0 && height > 0 ? $"{width} × {height}" : "—");
        Add("视频", "帧率", $"{_player.GetPropertyDouble("container-fps"):0.###}");
        Add("视频", "硬件解码", _player.GetPropertyString("hwdec-current"));
        Add("视频", "渲染 API", _player.GetPropertyString("gpu-api"));
        Add("视频", "渲染上下文", _player.GetPropertyString("gpu-context"));

        Add("音频", "编码", _player.GetPropertyString("audio-codec"));
        Add("音频", "采样率", $"{_player.GetPropertyInt64("audio-samplerate")} Hz");
        Add("音频", "声道", _player.GetPropertyString("audio-channels"));
        Add("音频", "设备", _player.GetPropertyString("audio-device"));
        Add("音频", "延迟", $"{_player.GetPropertyDouble("audio-delay"):0.###} s");
        Add("音频", "音量", $"{_player.GetPropertyDouble("volume"):0.#}%{( _player.GetPropertyFlag("mute") ? "（静音）" : string.Empty)}");

        int subtitleIndex = 0;
        foreach (TrackInfo track in _trackList.Where(track => track.Type == TrackType.Subtitle))
        {
            subtitleIndex++;
            string label = $"轨道 {subtitleIndex}";
            string details = string.Join(
                " · ",
                new[]
                {
                    track.Codec,
                    track.Language,
                    track.Title,
                    track.IsSelected ? "已选中" : null,
                }.Where(part => !string.IsNullOrWhiteSpace(part)));
            Add("字幕", label, details);
        }

        Add("字幕", "可见", _player.GetPropertyFlag("sub-visibility") ? "是" : "否");
        Add("字幕", "延迟", $"{_player.GetPropertyDouble("sub-delay"):0.###} s");
        Add("字幕", "位置", $"{_player.GetPropertyDouble("sub-pos"):0.#}");
        Add("字幕", "缩放", $"{_player.GetPropertyDouble("sub-scale"):0.##}×");
        Add("字幕", "字体", _player.GetPropertyString("sub-font"));

        Add("轨道", "视频", _trackList.Count(track => track.Type == TrackType.Video).ToString(CultureInfo.InvariantCulture));
        Add("轨道", "音频", _trackList.Count(track => track.Type == TrackType.Audio).ToString(CultureInfo.InvariantCulture));
        Add("轨道", "字幕", _trackList.Count(track => track.Type == TrackType.Subtitle).ToString(CultureInfo.InvariantCulture));
        Add("播放列表", "条目", _playlistItems.Count.ToString(CultureInfo.InvariantCulture));
        Add("播放列表", "当前索引", (_currentPlaylistIndex + 1).ToString(CultureInfo.InvariantCulture));
        Add("章节", "数量", _chapterList.Count.ToString(CultureInfo.InvariantCulture));
        Add("章节", "当前", _currentChapterIndex >= 0 ? (_currentChapterIndex + 1).ToString(CultureInfo.InvariantCulture) : "—");

        return rows;
    }

    private void AdjustAudioDelay(double delta)
    {
        _settings.AudioDelay = QuickSettings.ClampAudioDelay(_settings.AudioDelay + delta);
        ApplyMpvString("audio-delay", FormatDouble(_settings.AudioDelay));
        ShowOsd("音频延迟", $"{_settings.AudioDelay:+0.0;-0.0;0.0} s");
        SaveSettings();
    }

    private static string CacheBytes(int megabytes) =>
        (Math.Max(0, megabytes) * 1024L * 1024L)
            .ToString(CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
