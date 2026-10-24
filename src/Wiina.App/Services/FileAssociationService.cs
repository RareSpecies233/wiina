using System.Runtime.InteropServices;
using Microsoft.Win32;
using Wiina.Core.Files;

namespace Wiina.App.Services;

/// <summary>
/// Per-user file association registration for the unpackaged build (issue 11).
/// Nothing here needs administrator rights: everything lives under
/// <c>HKCU\Software\Classes</c>. Windows 10/11 deliberately require the user to
/// confirm the default handler in Settings, so registration only makes Wiina
/// an eligible handler and exposes it in Default Apps.
/// </summary>
public static class FileAssociationService
{
    private const int ShcneAssocChanged = 0x08000000;

    public static bool IsRegistered(string progId)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\{progId}\shell\open\command");
            return key?.GetValue(null) is string command &&
                command.Contains("Wiina", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsVideoRegistered => IsRegistered(FileAssociationCatalog.VideoProgId);

    public static bool IsAudioRegistered => IsRegistered(FileAssociationCatalog.AudioProgId);

    /// <summary>
    /// Registers the requested handler kinds. Passing both false simply
    /// rewrites nothing; call <see cref="Unregister"/> to remove them.
    /// </summary>
    public static void Register(bool video, bool audio)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the Wiina executable path.");

        foreach (FileAssociationCatalog.Entry entry in
            FileAssociationCatalog.BuildPlan(executable, video, audio))
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(entry.KeyPath)
                ?? throw new InvalidOperationException($"Cannot open HKCU\\{entry.KeyPath}.");
            if (entry.Binary)
            {
                key.SetValue(entry.ValueName!, Array.Empty<byte>(), RegistryValueKind.Binary);
            }
            else
            {
                key.SetValue(entry.ValueName ?? string.Empty, entry.Value, RegistryValueKind.String);
            }
        }

        NotifyShell();
    }

    /// <summary>Removes only the keys/values Wiina owns.</summary>
    public static void Unregister()
    {
        foreach (string progId in FileAssociationCatalog.OwnedProgIds)
        {
            TryDeleteTree($@"Software\Classes\{progId}");
        }

        foreach (string extension in
            FileAssociationCatalog.VideoExtensions.Concat(FileAssociationCatalog.AudioExtensions))
        {
            RemoveOpenWith(extension);
        }

        TryDeleteTree(@"Software\Wiina");

        try
        {
            using RegistryKey? registered = Registry.CurrentUser.OpenSubKey(
                @"Software\RegisteredApplications",
                writable: true);
            if (registered?.GetValue(FileAssociationCatalog.ApplicationName) is not null)
            {
                registered.DeleteValue(FileAssociationCatalog.ApplicationName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best effort: the value may be protected or already gone.
        }

        NotifyShell();
    }

    private static void RemoveOpenWith(string extension)
    {
        string openWithPath = $@"Software\Classes\.{extension}\OpenWithProgids";
        try
        {
            using (RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(openWithPath, writable: true))
            {
                if (openWith is null)
                {
                    return;
                }

                foreach (string progId in FileAssociationCatalog.OwnedProgIds)
                {
                    openWith.DeleteValue(progId, throwOnMissingValue: false);
                }
            }

            // Only delete the extension key when it holds nothing else: it may
            // carry a system default or another app's registration.
            using RegistryKey? extensionKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\.{extension}");
            if (extensionKey is null)
            {
                return;
            }

            bool empty = extensionKey.GetValueNames().Length == 0 &&
                extensionKey.GetSubKeyNames().Length == 1 &&
                string.Equals(
                    extensionKey.GetSubKeyNames()[0],
                    "OpenWithProgids",
                    StringComparison.OrdinalIgnoreCase);
            if (empty)
            {
                TryDeleteTree($@"Software\Classes\.{extension}");
            }
        }
        catch
        {
            // Best effort: a locked key must not block uninstall flows.
        }
    }

    private static void TryDeleteTree(string path)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Explorer not running / shell32 unavailable: harmless.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        int eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);
}
