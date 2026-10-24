using System.Globalization;
using System.IO;
using Microsoft.UI.Windowing;
using Wiina.App.Controls;
using Wiina.App.Interop;
using Wiina.App.Views;
using Wiina.Core.Diagnostics;
using Wiina.App.Localization;
using Wiina.Core.Filters;
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
        _keyMap = KeyBindings.BuildKeyMap(_settings.KeyBindings, _settings.KeyBindingProfile);
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
            return Loc.T("settings.engineNotStarted");
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
                ApplyMpvString("hwdec", EffectiveHwdecValue());
                ApplyVideoFilters();
                break;
            case "video.hwdec-mode":
                _settings.VideoHwdecMode = NormalizeHwdecMode(_settings.VideoHwdecMode);
                ApplyMpvString("hwdec", EffectiveHwdecValue());
                ApplyVideoFilters();
                break;
            case "video.quality":
                ApplyQualityPreset();
                break;
            case "video.tone-mapping":
            case "video.hdr-compute-peak":
            case "video.target-peak":
            case "video.output-levels":
                ApplyStage8VideoOptions();
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
            case "video.delogo-enabled":
            case "video.delogo-x":
            case "video.delogo-y":
            case "video.delogo-width":
            case "video.delogo-height":
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
            case "audio.equalizer":
            case "audio.equalizer-preset":
                ApplyAudioFilters();
                SyncEqualizerViews();
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
            case "subtitles.ass-override":
                ApplyMpvString("sub-ass-override", NormalizeAssOverride(_settings.SubtitleAssOverride));
                break;
            case "subtitles.bold":
                ApplyMpvString("sub-bold", QuickSettings.BooleanValue(_settings.SubtitleBold));
                break;
            case "subtitles.italic":
                ApplyMpvString("sub-italic", QuickSettings.BooleanValue(_settings.SubtitleItalic));
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

    // ------------------------------------------------- Stage 8 filters/EQ

    /// <summary>
    /// Applies advanced A/V options and both filter graphs after engine start or
    /// a shared-settings change. Safe to call repeatedly.
    /// </summary>
    public void ApplyStage8Settings()
    {
        if (_player is null)
        {
            return;
        }

        ApplyStage8VideoOptions();
        ApplyStage8SubtitleOptions();
        ApplyStage8Filters();
    }

    /// <summary>
    /// Rebuilds mpv vf/af immediately and returns validation/mpv errors. The
    /// filter window shows these without breaking playback.
    /// </summary>
    public IReadOnlyList<string> ApplyStage8Filters()
    {
        var errors = new List<string>();
        if (_player is null)
        {
            return errors;
        }

        ApplyVideoFiltersCore(errors);
        ApplyAudioFiltersCore(errors);
        if (errors.Count > 0)
        {
            ShowOsd(Loc.T("osd.filterGraphWarning"), errors[0]);
        }

        return errors;
    }

    public void SyncStage8Views() => SyncEqualizerViews();

    private void ApplyStage8VideoOptions()
    {
        ApplyMpvString("tone-mapping", NormalizeToneMapping(_settings.VideoToneMapping));
        ApplyMpvString("hdr-compute-peak", NormalizeTriState(_settings.VideoHdrComputePeak, "auto"));
        ApplyMpvString(
            "target-peak",
            _settings.VideoTargetPeak > 0
                ? FormatDouble(_settings.VideoTargetPeak)
                : "auto");
        ApplyMpvString(
            "video-output-levels",
            NormalizeChoice(_settings.VideoOutputLevels, new[] { "auto", "limited", "full" }, "auto"));
        ApplyMpvString(
            "hwdec",
            HasActiveVideoFilters() ? "no" : EffectiveHwdecValue());
    }

    private bool HasActiveVideoFilters() =>
        _settings.VideoFilters.Any(filter => filter.Enabled) ||
        _settings.VideoCropAspect > 0 ||
        _settings.VideoFlipHorizontal ||
        _settings.VideoFlipVertical ||
        _settings.VideoDelogoEnabled;

    private void ApplyStage8SubtitleOptions()
    {
        ApplyMpvString(
            "sub-ass-override",
            NormalizeChoice(
                _settings.SubtitleAssOverride,
                new[] { "no", "yes", "scale", "strip", "force" },
                "scale"));
        ApplyMpvString("sub-bold", QuickSettings.BooleanValue(_settings.SubtitleBold));
        ApplyMpvString("sub-italic", QuickSettings.BooleanValue(_settings.SubtitleItalic));
    }

    private void ApplyAudioFiltersCore(List<string> errors)
    {
        AudioFilterGraphResult result = AudioFilterGraph.Build(
            _settings.EqualizerEnabled,
            _settings.EqualizerGains,
            _settings.AudioFilters);
        errors.AddRange(result.Errors);

        if (!_player!.TrySetPropertyString("af", result.Graph))
        {
            string message = result.Graph.Length == 0
                ? Loc.T("osd.audioFilterClearRefused")
                : Loc.T("osd.audioFilterGraphRejected") + FirstGraphPart(result.Graph);
            errors.Add(message);
            Trace(message);
        }
    }

    private void ApplyAudioFilters() => ApplyAudioFiltersCore(new List<string>());

    private void ApplyEqualizerPreset(string? name)
    {
        if (string.Equals(name, EqualizerBands.CustomPreset, StringComparison.Ordinal))
        {
            _settings.EqualizerPreset = EqualizerBands.CustomPreset;
            return;
        }

        if (string.IsNullOrWhiteSpace(name) ||
            !EqualizerBands.PresetNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.EqualizerPreset = name;
        _settings.EqualizerGains = EqualizerBands.CreatePreset(name).ToList();
    }

    private void SyncEqualizerViews()
    {
        foreach (SidebarHost host in new[] { SidebarLeading, SidebarTrailing })
        {
            host.QuickSettings.SetEqualizerState(_settings);
        }
    }

    private VideoFilterLegacy CreateVideoFilterLegacy() => new(
        _settings.VideoFlipHorizontal,
        _settings.VideoFlipVertical,
        _settings.VideoCropAspect,
        _settings.VideoRotation,
        _settings.VideoDeinterlace,
        _settings.VideoDelogoEnabled,
        _settings.VideoDelogoX,
        _settings.VideoDelogoY,
        _settings.VideoDelogoWidth,
        _settings.VideoDelogoHeight);

    private string EffectiveHwdecValue() =>
        !_settings.VideoHardwareDecode
            ? "no"
            : NormalizeChoice(
                _settings.VideoHwdecMode,
                new[] { "auto-safe", "auto", "d3d11va", "dxva2", "no" },
                "auto-safe");

    private string EqualizerDescription()
    {
        if (!_settings.EqualizerEnabled)
        {
            return Loc.T("menu.off");
        }

        IReadOnlyList<double> gains = EqualizerBands.NormalizeGains(_settings.EqualizerGains);
        return $"{_settings.EqualizerPreset}：" + string.Join(
            " / ",
            gains.Select(gain => gain.ToString("0.#", CultureInfo.InvariantCulture)));
    }

    private static string NormalizeHwdecMode(string? mode) =>
        NormalizeChoice(mode, new[] { "auto-safe", "auto", "d3d11va", "dxva2", "no" }, "auto-safe");

    private static string NormalizeToneMapping(string? value) =>
        NormalizeChoice(
            value,
            new[]
            {
                "auto", "bt.2390", "st2094-40", "st2094-10", "spline",
                "hable", "clip", "gamma", "reinhard", "mobius",
            },
            "auto");

    private static string NormalizeAssOverride(string? value) =>
        NormalizeChoice(value, new[] { "no", "yes", "scale", "strip", "force" }, "scale");

    private static string NormalizeTriState(string? value, string fallback) =>
        NormalizeChoice(value, new[] { "auto", "yes", "no" }, fallback);

    private static string NormalizeChoice(
        string? value,
        IReadOnlyList<string> allowed,
        string fallback)
    {
        string text = (value ?? string.Empty).Trim();
        return allowed.FirstOrDefault(
            item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private static string FirstGraphPart(string graph)
    {
        int comma = graph.IndexOf(',');
        return comma < 0 ? graph : graph[..comma];
    }

    /// <summary>Re-applies persisted Stage 7 mpv settings after engine start.</summary>
    public void ApplyStage7Settings()    {
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
        MpvPlayer? player = _player;
        if (player is null)
        {
            return Array.Empty<InspectorRow>();
        }

        return BuildInspectorSnapshot(
            player,
            _trackList.ToArray(),
            _playlistItems.Count,
            _currentPlaylistIndex,
            _chapterList.Count,
            _currentChapterIndex,
            _currentPath,
            _currentTitle,
            EqualizerDescription());
    }

    /// <summary>
    /// Gathers mpv properties on a worker thread so the inspector's 1.5 s
    /// refresh never blocks the UI thread.
    /// </summary>
    public Task<IReadOnlyList<InspectorRow>> GetInspectorSnapshotAsync()
    {
        MpvPlayer? player = _player;
        if (player is null)
        {
            return Task.FromResult<IReadOnlyList<InspectorRow>>(
                Array.Empty<InspectorRow>());
        }

        TrackInfo[] tracks = _trackList.ToArray();
        int playlistCount = _playlistItems.Count;
        int playlistIndex = _currentPlaylistIndex;
        int chapterCount = _chapterList.Count;
        int currentChapter = _currentChapterIndex;
        string? path = _currentPath;
        string? title = _currentTitle;
        string equalizer = EqualizerDescription();
        return Task.Run(() => BuildInspectorSnapshot(
            player,
            tracks,
            playlistCount,
            playlistIndex,
            chapterCount,
            currentChapter,
            path,
            title,
            equalizer));
    }

    private static IReadOnlyList<InspectorRow> BuildInspectorSnapshot(
        MpvPlayer player,
        IReadOnlyList<TrackInfo> tracks,
        int playlistCount,
        int playlistIndex,
        int chapterCount,
        int currentChapter,
        string? currentPath,
        string? currentTitle,
        string equalizer)
    {
        var rows = new List<InspectorRow>();

        void Add(string section, string name, string? value) =>
            rows.Add(new InspectorRow(
                section,
                name,
                string.IsNullOrWhiteSpace(value) ? "—" : value));

        string? Metadata(string key) =>
            player.GetPropertyString($"metadata/by-key/{key}");

        Add("文件", "路径", player.GetPropertyString("path") ?? currentPath);
        Add("文件", "标题", player.GetPropertyString("media-title") ?? currentTitle);
        Add("文件", "容器", player.GetPropertyString("file-format"));
        Add("文件", "文件大小", InspectorFormatter.Bytes(player.GetPropertyInt64("file-size")));
        Add("文件", "时长", TimeFormatter.Format(player.GetPropertyDouble("duration")));
        Add("文件", "可跳转", player.GetPropertyFlag("seekable") ? "是" : "否");
        Add("文件", "艺术家", Metadata("Artist") ?? Metadata("artist"));
        Add("文件", "专辑", Metadata("Album") ?? Metadata("album"));
        Add("文件", "编码器", Metadata("Encoder"));

        Add("播放", "位置", TimeFormatter.Format(player.GetPropertyDouble("time-pos")));
        Add("播放", "状态", player.GetPropertyFlag("pause") ? "暂停" : "播放中");
        Add("播放", "速度", $"{player.GetPropertyDouble("speed"):0.##}×");
        Add("播放", "缓存时间", TimeFormatter.Format(player.GetPropertyDouble("demuxer-cache-time")));
        Add("播放", "缓冲进度", $"{player.GetPropertyDouble("cache-buffering-state"):0}%");
        Add("播放", "缓存速度", InspectorFormatter.BitRate(player.GetPropertyDouble("cache-speed") * 8));

        int width = (int)player.GetPropertyInt64("width");
        int height = (int)player.GetPropertyInt64("height");
        int codedWidth = (int)player.GetPropertyInt64("video-params/w");
        int codedHeight = (int)player.GetPropertyInt64("video-params/h");
        int displayWidth = (int)player.GetPropertyInt64("video-params/dw");
        int displayHeight = (int)player.GetPropertyInt64("video-params/dh");
        double containerFps = player.GetPropertyDouble("container-fps");
        double estimatedFps = player.GetPropertyDouble("estimated-vf-fps");

        Add("视频", "编码", player.GetPropertyString("video-codec"));
        Add("视频", "分辨率", InspectorFormatter.Resolution(width, height));
        if (codedWidth > 0 && codedHeight > 0 &&
            (codedWidth != width || codedHeight != height))
        {
            Add("视频", "编码分辨率", InspectorFormatter.Resolution(codedWidth, codedHeight));
        }

        if (displayWidth > 0 && displayHeight > 0 &&
            (displayWidth != width || displayHeight != height))
        {
            Add("视频", "显示尺寸", InspectorFormatter.Resolution(displayWidth, displayHeight));
        }

        Add("视频", "帧率", InspectorFormatter.FrameRate(containerFps));
        if (estimatedFps > 0 && Math.Abs(estimatedFps - containerFps) > 0.01)
        {
            Add("视频", "输出帧率", InspectorFormatter.FrameRate(estimatedFps));
        }

        Add("视频", "像素格式", player.GetPropertyString("video-params/pixelformat"));
        Add("视频", "硬件像素格式", player.GetPropertyString("video-params/hw-pixelformat"));
        Add("视频", "视频码率", InspectorFormatter.BitRate(player.GetPropertyDouble("video-bitrate")));
        Add("视频", "旋转", $"{player.GetPropertyInt64("video-params/rotate")}°");
        Add("视频", "硬件解码", player.GetPropertyString("hwdec-current"));
        Add("视频", "硬件互操作", player.GetPropertyString("hwdec-interop"));
        Add("视频", "渲染 API", player.GetPropertyString("gpu-api"));
        Add("视频", "渲染上下文", player.GetPropertyString("gpu-context"));

        string? primaries = player.GetPropertyString("video-params/primaries");
        string? gamma = player.GetPropertyString("video-params/gamma");
        string? matrix = player.GetPropertyString("video-params/colormatrix");
        string? levels = player.GetPropertyString("video-params/colorlevels");
        double signalPeak = player.GetPropertyDouble("video-params/sig-peak");
        double minLuma = player.GetPropertyDouble("video-params/min-luma");
        double maxLuma = player.GetPropertyDouble("video-params/max-luma");
        Add("HDR / 色彩", "动态范围", InspectorFormatter.DynamicRange(gamma, maxLuma));
        Add("HDR / 色彩", "色彩参数", InspectorFormatter.Colour(
            primaries, gamma, matrix, levels, signalPeak, minLuma, maxLuma));
        Add("HDR / 色彩", "色度位置", player.GetPropertyString("video-params/chroma-location"));
        Add("HDR / 色彩", "色调映射", player.GetPropertyString("tone-mapping"));
        Add("HDR / 色彩", "目标峰值", player.GetPropertyString("target-peak"));

        Add("音频", "编码", player.GetPropertyString("audio-codec"));
        Add("音频", "采样率", InspectorFormatter.SampleRate(player.GetPropertyInt64("audio-samplerate")));
        Add("音频", "声道", InspectorFormatter.Channels(player.GetPropertyString("audio-channels")));
        Add("音频", "采样格式", player.GetPropertyString("audio-params/format"));
        Add("音频", "音频码率", InspectorFormatter.BitRate(player.GetPropertyDouble("audio-bitrate")));
        Add("音频", "输出设备", player.GetPropertyString("audio-device"));
        Add("音频", "延迟", $"{player.GetPropertyDouble("audio-delay"):0.###} s");
        Add("音频", "音量", $"{player.GetPropertyDouble("volume"):0.#}%" +
            (player.GetPropertyFlag("mute") ? "（静音）" : string.Empty));
        Add("音频", "均衡器", equalizer);

        int subtitleIndex = 0;
        foreach (TrackInfo track in tracks.Where(track => track.Type == TrackType.Subtitle))
        {
            subtitleIndex++;
            Add(
                "字幕",
                $"轨道 {subtitleIndex}",
                InspectorFormatter.SubtitleTrack(
                    track.Codec,
                    track.Language,
                    track.Title,
                    track.IsSelected));
        }

        if (subtitleIndex == 0)
        {
            Add("字幕", "轨道", "无");
        }

        Add("字幕", "可见", player.GetPropertyFlag("sub-visibility") ? "是" : "否");
        Add("字幕", "延迟", $"{player.GetPropertyDouble("sub-delay"):0.###} s");
        Add("字幕", "位置", $"{player.GetPropertyDouble("sub-pos"):0.#}");
        Add("字幕", "缩放", $"{player.GetPropertyDouble("sub-scale"):0.##}×");
        Add("字幕", "字体", player.GetPropertyString("sub-font"));
        Add("字幕", "字号", $"{player.GetPropertyDouble("sub-font-size"):0.#}");
        Add("字幕", "颜色", player.GetPropertyString("sub-color"));
        Add("字幕", "边框", $"{player.GetPropertyDouble("sub-border-size"):0.#}");
        Add("字幕", "ASS 覆盖", player.GetPropertyString("sub-ass-override"));

        foreach (IGrouping<TrackType, TrackInfo> group in tracks.GroupBy(track => track.Type))
        {
            Add(
                "轨道",
                group.Key switch
                {
                    TrackType.Video => "视频轨道",
                    TrackType.Audio => "音频轨道",
                    _ => "字幕轨道",
                },
                string.Join(
                    "；",
                    group.Select(track =>
                        $"{track.DisplayName} · {track.Codec} · {track.Language}" +
                        (track.IsSelected ? " · 已选中" : string.Empty))));
        }

        Add("轨道", "视频", tracks.Count(track => track.Type == TrackType.Video).ToString(CultureInfo.InvariantCulture));
        Add("轨道", "音频", tracks.Count(track => track.Type == TrackType.Audio).ToString(CultureInfo.InvariantCulture));
        Add("轨道", "字幕", tracks.Count(track => track.Type == TrackType.Subtitle).ToString(CultureInfo.InvariantCulture));
        Add("轨道", "已选 ID", $"vid={player.GetPropertyString("vid") ?? "auto"} · " +
            $"aid={player.GetPropertyString("aid") ?? "auto"} · " +
            $"sid={player.GetPropertyString("sid") ?? "auto"}");
        Add("播放列表", "条目", playlistCount.ToString(CultureInfo.InvariantCulture));
        Add("播放列表", "当前索引",
            playlistIndex >= 0 ? (playlistIndex + 1).ToString(CultureInfo.InvariantCulture) : "—");
        Add("章节", "数量", chapterCount.ToString(CultureInfo.InvariantCulture));
        Add("章节", "当前",
            currentChapter >= 0 ? (currentChapter + 1).ToString(CultureInfo.InvariantCulture) : "—");

        return rows;
    }

    private void AdjustAudioDelay(double delta)
    {
        _settings.AudioDelay = QuickSettings.ClampAudioDelay(_settings.AudioDelay + delta);
        ApplyMpvString("audio-delay", FormatDouble(_settings.AudioDelay));
        ShowOsd(Loc.T("osd.audioDelay"), $"{_settings.AudioDelay:+0.0;-0.0;0.0} s");
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
