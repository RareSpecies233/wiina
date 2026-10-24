using System.Runtime.InteropServices;

namespace Wiina.Uninstall;

/// <summary>
/// The uninstaller's entire UI surface: two user32 message boxes plus the
/// shell-association broadcast. Keeping this to raw P/Invoke avoids WinForms,
/// WPF and the Windows App SDK in a process whose only job is deleting files.
/// </summary>
internal static class NativeMethods
{
    // MessageBox uType flags (winuser.h).
    public const uint MbYesNo = 0x00000004;
    public const uint MbIconError = 0x00000010;
    public const uint MbIconQuestion = 0x00000020;
    public const uint MbIconWarning = 0x00000030;
    public const uint MbIconInformation = 0x00000040;
    public const uint MbDefaultButton1 = 0x00000000;
    public const uint MbDefaultButton2 = 0x00000100;

    // MessageBox return values.
    public const int IdYes = 6;
    public const int IdNo = 7;

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static bool Confirm(string text, string caption, bool defaultYes)
    {
        uint type = MbYesNo | MbIconQuestion |
            (defaultYes ? MbDefaultButton1 : MbDefaultButton2);
        return Show(text, caption, type | MbIconWarning) == IdYes;
    }

    public static bool Ask(string text, string caption, bool defaultYes)
    {
        uint type = MbYesNo | MbIconQuestion |
            (defaultYes ? MbDefaultButton1 : MbDefaultButton2);
        return Show(text, caption, type) == IdYes;
    }

    public static void Warn(string text, string caption)
        => Show(text, caption, MbIconWarning);

    public static void Error(string text, string caption)
        => Show(text, caption, MbIconError);

    public static void Info(string text, string caption)
        => Show(text, caption, MbIconInformation);

    /// <summary>Tells Explorer that file associations changed (best effort).</summary>
    public static void NotifyAssociationsChanged()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // shell32 missing is impossible on Windows, but never let cleanup fail on it.
        }
    }

    private static int Show(string text, string caption, uint type) =>
        MessageBoxW(IntPtr.Zero, text, caption, type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
