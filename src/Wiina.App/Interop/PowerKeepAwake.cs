using System.Runtime.InteropServices;

namespace Wiina.App.Interop;

/// <summary>
/// Keeps the display on and the system awake while Wiina plays media.
///
/// Preferred path: <c>PowerCreateRequest</c> + <c>PowerSetRequest</c> asking for
/// <c>PowerRequestDisplayRequired</c> and <c>PowerRequestSystemRequired</c>.
/// Those requests show up in <c>powercfg /requests</c> with this process and the
/// reason string below, which makes field reports diagnosable. When the
/// power-request API is unavailable (or refuses the request) the class falls
/// back to <c>SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED |
/// ES_DISPLAY_REQUIRED)</c>.
///
/// Every public method is best-effort: failures are swallowed so a
/// power-management problem can never throw into the player's UI thread.
/// </summary>
internal sealed class PowerKeepAwake : IDisposable
{
    private const string ReasonText = "Wiina is playing media";

    // POWER_REQUEST_TYPE (winnt.h).
    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;

    // EXECUTION_STATE (winbase.h).
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    // REASON_CONTEXT: version 0 with a simple string reason.
    private const uint PowerRequestContextSimpleString = 1;

    private readonly object _gate = new();
    private nint _request;
    private nint _reasonString;
    private bool _displaySet;
    private bool _systemSet;
    private bool _executionStateHeld;
    private bool _active;
    private bool _disposed;

    /// <summary>True while this instance currently holds a power request.</summary>
    internal bool IsActive => _active;

    /// <summary>
    /// Holds (<c>true</c>) or releases (<c>false</c>) the power request.
    /// Idempotent: repeating the current value is a cheap no-op.
    /// </summary>
    internal void Set(bool active)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || active == _active)
                {
                    return;
                }

                if (active)
                {
                    Acquire();
                }
                else
                {
                    Release();
                }

                _active = active;
            }
        }
        catch
        {
            // Power requests are opportunistic; never bubble a failure to the
            // UI thread and never block playback on them.
        }
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                Release();
                _active = false;
                _disposed = true;
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private void Acquire()
    {
        if (_request == 0)
        {
            CreateRequest();
        }

        if (_request != 0)
        {
            _displaySet = PowerSetRequest(_request, PowerRequestDisplayRequired);
            _systemSet = PowerSetRequest(_request, PowerRequestSystemRequired);
            if (_displaySet && _systemSet)
            {
                return;
            }

            // A half-applied request is worse than none because it is easy to
            // leave behind; clear it and use the legacy fallback instead.
            ClearPowerRequest();
        }

        _executionStateHeld =
            SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) != 0;
    }

    private void CreateRequest()
    {
        _reasonString = Marshal.StringToHGlobalUni(ReasonText);
        var context = new ReasonContext
        {
            Version = 0,
            Flags = PowerRequestContextSimpleString,
            SimpleReasonString = _reasonString,
        };
        _request = PowerCreateRequest(ref context);
    }

    private void Release()
    {
        ClearPowerRequest();
        if (_reasonString != 0)
        {
            Marshal.FreeHGlobal(_reasonString);
            _reasonString = 0;
        }

        if (_executionStateHeld)
        {
            SetThreadExecutionState(EsContinuous);
            _executionStateHeld = false;
        }
    }

    private void ClearPowerRequest()
    {
        if (_request == 0)
        {
            return;
        }

        if (_displaySet)
        {
            PowerClearRequest(_request, PowerRequestDisplayRequired);
            _displaySet = false;
        }

        if (_systemSet)
        {
            PowerClearRequest(_request, PowerRequestSystemRequired);
            _systemSet = false;
        }

        CloseHandle(_request);
        _request = 0;
    }

    // ------------------------------------------------------------- P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerSetRequest(nint powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PowerClearRequest(nint powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);
}
