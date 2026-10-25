namespace Wiina.Core.Playback;

/// <summary>What a graphics-device loss looks like to the app.</summary>
public enum DeviceLossSignal
{
    /// <summary>Not a device loss; normal rendering.</summary>
    None = 0,

    /// <summary>DXGI_ERROR_DEVICE_REMOVED (0x887A0005): the adapter disappeared.</summary>
    Removed,

    /// <summary>DXGI_ERROR_DEVICE_HUNG (0x887A0006): the GPU stopped answering.</summary>
    Hung,

    /// <summary>DXGI_ERROR_DEVICE_RESET (0x887A0007): the driver reset after a TDR.</summary>
    Reset,

    /// <summary>DXGI_ERROR_DRIVER_INTERNAL_ERROR (0x887A0020).</summary>
    DriverError,
}

/// <summary>
/// Recognises a lost D3D11 device from the two places Wiina can see one: an
/// HRESULT from binding mpv's composition swapchain to the SwapChainPanel, and
/// the lines mpv writes to its log when <c>vo/gpu-next</c> fails (the render
/// output prints the raw <c>0x887a0005</c> code rather than a friendly word).
/// </summary>
public static class DeviceLoss
{
    /// <summary>DXGI_ERROR_DEVICE_REMOVED.</summary>
    public const int DeviceRemoved = unchecked((int)0x887A0005);

    /// <summary>DXGI_ERROR_DEVICE_HUNG.</summary>
    public const int DeviceHung = unchecked((int)0x887A0006);

    /// <summary>DXGI_ERROR_DEVICE_RESET.</summary>
    public const int DeviceReset = unchecked((int)0x887A0007);

    /// <summary>DXGI_ERROR_DRIVER_INTERNAL_ERROR.</summary>
    public const int DriverInternalError = unchecked((int)0x887A0020);

    private static readonly string[] LogMarkers =
    {
        "887a0005",
        "887a0006",
        "887a0007",
        "887a0020",
        "device removed",
        "device lost",
        "device hung",
        "device reset",
        "dxgi_error_device",
    };

    /// <summary>True when <paramref name="hr"/> is one of the DXGI device-loss codes.</summary>
    public static bool IsDeviceLoss(int hr) => Classify(hr) != DeviceLossSignal.None;

    /// <summary>Classifies a raw HRESULT.</summary>
    public static DeviceLossSignal Classify(int hr) => hr switch
    {
        DeviceRemoved => DeviceLossSignal.Removed,
        DeviceHung => DeviceLossSignal.Hung,
        DeviceReset => DeviceLossSignal.Reset,
        DriverInternalError => DeviceLossSignal.DriverError,
        _ => DeviceLossSignal.None,
    };

    /// <summary>
    /// True when an mpv log line reports a lost device. The match is
    /// deliberately case-insensitive and substring-based: mpv mixes prefixes
    /// (<c>vo/gpu-next/d3d11</c>, <c>libplacebo</c>, <c>cplayer</c>) and prints
    /// either the hexadecimal code or an English phrase.
    /// </summary>
    public static bool MentionsDeviceLoss(string? logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return false;
        }

        foreach (string marker in LogMarkers)
        {
            if (logLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The signal implied by an mpv log line, or <see cref="DeviceLossSignal.None"/>.</summary>
    public static DeviceLossSignal ClassifyLogLine(string? logLine)
    {
        if (!MentionsDeviceLoss(logLine))
        {
            return DeviceLossSignal.None;
        }

        if (Mentions(logLine, "887a0006") || Mentions(logLine, "device hung"))
        {
            return DeviceLossSignal.Hung;
        }

        if (Mentions(logLine, "887a0007") || Mentions(logLine, "device reset"))
        {
            return DeviceLossSignal.Reset;
        }

        if (Mentions(logLine, "887a0020") || Mentions(logLine, "driver_internal"))
        {
            return DeviceLossSignal.DriverError;
        }

        return DeviceLossSignal.Removed;
    }

    private static bool Mentions(string? logLine, string marker) =>
        logLine is not null && logLine.Contains(marker, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Bounds how often Wiina rebuilds the video output after a device loss.
/// A lost adapter usually produces a burst of failures (every present call
/// reports the same code), so recovery must be attempted once per burst and a
/// hard limit must stop an endless reinit loop on permanently broken hardware.
/// </summary>
public sealed class DeviceLossRecoveryBudget
{
    /// <summary>Attempts allowed inside <see cref="Window"/>.</summary>
    public const int DefaultAttempts = 3;

    /// <summary>How long an attempt counts against the budget.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(2);

    /// <summary>Minimum gap between two attempts; a burst inside it is ignored.</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(5);

    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly TimeSpan _cooldown;

    /// <summary>Attempts recorded inside the current window.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the current window started (or <c>null</c> before the first attempt).</summary>
    public DateTimeOffset? WindowStarted { get; private set; }

    /// <summary>When the most recent attempt was granted.</summary>
    public DateTimeOffset? LastAttempt { get; private set; }

    public DeviceLossRecoveryBudget(
        int maxAttempts = DefaultAttempts,
        TimeSpan? window = null,
        TimeSpan? cooldown = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _window = window ?? DefaultWindow;
        _cooldown = cooldown ?? DefaultCooldown;
    }

    /// <summary>True while another recovery attempt is allowed.</summary>
    public bool CanAttempt => Attempts < _maxAttempts;

    /// <summary>True when <paramref name="now"/> is at least one cooldown after the last attempt.</summary>
    public bool IsCoolingDown(DateTimeOffset now) =>
        LastAttempt is { } last && now - last < _cooldown;

    /// <summary>
    /// Records an attempt and returns true when the caller should recover. False
    /// when the burst is still inside the cooldown, or when the budget for the
    /// current window is exhausted. The window resets automatically once it has
    /// elapsed, so a machine that loses the device again much later recovers.
    /// </summary>
    public bool TryBegin(DateTimeOffset now)
    {
        if (WindowStarted is { } started && now - started > _window)
        {
            Attempts = 0;
            WindowStarted = null;
            LastAttempt = null;
        }

        if (!CanAttempt || IsCoolingDown(now))
        {
            return false;
        }

        WindowStarted ??= now;
        LastAttempt = now;
        Attempts++;
        return true;
    }

    /// <summary>Clears the budget, e.g. after playback was reloaded by the user.</summary>
    public void Reset()
    {
        Attempts = 0;
        WindowStarted = null;
        LastAttempt = null;
    }
}
