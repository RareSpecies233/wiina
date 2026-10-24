using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Wiina.Core.State;

namespace Wiina.App.Interop;

/// <summary>
/// Small window subclass for things WinUI 3 does not expose:
/// WM_GETMINMAXINFO (minimum size) and WM_SIZING (aspect-ratio lock).
/// </summary>
internal static class WindowInterop
{
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_MOVING = 0x0216;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_APPCOMMAND = 0x0319;

    // DWM window-attribute ids used to make a snapped window flush with the
    // monitor edge (no 1 px border line, no rounded corner gap).
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>APPCOMMAND_* values carried by WM_APPCOMMAND (media keys).</summary>
    public const int AppCommandMediaNextTrack = 11;
    public const int AppCommandMediaPreviousTrack = 12;
    public const int AppCommandMediaStop = 13;
    public const int AppCommandMediaPlayPause = 14;
    public const int AppCommandMediaPlay = 46;
    public const int AppCommandMediaPause = 47;
    public const int AppCommandMediaFastForward = 49;
    public const int AppCommandMediaRewind = 50;
    private const int HTCLIENT = 1;

    private static readonly Dictionary<nint, Hook> Hooks = new();

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass,
        nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hWnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hWnd, int attribute, out RECT value, int size);

    public static void SetImmersiveDarkMode(Window window, bool dark)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }

    /// <summary>
    /// Removes the DWM frame decoration while the window is snapped flush
    /// against a monitor edge: the 1 px border (which shows up as a light line
    /// between the window and the edge) is suppressed and the rounded corners
    /// are disabled, so the client area really touches the screen edge. Both
    /// attributes are ignored on Windows 10, where the call is a no-op.
    /// </summary>
    public static void SetSnappedFrame(Window window, bool snapped)
    {
        try
        {
            nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            int corner = snapped ? DWMWCP_DONOTROUND : DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            int border = snapped ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Cosmetic only; never let it break dragging.
        }
    }

    // Frame margins are measured from DWM once per DPI (and re-measured at the
    // start of every drag gesture), so WM_MOVING does not pay for two extra
    // P/Invokes per pointer move.
    private static readonly Dictionary<nint, CachedFrameMargins> FrameMarginsByWindow = new();

    private readonly record struct CachedFrameMargins(uint Dpi, FrameMargins Margins);

    /// <summary>
    /// Visible-frame margins for a window (see <see cref="FrameMargins"/>): the
    /// distance between the outer window rect and the frame DWM actually paints.
    /// Snapping must happen in visible-frame space, otherwise the invisible
    /// resize borders (≈8 px at 100% on left/right/bottom) leave a visible gap
    /// on three edges while the top edge looks flush (issue 2).
    ///
    /// The result is cached per HWND and DPI. Call
    /// <see cref="InvalidateFrameMargins(nint)"/> after anything that can change
    /// the frame (drag start, presenter/fullscreen change); on failure the
    /// zero margins are returned so callers degrade to the old outer-rect
    /// behaviour instead of losing snapping.
    /// </summary>
    public static bool TryGetFrameMargins(Window window, out FrameMargins margins)
    {
        try
        {
            nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            return TryGetFrameMargins(hwnd, out margins);
        }
        catch
        {
            margins = FrameMargins.None;
            return false;
        }
    }

    /// <inheritdoc cref="TryGetFrameMargins(Window, out FrameMargins)"/>
    public static bool TryGetFrameMargins(nint hwnd, out FrameMargins margins)
    {
        margins = FrameMargins.None;
        if (hwnd == 0)
        {
            return false;
        }

        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (FrameMarginsByWindow.TryGetValue(hwnd, out CachedFrameMargins cached) && cached.Dpi == dpi)
            {
                margins = cached.Margins;
                return true;
            }

            if (!GetWindowRect(hwnd, out RECT outer) ||
                DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT visible, Marshal.SizeOf<RECT>()) != 0)
            {
                return false;
            }

            margins = FrameMargins.FromRects(
                new WindowRect(outer.Left, outer.Top, outer.Right, outer.Bottom),
                new WindowRect(visible.Left, visible.Top, visible.Right, visible.Bottom));
            FrameMarginsByWindow[hwnd] = new CachedFrameMargins(dpi, margins);
            return true;
        }
        catch
        {
            margins = FrameMargins.None;
            return false;
        }
    }

    /// <summary>Drops the cached frame margins for a window.</summary>
    public static void InvalidateFrameMargins(nint hwnd) => FrameMarginsByWindow.Remove(hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private const int IDC_ARROW = 32512;

    private delegate nint SubclassProc(
        nint hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData);

    public static void Attach(
        Window window,
        int minWidthDip,
        int minHeightDip,
        Func<bool> lockAspect,
        Func<double> aspect,
        Func<int, bool>? appCommand = null,
        Action? windowResizeStarted = null,
        Action? windowMoveEnded = null,
        Func<WindowRect, WindowRect>? windowMoving = null,
        Action? windowMoveStarted = null)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        if (Hooks.ContainsKey(hwnd))
        {
            return;
        }

        var hook = new Hook(
            hwnd,
            minWidthDip,
            minHeightDip,
            lockAspect,
            aspect,
            appCommand,
            windowResizeStarted,
            windowMoveEnded,
            windowMoving,
            windowMoveStarted);
        Hooks[hwnd] = hook;
        hook.Attach();
    }

    public static void Detach(Window window)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        if (Hooks.Remove(hwnd, out Hook? hook))
        {
            hook.Detach();
        }
    }

    /// <summary>
    /// True while the OS pointer is inside the window rect. WinUI raises
    /// PointerExited on the root when the pointer moves between child
    /// elements too, so the root-exit handler must filter those.
    /// </summary>
    public static bool IsCursorInsideWindow(Window window)
    {
        try
        {
            nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            return GetCursorPos(out POINT point) &&
                GetWindowRect(hwnd, out RECT rect) &&
                point.X >= rect.Left &&
                point.X < rect.Right &&
                point.Y >= rect.Top &&
                point.Y < rect.Bottom;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hide the pointer while the chrome is hidden. WinUI keeps resetting the
    /// cursor on pointer moves, so callers re-apply this from PointerMoved.
    /// </summary>
    public static void SetCursorVisible(bool visible)
    {
        try
        {
            SetCursor(visible ? LoadCursor(0, IDC_ARROW) : 0);
        }
        catch
        {
            // Cursor feedback must never break playback.
        }
    }

    /// <summary>
    /// True while the left mouse button is physically down. Used by the OSC
    /// auto-hide logic so dragging a slider past the window edge cannot hide
    /// the chrome mid-gesture.
    /// </summary>
    public static bool IsLeftButtonDown() => (GetKeyState(0x01) & 0x8000) != 0;

    /// <summary>Global key state for shortcut modifier detection.</summary>
    public static bool IsKeyDown(Windows.System.VirtualKey key)
    {
        try
        {
            return (GetKeyState((int)key) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>System double-click interval, clamped to a usable range.</summary>
    public static int GetDoubleClickMilliseconds()
    {
        try
        {
            uint milliseconds = GetDoubleClickTime();
            return milliseconds is >= 100 and <= 1000 ? (int)milliseconds : 500;
        }
        catch
        {
            return 500;
        }
    }

    /// <summary>Screen cursor position in physical pixels.</summary>
    public static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out POINT point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private sealed class Hook
    {
        private readonly nint _hwnd;
        private readonly int _minWidthDip;
        private readonly int _minHeightDip;
        private readonly Func<bool> _lockAspect;
        private readonly Func<double> _aspect;
        private readonly Func<int, bool>? _appCommand;
        private readonly Action? _windowResizeStarted;
        private readonly Action? _windowMoveEnded;
        private readonly Func<WindowRect, WindowRect>? _windowMoving;
        private readonly Action? _windowMoveStarted;
        private readonly SubclassProc _proc;
        private bool _sizingGesture;

        public Hook(
            nint hwnd,
            int minWidthDip,
            int minHeightDip,
            Func<bool> lockAspect,
            Func<double> aspect,
            Func<int, bool>? appCommand,
            Action? windowResizeStarted,
            Action? windowMoveEnded,
            Func<WindowRect, WindowRect>? windowMoving,
            Action? windowMoveStarted)
        {
            _hwnd = hwnd;
            _minWidthDip = minWidthDip;
            _minHeightDip = minHeightDip;
            _lockAspect = lockAspect;
            _aspect = aspect;
            _appCommand = appCommand;
            _windowResizeStarted = windowResizeStarted;
            _windowMoveEnded = windowMoveEnded;
            _windowMoving = windowMoving;
            _windowMoveStarted = windowMoveStarted;
            _proc = WndProc;
        }

        public void Attach() => SetWindowSubclass(_hwnd, _proc, 1, 0);

        public void Detach() => RemoveWindowSubclass(_hwnd, _proc, 1);

        private nint WndProc(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nint dwRefData)
        {
            if (msg == WM_APPCOMMAND && _appCommand is not null)
            {
                // GET_APPCOMMAND_LPARAM: HIWORD without the FAPPCOMMAND flag.
                int command = (int)(((long)lParam >> 16) & 0x0FFF);
                if (_appCommand(command))
                {
                    return 1;
                }
            }

            if (msg == WM_GETMINMAXINFO)
            {
                MINMAXINFO info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                double scale = GetDpiForWindow(hWnd) / 96.0;
                info.ptMinTrackSize.X = (int)Math.Round(_minWidthDip * scale);
                info.ptMinTrackSize.Y = (int)Math.Round(_minHeightDip * scale);
                Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                return 0;
            }

            if (msg == WM_MOVING && _windowMoving is not null)
            {
                RECT moving = Marshal.PtrToStructure<RECT>(lParam);
                WindowRect snapped = _windowMoving(
                    new WindowRect(moving.Left, moving.Top, moving.Right, moving.Bottom));
                moving.Left = snapped.Left;
                moving.Top = snapped.Top;
                moving.Right = snapped.Right;
                moving.Bottom = snapped.Bottom;
                Marshal.StructureToPtr(moving, lParam, fDeleteOld: false);
                return 1;
            }

            if (msg == WM_ENTERSIZEMOVE)
            {
                // A new drag/resize gesture starts; edge-snap state must not leak
                // across gestures (the window may have been snapped elsewhere),
                // and the frame margins are re-measured once for this gesture.
                InvalidateFrameMargins(hWnd);
                _windowMoveStarted?.Invoke();
            }

            if (msg == WM_SIZING && !_sizingGesture)
            {
                _sizingGesture = true;
                _windowResizeStarted?.Invoke();
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                _sizingGesture = false;
                _windowMoveEnded?.Invoke();
            }

            if (msg == WM_SIZING && _lockAspect())
            {
                double aspect = _aspect();
                if (aspect > 0.05 &&
                    Enum.IsDefined(typeof(WindowResizeEdge), (int)wParam))
                {
                    RECT rect = Marshal.PtrToStructure<RECT>(lParam);
                    double scale = GetDpiForWindow(hWnd) / 96.0;
                    WindowRect resized = WindowGeometry.ResizeWithAspect(
                        new WindowRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                        (WindowResizeEdge)(int)wParam,
                        aspect,
                        (int)Math.Round(_minWidthDip * scale),
                        (int)Math.Round(_minHeightDip * scale));

                    rect.Left = resized.Left;
                    rect.Top = resized.Top;
                    rect.Right = resized.Right;
                    rect.Bottom = resized.Bottom;
                    Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
                    return 1;
                }
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
