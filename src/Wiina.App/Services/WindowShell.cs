using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Wiina.App.Interop;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Wiina.App.Services;

/// <summary>
/// Shared setup for Stage 7 secondary windows: size, Mica, extended titlebar,
/// caption button colours and picker HWND initialization.
/// </summary>
internal static class WindowShell
{
    public static void Prepare(
        Window window,
        int width,
        int height,
        bool resizable = true,
        bool extendTitleBar = false,
        FrameworkElement? titleBar = null,
        bool dark = true,
        int minWidth = 0,
        int minHeight = 0)
    {
        // AppWindow.Resize takes physical pixels; specs are in DIP. Scale by the
        // monitor DPI so 600×480 and 560×440 stay true at 125–200% scaling.
        double scale = GetDpiForWindowSafe(window) / 96.0;
        window.AppWindow.Resize(new SizeInt32(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale))));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = resizable;
            presenter.IsMaximizable = resizable;
            presenter.IsMinimizable = true;
        }

        TryUseMica(window);
        TrySetWindowIcon(window);

        if (extendTitleBar)
        {
            window.ExtendsContentIntoTitleBar = true;
            window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            if (titleBar is not null)
            {
                window.SetTitleBar(titleBar);
            }
        }

        ConfigureCaptionButtons(window, dark);

        if (minWidth > 0 && minHeight > 0)
        {
            WindowInterop.Attach(window, minWidth, minHeight, () => false, () => 1.0);
        }
    }

    public static void ConfigureCaptionButtons(Window window, bool dark)
    {
        AppWindowTitleBar bar = window.AppWindow.TitleBar;
        bar.BackgroundColor = Colors.Transparent;
        bar.InactiveBackgroundColor = Colors.Transparent;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? Colors.Gray : Colors.DimGray;
        bar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x1F, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0, 0, 0);
    }

    public static nint Handle(Window window) => WindowNative.GetWindowHandle(window);

    /// <summary>
    /// Restores a secondary window from minimize and raises it above the
    /// player. <see cref="Window.Activate"/> alone is a no-op when the window
    /// already exists but is behind another foreground window.
    /// </summary>
    public static void BringToForeground(Window window)
    {
        try
        {
            nint hwnd = Handle(window);
            ShowWindow(hwnd, SW_RESTORE);
            window.Activate();
            SetForegroundWindow(hwnd);
        }
        catch
        {
            // Window may be closing; activation is best effort.
        }
    }

    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    public static void InitializePicker(Window window, object picker) =>
        InitializeWithWindow.Initialize(picker, Handle(window));

    public static void Detach(Window window)
    {
        try
        {
            WindowInterop.Detach(window);
        }
        catch
        {
            // The window may already be gone.
        }
    }

    private static double GetDpiForWindowSafe(Window window)
    {
        try
        {
            uint dpi = GetDpiForWindow(Handle(window));
            return dpi == 0 ? 96 : dpi;
        }
        catch
        {
            return 96;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private static void TryUseMica(Window window)
    {
        try
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            try
            {
                window.SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
                // Windows 10 / VM fallback: the solid page background remains.
            }
        }
    }

    /// <summary>
    /// Uses the generated multi-size icon for secondary windows and the taskbar.
    /// The executable icon is embedded separately; this covers windows created
    /// after launch and keeps the titlebar/taskbar glyph consistent.
    /// </summary>
    private static void TrySetWindowIcon(Window window)
    {
        try
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "wiina.ico");
            if (File.Exists(icon))
            {
                window.AppWindow.SetIcon(icon);
            }
        }
        catch
        {
            // Non-fatal: the executable icon is still used by Explorer.
        }
    }
}
