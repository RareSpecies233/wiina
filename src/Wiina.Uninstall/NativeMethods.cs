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

    // ------------------------------------------------- elevation and identities

    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;
    private const int TokenElevationClass = 20;

    /// <summary>Minimum rights to read a process image path / open its token.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>True when this process runs with an elevated administrator token.</summary>
    public static bool IsElevated()
    {
        nint token = OpenCurrentProcessToken();
        if (token == 0)
        {
            return false;
        }

        try
        {
            return IsTokenElevated(token);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public static nint OpenCurrentProcessToken()
    {
        try
        {
            return OpenProcessToken(GetCurrentProcess(), TokenQuery, out nint token) ? token : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Opens a query-only token; the caller closes both handles.</summary>
    public static bool TryOpenProcessToken(int processId, out nint processHandle, out nint token)
    {
        processHandle = 0;
        token = 0;
        try
        {
            processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (processHandle == 0)
            {
                return false;
            }

            if (!OpenProcessToken(processHandle, TokenQuery, out token))
            {
                CloseHandle(processHandle);
                processHandle = 0;
                return false;
            }

            return true;
        }
        catch
        {
            if (processHandle != 0)
            {
                CloseHandle(processHandle);
                processHandle = 0;
            }

            return false;
        }
    }

    public static void CloseToken(nint token, nint processHandle)
    {
        if (token != 0)
        {
            CloseHandle(token);
        }

        if (processHandle != 0)
        {
            CloseHandle(processHandle);
        }
    }

    private static bool IsTokenElevated(nint token)
    {
        try
        {
            const int size = sizeof(int);
            nint buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, TokenElevationClass, buffer, size, out _))
                {
                    return false;
                }

                return Marshal.ReadInt32(buffer) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>String SID of the token's user, or null when it cannot be read.</summary>
    public static string? GetTokenUserSid(nint token)
    {
        try
        {
            GetTokenInformation(token, TokenUserClass, 0, 0, out int needed);
            if (needed <= 0)
            {
                return null;
            }

            nint buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, TokenUserClass, buffer, needed, out _))
                {
                    return null;
                }

                // TOKEN_USER { SID_AND_ATTRIBUTES User; } — the SID pointer is first.
                nint sid = Marshal.ReadIntPtr(buffer);
                if (sid == 0)
                {
                    return null;
                }

                if (!ConvertSidToStringSid(sid, out nint text))
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(text);
                }
                finally
                {
                    LocalFree(text);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    public static string? GetCurrentUserSid()
    {
        nint token = OpenCurrentProcessToken();
        if (token == 0)
        {
            return null;
        }

        try
        {
            return GetTokenUserSid(token);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Known folder path for a user token (SHGetKnownFolderPath). Used to find
    /// the invoking user's %LOCALAPPDATA% when UAC elevated us under a different
    /// account (standard user + admin credentials).
    /// </summary>
    public static string? GetKnownFolderPath(nint token, Guid folderId)
    {
        nint path = 0;
        try
        {
            Guid id = folderId;
            if (SHGetKnownFolderPath(ref id, 0, token, out path) != 0 || path == 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(path);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (path != 0)
            {
                CoTaskMemFree(path);
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int infoClass, nint info, int length, out int returned);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertSidToStringSid(nint sid, out nint text);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, nint token, out nint path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint memory);
}
