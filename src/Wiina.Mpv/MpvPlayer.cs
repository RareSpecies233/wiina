using System.Runtime.InteropServices;

namespace Wiina.Mpv;

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

    /// <summary>Raised on the mpv event thread with the raw DXGI swapchain pointer.</summary>
    public event Action<long>? DisplaySwapChainChanged;

    /// <summary>Raised on the mpv event thread for diagnostics.</summary>
    public event Action<string>? Log;

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
        Check(MpvInterop.mpv_observe_property(_handle, 0, name, format), $"observe property {name}");
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
        if (name != "display-swapchain"
            || prop.Format != MpvFormat.Int64
            || prop.Data == 0)
        {
            return;
        }

        long swapChain = Marshal.ReadInt64(prop.Data);
        long previous = Interlocked.Exchange(ref _lastSwapChain, swapChain);
        if (swapChain != previous)
        {
            Log?.Invoke($"display-swapchain = 0x{swapChain:x}.");
            DisplaySwapChainChanged?.Invoke(swapChain);
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
