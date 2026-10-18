using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Wiina.App.Interop;

/// <summary>
/// Small window subclass for things WinUI 3 does not expose:
/// WM_GETMINMAXINFO (minimum size) and WM_SIZING (aspect-ratio lock).
/// </summary>
internal static class WindowInterop
{
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_SIZING = 0x0214;
    private const int LeftEdge = 1;
    private const int RightEdge = 2;
    private const int TopEdge = 3;
    private const int TopLeft = 4;
    private const int TopRight = 5;
    private const int BottomEdge = 6;
    private const int BottomLeft = 7;
    private const int BottomRight = 8;

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

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

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
        Func<double> aspect)
    {
        nint hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
        if (Hooks.ContainsKey(hwnd))
        {
            return;
        }

        var hook = new Hook(hwnd, minWidthDip, minHeightDip, lockAspect, aspect);
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

    private sealed class Hook
    {
        private readonly nint _hwnd;
        private readonly int _minWidthDip;
        private readonly int _minHeightDip;
        private readonly Func<bool> _lockAspect;
        private readonly Func<double> _aspect;
        private readonly SubclassProc _proc;

        public Hook(
            nint hwnd,
            int minWidthDip,
            int minHeightDip,
            Func<bool> lockAspect,
            Func<double> aspect)
        {
            _hwnd = hwnd;
            _minWidthDip = minWidthDip;
            _minHeightDip = minHeightDip;
            _lockAspect = lockAspect;
            _aspect = aspect;
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
            if (msg == WM_GETMINMAXINFO)
            {
                MINMAXINFO info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                double scale = GetDpiForWindow(hWnd) / 96.0;
                info.ptMinTrackSize.X = (int)Math.Round(_minWidthDip * scale);
                info.ptMinTrackSize.Y = (int)Math.Round(_minHeightDip * scale);
                Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                return 0;
            }

            if (msg == WM_SIZING && _lockAspect())
            {
                double aspect = _aspect();
                if (aspect > 0.05)
                {
                    RECT rect = Marshal.PtrToStructure<RECT>(lParam);
                    int edge = (int)wParam;
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (edge is LeftEdge or RightEdge)
                    {
                        height = (int)Math.Round(width / aspect);
                    }
                    else if (edge is TopEdge or BottomEdge)
                    {
                        width = (int)Math.Round(height * aspect);
                    }
                    else
                    {
                        height = (int)Math.Round(width / aspect);
                    }

                    switch (edge)
                    {
                        case LeftEdge:
                            rect.Left = rect.Right - width;
                            break;
                        case RightEdge:
                            rect.Right = rect.Left + width;
                            break;
                        case TopEdge:
                            rect.Top = rect.Bottom - height;
                            break;
                        case BottomEdge:
                            rect.Bottom = rect.Top + height;
                            break;
                        case TopLeft:
                            rect.Left = rect.Right - width;
                            rect.Top = rect.Bottom - height;
                            break;
                        case TopRight:
                            rect.Right = rect.Left + width;
                            rect.Top = rect.Bottom - height;
                            break;
                        case BottomLeft:
                            rect.Left = rect.Right - width;
                            rect.Bottom = rect.Top + height;
                            break;
                        case BottomRight:
                            rect.Right = rect.Left + width;
                            rect.Bottom = rect.Top + height;
                            break;
                    }

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
