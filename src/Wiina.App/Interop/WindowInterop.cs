using System.Reflection;
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
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_NCMOUSEMOVE = 0x00A0;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_POINTERUPDATE = 0x0245;
    private const uint WM_POINTERENTER = 0x0249;

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

    /// <summary>Top-level player window whose WinUI children get the cursor hook.</summary>
    private static nint _cursorRootHwnd;

    private static readonly HashSet<nint> CursorHookedChildren = new();

    private static SubclassProc? _cursorChildProc;

    private static long _cursorChildHits;

    private static PropertyInfo? _protectedCursorProperty;

    private static object? _arrowInputCursor;

    private static bool _arrowLookupFailed;

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

    private delegate bool EnumChildProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumChildProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct CWPRETSTRUCT
    {
        public nint lResult;
        public nint lParam;
        public nint wParam;
        public uint message;
        public nint hwnd;
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, char[] lpClassName, int nMaxCount);

    public static void SetImmersiveDarkMode(Window window, bool dark)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }

    /// <summary>
    /// DWM frame decoration styles the app drives explicitly. The default style
    /// is the normal rounded window, <see cref="Snapped"/> removes the 1 px
    /// border while the window is flush against a monitor edge, and
    /// <see cref="Fullscreen"/> forces the borderless, square-cornered surface
    /// so a fullscreen presenter never shows an outline ring at high DPI.
    /// </summary>
    public enum WindowFrameStyle
    {
        Default,
        Snapped,
        Fullscreen,
    }

    /// <summary>
    /// Applies the DWM frame decoration for <paramref name="style"/>.
    /// <see cref="WindowFrameStyle.Default"/> restores the system border and
    /// rounded corners; the other styles suppress both (ignored on Windows 10,
    /// where the calls are a no-op). Cosmetic only: never throws.
    /// </summary>
    public static void SetWindowFrame(Window window, WindowFrameStyle style)
    {
        try
        {
            nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            bool flush = style != WindowFrameStyle.Default;
            int corner = flush ? DWMWCP_DONOTROUND : DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            int border = flush ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Cosmetic only; never let it break dragging or the presenter switch.
        }
    }

    /// <summary>
    /// Removes the DWM frame decoration while the window is snapped flush
    /// against a monitor edge: the 1 px border (which shows up as a light line
    /// between the window and the edge) is suppressed and the rounded corners
    /// are disabled, so the client area really touches the screen edge. Both
    /// attributes are ignored on Windows 10, where the call is a no-op.
    /// </summary>
    public static void SetSnappedFrame(Window window, bool snapped) =>
        SetWindowFrame(window, snapped ? WindowFrameStyle.Snapped : WindowFrameStyle.Default);

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
    private static extern bool GetCursorInfo(ref CURSORINFO info);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private const int IDC_ARROW = 32512;

    private static readonly bool CursorDiagEnabled =
        Environment.GetEnvironmentVariable("WIINA_CURSOR_DIAG") == "1";

    /// <summary>Env-gated cursor diagnostics (WIINA_CURSOR_DIAG=1) for the
    /// WM_SETCURSOR path, which is otherwise invisible from a release build.</summary>
    private static void Diag(string message)
    {
        if (!CursorDiagEnabled)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "wiina-cursor-diag.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect playback.
        }
    }

    private const int CursorShowingFlag = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public POINT ptScreenPos;    }

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
        _cursorRootHwnd = hwnd;
    }

    public static void Detach(Window window)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        if (Hooks.Remove(hwnd, out Hook? hook))
        {
            hook.Detach();
        }

        // A detached window must never leave the desktop pointer hidden.
        if (CursorHidden)
        {
            SetCursorVisible(true);
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
    /// True while playback has hidden the pointer. WinUI hands client-area
    /// cursor ownership to its input thread, so hiding is done by a message hook
    /// on that thread (see <see cref="CursorHookCallback"/>); the ShowCursor
    /// counter is kept balanced as a belt-and-braces fallback for non-client
    /// areas and for the moment before the hook installs.
    /// </summary>
    public static bool CursorHidden => _cursorHidden;

    private static volatile bool _cursorHidden;

    /// <summary>
    /// Hide or restore the pointer. Hiding is idempotent; restoring always
    /// balances the display counter back above zero and unhooks the input
    /// thread so a closed window cannot leave the desktop pointer hidden.
    /// </summary>
    public static void SetCursorVisible(bool visible)
    {
        try
        {
            if (visible)
            {
                if (CursorHidden)
                {
                    while (ShowCursor(true) < 0)
                    {
                    }

                    _cursorHidden = false;
                }

                UninstallCursorHook();
                SetCursor(LoadCursor(0, IDC_ARROW));
            }
            else
            {
                if (!CursorHidden)
                {
                    while (ShowCursor(false) >= 0)
                    {
                    }

                    _cursorHidden = true;
                }

                // Install the input-thread hook; the UI-thread SetCursor is only
                // a stopgap until the next message arrives.
                HookCursorChildren();
                SetCursor(GetBlankCursor());
            }
        }
        catch
        {
            _cursorHidden = false;
            // Cursor feedback must never break playback.
        }
    }

    /// <summary>
    /// Re-applies the hidden pointer while the chrome is faded out. The hook on
    /// the input thread does the real work; this keeps the install alive if the
    /// island was recreated and covers the gap before the next message arrives.
    /// </summary>
    public static void ReassertHiddenCursor()
    {
        if (!CursorHidden)
        {
            return;
        }

        try
        {
            HookCursorChildren();
            SetCursor(GetBlankCursor());
        }
        catch
        {
            // Cursor feedback must never break playback.
        }
    }

    /// <summary>
    /// Sets or clears WinUI's protected cursor on one element. Note: with the
    /// current WinAppSDK, <c>ProtectedCursor = null</c> falls back to the default
    /// arrow — there is no "no cursor" value in <c>InputSystemCursorShape</c>,
    /// and <c>InputCursor.CreateFromVisual</c> is not available yet — so this
    /// call is inert for hiding today. It is kept as the single transition hook
    /// for a future SDK that can express an invisible cursor, and as a fallback
    /// restore path. The property is protected, so it is reached by reflection
    /// and every failure is swallowed — a cursor problem must never break
    /// playback.
    /// </summary>
    public static void SetProtectedCursor(UIElement? element, bool hidden)
    {
        if (element is null)
        {
            return;
        }

        try
        {
            if (_protectedCursorSetter is null && !_protectedCursorLookupFailed)
            {
                var property = typeof(UIElement).GetProperty(
                    "ProtectedCursor",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _protectedCursorSetter = property?.GetSetMethod(nonPublic: true);
                _protectedCursorLookupFailed = _protectedCursorSetter is null;
            }

            if (_protectedCursorSetter is null)
            {
                Diag("protectedcursor: setter unavailable");
                return;
            }

            object? value = hidden ? null : GetArrowInputCursor();
            _protectedCursorSetter.Invoke(element, new[] { value });
            Diag(
                $"protectedcursor hidden={hidden} element={element.GetType().Name} " +
                $"value={(value is null ? "null" : value.GetType().Name)}");
        }
        catch (Exception exception)
        {
            Diag($"protectedcursor failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>The system arrow as a WinUI <c>InputCursor</c>, created once.</summary>
    private static object? GetArrowInputCursor()
    {
        if (_arrowInputCursor is not null || _arrowLookupFailed)
        {
            return _arrowInputCursor;
        }

        try
        {
            Type? cursorType = FindType("Microsoft.UI.Input.InputSystemCursor");
            Type? shapeType = FindType("Microsoft.UI.Input.InputSystemCursorShape");
            MethodInfo? create = cursorType?.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static);
            if (cursorType is null || shapeType is null || create is null)
            {
                _arrowLookupFailed = true;
                return null;
            }

            object arrow = Enum.Parse(shapeType, "Arrow");
            _arrowInputCursor = create.Invoke(null, new[] { arrow });
            _arrowLookupFailed = _arrowInputCursor is null;
        }
        catch
        {
            _arrowLookupFailed = true;
        }

        return _arrowInputCursor;
    }

    private static Type? FindType(string fullName)
    {
        Type? type = typeof(UIElement).Assembly.GetType(fullName);
        if (type is not null)
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(fullName);
            }
            catch
            {
                type = null;
            }

            if (type is not null)
            {
                return type;
            }
        }

        try
        {
            return Type.GetType(
                fullName + ", Microsoft.InteractiveExperiences.Projection",
                throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static MethodInfo? _protectedCursorSetter;

    private static bool _protectedCursorLookupFailed;

    /// <summary>
    /// Installs a message hook on the WinUI input thread that owns the content
    /// site bridge window. That thread — not the XAML UI thread — owns the
    /// desktop cursor while the pointer is over the video surface, so a
    /// SetCursor call made from the UI thread is silently ineffective. A
    /// WH_CALLWNDPROCRET hook runs after the window procedure on the owning
    /// thread, so clearing the cursor there sticks until the next message.
    /// </summary>
    private static void HookCursorChildren()
    {
        nint root = _cursorRootHwnd;
        if (root == 0 || !IsWindow(root))
        {
            return;
        }

        if (CursorHookedChildren.Count == 0 || _cursorHook == 0)
        {
            HookAllCursorHosts(root);
        }
    }

    /// <summary>
    /// Walks the WinUI child windows below the player window once: same-thread
    /// hosts are subclassed directly, hosts owned by the input thread get a
    /// message hook instead.
    /// </summary>
    private static void HookAllCursorHosts(nint root)
    {
        uint self = GetCurrentThreadId();
        uint foreignThread = 0;
        nint foreignWindow = 0;

        _cursorChildProc ??= CursorChildProc;
        CursorHookedChildren.RemoveWhere(hwnd => !IsWindow(hwnd));

        EnumChildWindows(
            root,
            (hwnd, unused) =>
            {
                string className = ClassNameOf(hwnd);
                bool cursorHost =
                    className.Contains("DesktopChildSiteBridge", StringComparison.Ordinal) ||
                    className.Contains("InputSiteWindowClass", StringComparison.Ordinal) ||
                    className.Contains("InputNonClientPointerSource", StringComparison.Ordinal);
                if (!cursorHost)
                {
                    return true;
                }

                uint thread = GetWindowThreadProcessId(hwnd, out _);
                if (thread == self)
                {
                    if (CursorHookedChildren.Add(hwnd))
                    {
                        SetWindowSubclass(hwnd, _cursorChildProc, 0xB1, 0);
                    }

                    return true;
                }

                if (foreignThread == 0)
                {
                    foreignThread = thread;
                    foreignWindow = hwnd;
                }

                return true;
            },
            0);

        if (foreignThread == 0)
        {
            // WinUI 3 may run its input stack on the XAML thread itself: the
            // platform sets the arrow while processing pointer messages and never
            // dispatches WM_SETCURSOR to the island windows, so the only place
            // left to win is right after each message on this very thread.
            foreignThread = self;
            Diag("hook: no foreign input thread, hooking the XAML thread");
        }

        if (_cursorHook != 0 && _cursorHookThread == foreignThread)
        {
            return;
        }

        UninstallCursorHook();

        _cursorHookProc ??= CursorHookCallback;
        _cursorHook = SetWindowsHookEx(WH_CALLWNDPROCRET, _cursorHookProc, 0, foreignThread);
        int error = Marshal.GetLastWin32Error();
        _cursorHookThread = _cursorHook != 0 ? foreignThread : 0;
        _cursorTargetThread = foreignThread;
        Diag(
            $"hook: thread={foreignThread} self={self} installed={_cursorHook != 0} error={error} " +
            $"subclassed={CursorHookedChildren.Count}");
    }

    private static nint CursorChildProc(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData)
    {
        if (CursorHidden)
        {
            if (msg == WM_SETCURSOR)
            {
                _cursorChildHits++;
                SetCursor(0);
                return 1;
            }

            if (msg == WM_MOUSEMOVE)
            {
                SetCursor(0);
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private static void UninstallCursorHook()
    {
        if (_cursorHook == 0)
        {
            return;
        }

        try
        {
            UnhookWindowsHookEx(_cursorHook);
        }
        catch
        {
            // Cursor feedback must never break playback.
        }

        _cursorHook = 0;
        _cursorHookThread = 0;
        _cursorTargetThread = 0;
    }

    private static nint CursorHookCallback(int nCode, nint wParam, nint lParam)
    {
        try
        {
            if (nCode >= 0 && CursorHidden)
            {
                var message = Marshal.PtrToStructure<CWPRETSTRUCT>(lParam);
                if (message.message == WM_SETCURSOR ||
                    message.message == WM_MOUSEMOVE ||
                    message.message == WM_NCMOUSEMOVE ||
                    message.message == WM_POINTERUPDATE ||
                    message.message == WM_POINTERENTER)
                {
                    // Runs on the input thread, so this really changes the cursor
                    // the desktop shows over the SwapChainPanel.
                    SetCursor(0);
                    _cursorHookHits++;
                }
            }
        }
        catch
        {
            // A throwing hook would poison the input thread; swallow everything.
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private static string ClassNameOf(nint hwnd)
    {
        try
        {
            var buffer = new char[256];
            int length = GetClassName(hwnd, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static HookProc? _cursorHookProc;
    private static nint _cursorHook;
    private static uint _cursorHookThread;
    private static uint _cursorTargetThread;
    private static long _cursorHookHits;
    private const int WH_CALLWNDPROCRET = 12;

    /// <summary>One-line live cursor state for the env-gated diagnostics.</summary>
    public static void LogCursorState(string label)
    {
        if (!CursorDiagEnabled)
        {
            return;
        }

        try
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            GetCursorInfo(ref info);

            nint blank = GetBlankCursor();
            uint ourThread = GetCurrentThreadId();
            nint under = WindowFromPoint(new POINT { X = info.ptScreenPos.X, Y = info.ptScreenPos.Y });
            uint inputThread = under != 0 ? GetWindowThreadProcessId(under, out _) : 0;
            bool attached = inputThread != 0 && inputThread != ourThread &&
                            AttachThreadInput(ourThread, inputThread, true);
            nint previous = SetCursor(blank);
            if (attached)
            {
                AttachThreadInput(ourThread, inputThread, false);
            }

            var after = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            GetCursorInfo(ref after);

            Diag(
                $"{label} hidden={CursorHidden} before=0x{info.hCursor:X} under=0x{under:X} " +
                $"inputThread={inputThread} attached={attached} blank=0x{blank:X} prev=0x{previous:X} " +
                $"after=0x{after.hCursor:X} flagsAfter={after.flags} isBlankAfter={after.hCursor == blank} " +
                $"hook=0x{_cursorHook:X} hookThread={_cursorHookThread} hits={_cursorHookHits} " +
                $"subclassed={CursorHookedChildren.Count} childHits={_cursorChildHits}");
        }
        catch
        {
            Diag($"{label} state failed");
        }
    }

    /// <summary>
    /// Lazily created 1x1 fully transparent cursor. The AND mask keeps every
    /// screen pixel and the XOR mask clears nothing, so the cursor paints
    /// nothing at all while still being a valid handle.
    /// </summary>
    private static nint GetBlankCursor()
    {
        if (_blankCursor == 0)
        {
            _blankCursor = CreateCursor(
                GetModuleHandle(null),
                0,
                0,
                1,
                1,
                new byte[] { 0xFF },
                new byte[] { 0x00 });
        }

        return _blankCursor;
    }

    private static nint _blankCursor;

    [DllImport("user32.dll")]
    private static extern int ShowCursor(bool show);

    [DllImport("user32.dll")]
    private static extern nint CreateCursor(
        nint hInst,
        int xHotSpot,
        int yHotSpot,
        int nWidth,
        int nHeight,
        byte[] pvANDPlane,
        byte[] pvXORPlane);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

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
            // While playback hides the pointer, keep the window from restoring
            // the arrow: WinUI/DefWindowProc call SetCursor(IDC_ARROW) for every
            // WM_SETCURSOR, which is exactly why ShowCursor alone looked broken.
            // Returning TRUE means "handled", so the class cursor never returns.
            if (msg == WM_SETCURSOR)
            {
                Diag($"setcursor hwnd=0x{hWnd:X} target=0x{_hwnd:X} hidden={CursorHidden}");

                if (CursorHidden && hWnd == _hwnd)
                {
                    SetCursor(0);
                    return 1;
                }
            }

            // Some hosts stop sending WM_SETCURSOR while the pointer rests on a
            // child surface (the mpv swapchain panel); re-assert on every move.
            if (msg == WM_MOUSEMOVE && CursorHidden && hWnd == _hwnd)
            {
                SetCursor(GetBlankCursor());
            }

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
