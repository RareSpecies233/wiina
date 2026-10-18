using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Wiina.Core.Models;

namespace Wiina.Mpv;

/// <summary>One chapter entry as reported by mpv (Stage 2 compatibility).</summary>
public readonly record struct MpvChapter(double Time, string? Title);

/// <summary>One mpv playlist entry.</summary>
public sealed record MpvPlaylistItem(
    int Index,
    string? Filename,
    bool IsCurrent,
    bool IsPlaying)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Filename))
            {
                try
                {
                    string name = System.IO.Path.GetFileName(Filename);
                    return string.IsNullOrWhiteSpace(name) ? Filename : name;
                }
                catch
                {
                    return Filename;
                }
            }

            return $"条目 {Index + 1}";
        }
    }
}

/// <summary>One entry of mpv's <c>audio-device-list</c>.</summary>
public sealed record MpvAudioDevice(string Name, string Description)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Description) ? Name : Description;
}

/// <summary>
/// Thread-safe libmpv wrapper. The mpv event loop runs on a dedicated
/// background thread; UI code must marshal events to the dispatcher itself.
/// Commands from the UI thread use <see cref="CommandAsync"/> so a slow file
/// open or seek never blocks XAML.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private const int EndFileReasonEof = 0;
    private const int EndFileReasonStop = 2;
    private const int EndFileReasonQuit = 3;
    private const int EndFileReasonError = 4;

    private nint _handle;
    private Thread? _eventThread;
    private CancellationTokenSource? _cts;
    private long _lastSwapChain;
    private bool _disposed;
    private long _commandId;
    private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ulong, string> _pendingCommands = new();
    private IReadOnlyList<TrackInfo> _trackCache = Array.Empty<TrackInfo>();
    private IReadOnlyList<ChapterInfo> _chapterCache = Array.Empty<ChapterInfo>();
    private IReadOnlyList<MpvPlaylistItem> _playlistCache = Array.Empty<MpvPlaylistItem>();
    private IReadOnlyList<MpvAudioDevice> _audioDeviceCache = Array.Empty<MpvAudioDevice>();

    /// <summary>Raised on the mpv event thread with the raw DXGI swapchain pointer.</summary>
    public event Action<long>? DisplaySwapChainChanged;

    /// <summary>Raised on the mpv event thread for diagnostics.</summary>
    public event Action<string>? Log;

    /// <summary>Numeric mpv property update (time-pos, duration, volume, ...).</summary>
    public event Action<string, double>? NumberPropertyChanged;

    /// <summary>Boolean mpv property update (pause, mute, ...).</summary>
    public event Action<string, bool>? FlagPropertyChanged;

    /// <summary>String mpv property update.</summary>
    public event Action<string, string?>? StringPropertyChanged;

    /// <summary>Raised when a file is loaded (headers read, decoding starts).</summary>
    public event Action? FileLoaded;

    /// <summary>Raised with the parsed <c>track-list</c> node.</summary>
    public event Action<IReadOnlyList<TrackInfo>>? TrackListChanged;

    /// <summary>Raised with the parsed <c>chapter-list</c> node.</summary>
    public event Action<IReadOnlyList<ChapterInfo>>? ChapterListChanged;

    /// <summary>Raised with the parsed <c>playlist</c> node.</summary>
    public event Action<IReadOnlyList<MpvPlaylistItem>>? PlaylistChanged;

    /// <summary>Raised with the parsed <c>audio-device-list</c> node.</summary>
    public event Action<IReadOnlyList<MpvAudioDevice>>? AudioDeviceListChanged;

    /// <summary>Raised with the current playlist index.</summary>
    public event Action<int>? PlaylistPositionChanged;

    /// <summary>Raised with the current chapter index (0-based, -1 = none).</summary>
    public event Action<int>? ChapterChanged;

    /// <summary>Raised when video dimensions become known.</summary>
    public event Action<int, int>? VideoSizeChanged;

    /// <summary>Raised when a file fails to load (mpv error code/message).</summary>
    public event Action<int, string?>? FileLoadFailed;

    /// <summary>Raised when an async command fails (description/message).</summary>
    public event Action<string, string?>? CommandFailed;

    public bool IsRunning => _handle != 0;

    public void Start(int width, int height)
    {
        Start(width, height, headless: false);
    }

    /// <summary>
    /// Starts the client. <paramref name="headless"/> is used by the Stage 5
    /// thumbnail extractor: no window, no audio, paused and kept open so the
    /// background worker can seek and call <see cref="ScreenshotRaw"/>.
    /// </summary>
    public void Start(int width, int height, bool headless)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle != 0)
        {
            throw new InvalidOperationException("MpvPlayer is already started.");
        }

        _handle = MpvInterop.mpv_create();
        if (_handle == 0)
        {
            throw new MpvException("mpv_create returned null.");
        }

        try
        {
            // Video output: D3D11 composition mode, no window of its own.
            SetOption("vo", "gpu-next");
            SetOption("gpu-context", "d3d11");
            SetOption("gpu-api", "d3d11");
            SetOption("d3d11-output-mode", "composition");
            SetOption("d3d11-composition-size", $"{Math.Max(1, width)}x{Math.Max(1, height)}");

            // Embedding-friendly defaults. Wiina draws its own OSC/OSD.
            SetOption("force-window", headless ? "no" : "immediate");
            SetOption("idle", "yes");
            SetOption("keep-open", "yes");
            SetOption("terminal", "no");
            SetOption("config", "no");
            SetOption("input-default-bindings", "no");
            SetOption("osc", "no");
            SetOption("osd-level", "0");
            SetOption("hwdec", "auto-safe");
            SetOption("screenshot-format", "png");
            SetOption("screenshot-directory", DefaultScreenshotFolder());
            SetOption("sub-visibility", "yes");

            if (headless)
            {
                SetOption("audio", "no");
                SetOption("pause", "yes");
                SetOption("untimed", "yes");
                SetOption("hr-seek", "yes");
                SetOption("video-sync", "audio");
                SetOption("cache", "no");
            }

            Check(MpvInterop.mpv_initialize(_handle), "mpv_initialize");
            ObservePlaybackProperties();
            MpvInterop.mpv_request_log_messages(_handle, "error");
        }
        catch
        {
            MpvInterop.mpv_terminate_destroy(_handle);
            _handle = 0;
            throw;
        }

        _cts = new CancellationTokenSource();
        _eventThread = new Thread(EventLoop)
        {
            IsBackground = true,
            Name = "mpv-event",
        };
        _eventThread.Start();
        Log?.Invoke($"mpv initialized (client API version 0x{MpvInterop.mpv_client_api_version():x}).");
    }

    public void SetOption(string name, string value)
    {
        Check(MpvInterop.mpv_set_option_string(_handle, name, value), $"set option {name}");
    }

    public void SetPropertyString(string name, string value)
    {
        Check(MpvInterop.mpv_set_property_string(_handle, name, value), $"set property {name}");
    }

    public bool TrySetPropertyString(string name, string value)
    {
        if (_handle == 0)
        {
            return false;
        }

        return MpvInterop.mpv_set_property_string(_handle, name, value) >= 0;
    }

    public long GetPropertyInt64(string name)
    {
        if (_handle == 0)
        {
            return 0;
        }

        return MpvInterop.mpv_get_property(_handle, name, MpvFormat.Int64, out long value) >= 0
            ? value
            : 0;
    }

    public double GetPropertyDouble(string name)
    {
        if (_handle == 0)
        {
            return 0;
        }

        return MpvInterop.mpv_get_property_double(_handle, name, MpvFormat.Double, out double value) >= 0
            ? value
            : 0;
    }

    public bool GetPropertyFlag(string name)
    {
        if (_handle == 0)
        {
            return false;
        }

        return MpvInterop.mpv_get_property_flag(_handle, name, MpvFormat.Flag, out int value) >= 0
            && value != 0;
    }

    public string? GetPropertyString(string name)
    {
        if (_handle == 0)
        {
            return null;
        }

        nint ptr = MpvInterop.mpv_get_property_string(_handle, name);
        if (ptr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            MpvInterop.mpv_free(ptr);
        }
    }

    public void ObserveProperty(string name, MpvFormat format)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == 0)
        {
            throw new InvalidOperationException("MpvPlayer is not started.");
        }

        if (_observed.Contains(name))
        {
            return;
        }

        Check(MpvInterop.mpv_observe_property(_handle, 0, name, format), $"observe property {name}");
        _observed.Add(name);
    }

    /// <summary>Observes without throwing when mpv lacks the property.</summary>
    public bool TryObserveProperty(string name, MpvFormat format)
    {
        if (_handle == 0 || !_observed.Add(name))
        {
            return _handle != 0;
        }

        int result = MpvInterop.mpv_observe_property(_handle, 0, name, format);
        if (result < 0)
        {
            _observed.Remove(name);
            Log?.Invoke($"observe {name} failed: {ErrorString(result)}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Properties needed by the OSC and Stage 3 track/chapter/playlist UI.
    /// The swapchain property stays opt-in because it is only meaningful for
    /// the composition host.
    /// </summary>
    private void ObservePlaybackProperties()
    {
        ObserveProperty("time-pos", MpvFormat.Double);
        ObserveProperty("duration", MpvFormat.Double);
        ObserveProperty("pause", MpvFormat.Flag);
        ObserveProperty("volume", MpvFormat.Double);
        ObserveProperty("mute", MpvFormat.Flag);
        ObserveProperty("speed", MpvFormat.Double);
        ObserveProperty("demuxer-cache-time", MpvFormat.Double);
        ObserveProperty("media-title", MpvFormat.String);
        ObserveProperty("path", MpvFormat.String);

        TryObserveProperty("track-list", MpvFormat.Node);
        TryObserveProperty("chapter-list", MpvFormat.Node);
        TryObserveProperty("chapter", MpvFormat.Int64);
        TryObserveProperty("playlist", MpvFormat.Node);
        TryObserveProperty("audio-device-list", MpvFormat.Node);
        TryObserveProperty("audio-device", MpvFormat.String);
        TryObserveProperty("playlist-pos", MpvFormat.Int64);
        TryObserveProperty("playlist-count", MpvFormat.Int64);
        TryObserveProperty("playlist-current-pos", MpvFormat.Int64);
        TryObserveProperty("video-params", MpvFormat.Node);
        TryObserveProperty("shuffle", MpvFormat.Flag);
        TryObserveProperty("loop-playlist", MpvFormat.String);
        TryObserveProperty("sub-visibility", MpvFormat.Flag);
        TryObserveProperty("eof-reached", MpvFormat.Flag);
        TryObserveProperty("idle-active", MpvFormat.Flag);

        // Stage 5 status overlays.
        TryObserveProperty("paused-for-cache", MpvFormat.Flag);
        TryObserveProperty("seeking", MpvFormat.Flag);
        TryObserveProperty("cache-buffering-state", MpvFormat.Double);
        TryObserveProperty("cache-speed", MpvFormat.Double);
        TryObserveProperty("demuxer-cache-state", MpvFormat.Node);
    }

    /// <summary>Last parsed chapter list (updated on the mpv event thread).</summary>
    public IReadOnlyList<ChapterInfo> GetChapters() => _chapterCache;

    /// <summary>Last parsed track list (updated on the mpv event thread).</summary>
    public IReadOnlyList<TrackInfo> GetTracks() => _trackCache;

    /// <summary>Last parsed playlist (updated on the mpv event thread).</summary>
    public IReadOnlyList<MpvPlaylistItem> GetPlaylist() => _playlistCache;

    /// <summary>Last parsed audio device list (updated on the mpv event thread).</summary>
    public IReadOnlyList<MpvAudioDevice> GetAudioDevices() => _audioDeviceCache;

    public int GetCurrentChapter() => (int)GetPropertyInt64("chapter");

    /// <summary>Synchronous command; prefer <see cref="CommandAsync"/> on the UI thread.</summary>
    public void Command(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("mpv command must have a name.", nameof(args));
        }

        nint[] strings = new nint[args.Length];
        nint array = 0;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            array = Marshal.AllocHGlobal(IntPtr.Size * (args.Length + 1));
            for (int i = 0; i < strings.Length; i++)
            {
                Marshal.WriteIntPtr(array, i * IntPtr.Size, strings[i]);
            }

            Marshal.WriteIntPtr(array, strings.Length * IntPtr.Size, 0);
            Check(MpvInterop.mpv_command(_handle, array), $"command {args[0]}");
        }
        finally
        {
            foreach (nint p in strings)
            {
                if (p != 0)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }

            if (array != 0)
            {
                Marshal.FreeHGlobal(array);
            }
        }
    }

    /// <summary>
    /// Queues a command on mpv's command queue. The arguments are copied by
    /// libmpv immediately, so this is safe from the XAML thread and does not
    /// block it.
    /// </summary>
    public void CommandAsync(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("mpv command must have a name.", nameof(args));
        }

        if (_handle == 0)
        {
            throw new InvalidOperationException("MpvPlayer is not started.");
        }

        ulong id = (ulong)Interlocked.Increment(ref _commandId);
        string description = string.Join(' ', args);
        _pendingCommands[id] = description;

        nint[] strings = new nint[args.Length];
        nint array = 0;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                strings[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            array = Marshal.AllocHGlobal(IntPtr.Size * (args.Length + 1));
            for (int i = 0; i < strings.Length; i++)
            {
                Marshal.WriteIntPtr(array, i * IntPtr.Size, strings[i]);
            }

            Marshal.WriteIntPtr(array, strings.Length * IntPtr.Size, 0);
            Check(
                MpvInterop.mpv_command_async(_handle, id, array),
                $"command {args[0]}");
        }
        catch
        {
            _pendingCommands.TryRemove(id, out _);
            throw;
        }
        finally
        {
            foreach (nint p in strings)
            {
                if (p != 0)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }

            if (array != 0)
            {
                Marshal.FreeHGlobal(array);
            }
        }
    }

    /// <summary>
    /// Runs <c>screenshot-raw</c> synchronously and returns the decoded frame.
    /// Only call this off the UI thread; a 4K frame is tens of megabytes.
    /// </summary>
    public unsafe MpvScreenshot? ScreenshotRaw()
    {
        if (_handle == 0)
        {
            throw new InvalidOperationException("MpvPlayer is not started.");
        }

        nint name = Marshal.StringToCoTaskMemUTF8("screenshot-raw");
        MpvNode* values = stackalloc MpvNode[1];
        values[0] = new MpvNode { Data = name, Format = MpvFormat.String };
        MpvNodeList list = new()
        {
            Count = 1,
            Values = (nint)values,
            Keys = 0,
        };
        MpvNode arguments = new()
        {
            Data = (nint)(&list),
            Format = MpvFormat.NodeArray,
        };
        try
        {
            int result = MpvInterop.mpv_command_node(_handle, (nint)(&arguments), out MpvNode node);
            try
            {
                if (result < 0)
                {
                    Log?.Invoke($"screenshot-raw failed: {ErrorString(result)}");
                    return null;
                }

                return MpvScreenshotReader.Read((nint)(&node));
            }
            finally
            {
                MpvInterop.mpv_free_node_contents(ref node);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    private void EventLoop()
    {
        CancellationToken token = _cts?.Token ?? CancellationToken.None;
        while (!token.IsCancellationRequested)
        {
            nint eventPtr;
            try
            {
                eventPtr = MpvInterop.mpv_wait_event(_handle, 1.0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log?.Invoke($"mpv_wait_event failed: {ex.Message}");
                return;
            }

            if (eventPtr == 0)
            {
                continue;
            }

            MpvEvent ev = Marshal.PtrToStructure<MpvEvent>(eventPtr);
            switch (ev.EventId)
            {
                case MpvEventId.Shutdown:
                    Log?.Invoke("mpv shutdown event.");
                    return;

                case MpvEventId.PropertyChange:
                    HandlePropertyChange(ev);
                    break;

                case MpvEventId.FileLoaded:
                    FileLoaded?.Invoke();
                    break;

                case MpvEventId.StartFile:
                    Log?.Invoke("StartFile");
                    break;

                case MpvEventId.EndFile:
                    HandleEndFile(ev);
                    break;

                case MpvEventId.CommandReply:
                    HandleCommandReply(ev);
                    break;

                case MpvEventId.LogMessage:
                    HandleLogMessage(ev);
                    break;
            }
        }
    }

    private void HandleEndFile(MpvEvent ev)
    {
        if (ev.Data == 0)
        {
            return;
        }

        MpvEventEndFile end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
        switch (end.Reason)
        {
            case EndFileReasonError:
                FileLoadFailed?.Invoke(end.Error, ErrorString(end.Error));
                break;
            case EndFileReasonEof:
            case EndFileReasonStop:
            case EndFileReasonQuit:
            default:
                break;
        }
    }

    private void HandleCommandReply(MpvEvent ev)
    {
        if (!_pendingCommands.TryRemove(ev.ReplyUserData, out string? description))
        {
            return;
        }

        if (ev.Error < 0)
        {
            CommandFailed?.Invoke(description, ErrorString(ev.Error));
        }
    }

    private void HandleLogMessage(MpvEvent ev)
    {
        if (ev.Data == 0)
        {
            return;
        }

        MpvEventLogMessage message = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
        string? prefix = message.Prefix == 0
            ? null
            : Marshal.PtrToStringUTF8(message.Prefix);
        string? text = message.Text == 0
            ? null
            : Marshal.PtrToStringUTF8(message.Text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            Log?.Invoke($"{(string.IsNullOrEmpty(prefix) ? "mpv" : prefix)}: {text.TrimEnd()}");
        }
    }

    private void HandlePropertyChange(MpvEvent ev)
    {
        if (ev.Data == 0)
        {
            return;
        }

        MpvEventProperty prop = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);
        string? name = Marshal.PtrToStringUTF8(prop.Name);
        if (string.IsNullOrEmpty(name) || prop.Format == MpvFormat.None || prop.Data == 0)
        {
            return;
        }

        switch (prop.Format)
        {
            case MpvFormat.Int64:
            {
                long value = Marshal.ReadInt64(prop.Data);
                if (name == "display-swapchain")
                {
                    long previous = Interlocked.Exchange(ref _lastSwapChain, value);
                    if (value != previous)
                    {
                        Log?.Invoke($"display-swapchain = 0x{value:x}.");
                        DisplaySwapChainChanged?.Invoke(value);
                    }
                }
                else if (name is "playlist-pos" or "playlist-current-pos")
                {
                    PlaylistPositionChanged?.Invoke((int)value);
                }
                else if (name == "chapter")
                {
                    ChapterChanged?.Invoke((int)value);
                }
                else
                {
                    NumberPropertyChanged?.Invoke(name, value);
                }

                break;
            }

            case MpvFormat.Double:
                NumberPropertyChanged?.Invoke(
                    name,
                    Marshal.PtrToStructure<double>(prop.Data));
                break;

            case MpvFormat.Flag:
                FlagPropertyChanged?.Invoke(name, Marshal.ReadInt32(prop.Data) != 0);
                break;

            case MpvFormat.String:
            {
                nint stringPtr = Marshal.ReadIntPtr(prop.Data);
                StringPropertyChanged?.Invoke(
                    name,
                    stringPtr == 0 ? null : Marshal.PtrToStringUTF8(stringPtr));
                break;
            }

            case MpvFormat.Node:
            case MpvFormat.NodeArray:
            case MpvFormat.NodeMap:
                HandleNodeProperty(name, prop.Data);
                break;
        }
    }

    private void HandleNodeProperty(string name, nint data)
    {
        try
        {
            switch (name)
            {
                case "track-list":
                {
                    IReadOnlyList<TrackInfo> tracks =
                        ParseTracks(MpvNodeValue.Read(data));
                    _trackCache = tracks;
                    TrackListChanged?.Invoke(tracks);
                    break;
                }

                case "chapter-list":
                {
                    IReadOnlyList<ChapterInfo> chapters =
                        ParseChapters(MpvNodeValue.Read(data));
                    _chapterCache = chapters;
                    ChapterListChanged?.Invoke(chapters);
                    break;
                }

                case "playlist":
                {
                    IReadOnlyList<MpvPlaylistItem> playlist =
                        ParsePlaylist(MpvNodeValue.Read(data));
                    _playlistCache = playlist;
                    PlaylistChanged?.Invoke(playlist);
                    break;
                }
                case "audio-device-list":
                {
                    IReadOnlyList<MpvAudioDevice> devices =
                        ParseAudioDevices(MpvNodeValue.Read(data));
                    _audioDeviceCache = devices;
                    AudioDeviceListChanged?.Invoke(devices);
                    break;
                }
                case "video-params":
                {
                    MpvNodeValue? node = MpvNodeValue.Read(data);
                    if (node is not null)
                    {
                        int width = node["w"]?.AsInt() ?? 0;
                        int height = node["h"]?.AsInt() ?? 0;
                        if (width > 0 && height > 0)
                        {
                            VideoSizeChanged?.Invoke(width, height);
                        }
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"node property {name} parse failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<TrackInfo> ParseTracks(MpvNodeValue? root)
    {
        var tracks = new List<TrackInfo>();
        if (root?.ArrayValue is null)
        {
            return tracks;
        }

        foreach (MpvNodeValue item in root.ArrayValue)
        {
            int id = item["id"]?.AsInt() ?? -1;
            string? type = item["type"]?.AsString();
            if (id < 0 || type is null)
            {
                continue;
            }

            TrackType? trackType = type switch
            {
                "video" => TrackType.Video,
                "audio" => TrackType.Audio,
                "sub" => TrackType.Subtitle,
                _ => null,
            };
            if (trackType is null)
            {
                continue;
            }

            tracks.Add(new TrackInfo(
                id,
                trackType.Value,
                item["title"]?.AsString(),
                item["lang"]?.AsString(),
                item["codec"]?.AsString(),
                item["default"]?.AsFlag() ?? false,
                item["selected"]?.AsFlag() ?? false,
                item["external"]?.AsFlag() ?? false));
        }

        return tracks;
    }

    private static IReadOnlyList<ChapterInfo> ParseChapters(MpvNodeValue? root)
    {
        var chapters = new List<ChapterInfo>();
        if (root?.ArrayValue is null)
        {
            return chapters;
        }

        for (int i = 0; i < root.ArrayValue.Count; i++)
        {
            MpvNodeValue item = root.ArrayValue[i];
            chapters.Add(new ChapterInfo(
                i,
                item["time"]?.AsDouble() ?? 0,
                item["title"]?.AsString()));
        }

        return chapters;
    }

    private static IReadOnlyList<MpvAudioDevice> ParseAudioDevices(MpvNodeValue? root)
    {
        var devices = new List<MpvAudioDevice>();
        if (root?.ArrayValue is null)
        {
            return devices;
        }

        foreach (MpvNodeValue item in root.ArrayValue)
        {
            string? name = item["name"]?.AsString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            devices.Add(new MpvAudioDevice(
                name,
                item["description"]?.AsString() ?? name));
        }

        return devices;
    }

    private static IReadOnlyList<MpvPlaylistItem> ParsePlaylist(MpvNodeValue? root)
    {
        var items = new List<MpvPlaylistItem>();
        if (root?.ArrayValue is null)
        {
            return items;
        }

        for (int i = 0; i < root.ArrayValue.Count; i++)
        {
            MpvNodeValue item = root.ArrayValue[i];
            items.Add(new MpvPlaylistItem(
                i,
                item["filename"]?.AsString(),
                item["current"]?.AsFlag() ?? false,
                item["playing"]?.AsFlag() ?? false));
        }

        return items;
    }

    private static string DefaultScreenshotFolder()
    {
        try
        {
            string pictures = Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
            {
                pictures = Path.GetTempPath();
            }

            string folder = Path.Combine(pictures, "Wiina");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    private static string? ErrorString(int code)
    {
        if (code >= 0)
        {
            return null;
        }

        nint pointer = MpvInterop.mpv_error_string(code);
        return pointer == 0 ? null : Marshal.PtrToStringUTF8(pointer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        if (_handle != 0)
        {
            MpvInterop.mpv_wakeup(_handle);
        }

        _eventThread?.Join(TimeSpan.FromSeconds(3));
        if (_handle != 0)
        {
            MpvInterop.mpv_terminate_destroy(_handle);
            _handle = 0;
        }

        _pendingCommands.Clear();
        _cts?.Dispose();
        _cts = null;
        _eventThread = null;
    }

    private static void Check(int code, string operation)
    {
        if (code < 0)
        {
            throw new MpvException(operation, code);
        }
    }
}
