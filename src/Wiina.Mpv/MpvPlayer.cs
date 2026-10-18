using System.Runtime.InteropServices;

namespace Wiina.Mpv;

/// <summary>One chapter entry as reported by mpv.</summary>
public readonly record struct MpvChapter(double Time, string? Title);

/// <summary>
/// Minimal, thread-safe libmpv wrapper for Stage 0.
/// The mpv event loop runs on a dedicated background thread; UI code must
/// marshal events to the dispatcher itself.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private nint _handle;
    private Thread? _eventThread;
    private CancellationTokenSource? _cts;
    private long _lastSwapChain;
    private bool _disposed;
    private readonly HashSet<string> _observed = new(StringComparer.Ordinal);

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

    public bool IsRunning => _handle != 0;

    public void Start(int width, int height)
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
            SetOption("force-window", "immediate");
            SetOption("idle", "yes");
            SetOption("keep-open", "yes");
            SetOption("terminal", "no");
            SetOption("config", "no");
            SetOption("input-default-bindings", "no");
            SetOption("osc", "no");
            SetOption("osd-level", "0");
            SetOption("hwdec", "auto-safe");

            Check(MpvInterop.mpv_initialize(_handle), "mpv_initialize");
            ObservePlaybackProperties();
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
        if (!_observed.Add(name))
        {
            return;
        }

        Check(MpvInterop.mpv_observe_property(_handle, 0, name, format), $"observe property {name}");
    }

    /// <summary>
    /// Properties needed by the OSC. The swapchain property stays opt-in
    /// because it is only meaningful for the composition host.
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
    }

    /// <summary>
    /// Chapter times/titles. Uses per-item property reads instead of the node
    /// API because Stage 2 only needs a flat list.
    /// </summary>
    public IReadOnlyList<MpvChapter> GetChapters()
    {
        if (_handle == 0)
        {
            return Array.Empty<MpvChapter>();
        }

        long count = GetPropertyInt64("chapter-list/count");
        if (count <= 0 || count > 10_000)
        {
            return Array.Empty<MpvChapter>();
        }

        var chapters = new List<MpvChapter>((int)count);
        for (int i = 0; i < count; i++)
        {
            double time = GetPropertyDouble($"chapter-list/{i}/time");
            string? title = GetPropertyString($"chapter-list/{i}/title");
            chapters.Add(new MpvChapter(time, title));
        }

        return chapters;
    }

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
            }
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
        }
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
