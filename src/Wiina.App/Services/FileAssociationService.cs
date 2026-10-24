using System.Runtime.InteropServices;
using Microsoft.Win32;
using Wiina.Core.Files;

namespace Wiina.App.Services;

/// <summary>
/// File association registration for the unpackaged build (issue 11).
/// The normal path needs no administrator rights: everything lives under
/// <c>HKCU\Software\Classes</c> and Windows 10/11 asks the user to confirm the
/// default handler. <see cref="RegisterElevated"/> additionally mirrors the
/// registration into <c>HKLM\Software\Classes</c> and clears per-extension
/// <c>UserChoice</c> when the user explicitly asks for an elevated registration.
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

    /// <summary>
    /// Elevated registration used by the "associate as administrator" action.
    /// Mirrors the per-user registration into <c>HKLM\Software\Classes</c>,
    /// points each supported extension's class default at the matching ProgID,
    /// and removes the per-extension <c>UserChoice</c> override so Explorer falls
    /// back to that class default. Must run elevated: without administrator
    /// rights the <c>HKLM</c> writes throw <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static void RegisterElevated(bool video, bool audio)
    {
        if (!video && !audio)
        {
            throw new ArgumentException("At least one handler kind must be requested.", nameof(video));
        }

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the Wiina executable path.");

        // Per-user half first: capabilities, ProgIDs, and OpenWith entries.
        Register(video, audio);

        using (RegistryKey machineClasses = Registry.LocalMachine.CreateSubKey(@"Software\Classes")
            ?? throw new InvalidOperationException(@"Cannot open HKLM\Software\Classes."))
        {
            if (video)
            {
                WriteMachineProgId(
                    machineClasses,
                    FileAssociationCatalog.VideoProgId,
                    "Wiina 视频",
                    executable);
            }

            if (audio)
            {
                WriteMachineProgId(
                    machineClasses,
                    FileAssociationCatalog.AudioProgId,
                    "Wiina 音频",
                    executable);
            }

            foreach ((string extension, string progId) in SelectedExtensions(video, audio))
            {
                using RegistryKey openWith = machineClasses.CreateSubKey(
                    $@".{extension}\OpenWithProgids")
                    ?? throw new InvalidOperationException(
                        $@"Cannot open HKLM\Software\Classes\.{extension}\OpenWithProgids.");
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.Binary);
            }
        }

        using (RegistryKey capabilities = Registry.LocalMachine.CreateSubKey(
            FileAssociationCatalog.CapabilitiesPath)
            ?? throw new InvalidOperationException(
                $@"Cannot open HKLM\{FileAssociationCatalog.CapabilitiesPath}."))
        {
            capabilities.SetValue(
                "ApplicationName",
                FileAssociationCatalog.ApplicationName,
                RegistryValueKind.String);
            capabilities.SetValue(
                "ApplicationDescription",
                "Wiina 媒体播放器",
                RegistryValueKind.String);
            capabilities.SetValue(
                "ApplicationIcon",
                FileAssociationCatalog.IconCommand(executable),
                RegistryValueKind.String);
            using RegistryKey fileAssociations = capabilities.CreateSubKey("FileAssociations")
                ?? throw new InvalidOperationException(
                    $@"Cannot open HKLM\{FileAssociationCatalog.CapabilitiesPath}\FileAssociations.");
            foreach ((string extension, string progId) in SelectedExtensions(video, audio))
            {
                fileAssociations.SetValue($".{extension}", progId, RegistryValueKind.String);
            }
        }

        using (RegistryKey registered = Registry.LocalMachine.CreateSubKey(
            @"Software\RegisteredApplications")
            ?? throw new InvalidOperationException(
                @"Cannot open HKLM\Software\RegisteredApplications."))
        {
            registered.SetValue(
                FileAssociationCatalog.ApplicationName,
                FileAssociationCatalog.CapabilitiesPath,
                RegistryValueKind.String);
        }

        foreach ((string extension, string progId) in SelectedExtensions(video, audio))
        {
            using (RegistryKey extensionKey = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\.{extension}")
                ?? throw new InvalidOperationException(
                    $@"Cannot open HKCU\Software\Classes\.{extension}."))
            {
                extensionKey.SetValue(null, progId, RegistryValueKind.String);
            }

            try
            {
                // Windows re-creates UserChoice when the user picks a default
                // later; clearing it now makes the class default effective.
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.{extension}\UserChoice",
                    throwOnMissingSubKey: false);
            }
            catch
            {
                // Protected UserChoice keys must not abort the whole registration.
            }
        }

        NotifyShell();
    }

    private static IEnumerable<(string Extension, string ProgId)> SelectedExtensions(
        bool video,
        bool audio)
    {
        if (video)
        {
            foreach (string extension in FileAssociationCatalog.VideoExtensions)
            {
                yield return (extension, FileAssociationCatalog.VideoProgId);
            }
        }

        if (audio)
        {
            foreach (string extension in FileAssociationCatalog.AudioExtensions)
            {
                yield return (extension, FileAssociationCatalog.AudioProgId);
            }
        }
    }

    private static void WriteMachineProgId(
        RegistryKey machineClasses,
        string progId,
        string friendlyName,
        string executable)
    {
        using RegistryKey key = machineClasses.CreateSubKey(progId)
            ?? throw new InvalidOperationException($@"Cannot open HKLM\Software\Classes\{progId}.");
        key.SetValue(null, friendlyName, RegistryValueKind.String);
        key.SetValue("FriendlyTypeName", friendlyName, RegistryValueKind.String);

        using (RegistryKey icon = key.CreateSubKey("DefaultIcon")
            ?? throw new InvalidOperationException($@"Cannot open HKLM\Software\Classes\{progId}\DefaultIcon."))
        {
            icon.SetValue(null, FileAssociationCatalog.IconCommand(executable), RegistryValueKind.String);
        }

        using (RegistryKey command = key.CreateSubKey(@"shell\open\command")
            ?? throw new InvalidOperationException($@"Cannot open HKLM\Software\Classes\{progId}\shell\open\command."))
        {
            command.SetValue(null, FileAssociationCatalog.OpenCommand(executable), RegistryValueKind.String);
        }

        using RegistryKey application = key.CreateSubKey("Application")
            ?? throw new InvalidOperationException($@"Cannot open HKLM\Software\Classes\{progId}\Application.");
        application.SetValue(
            "ApplicationName",
            FileAssociationCatalog.ApplicationName,
            RegistryValueKind.String);
    }
    /// <summary>Removes only the keys/values Wiina owns (HKCU, plus HKLM when reachable).</summary>
    public static void Unregister()
    {
        foreach (string progId in FileAssociationCatalog.OwnedProgIds)
        {
            TryDeleteTree($@"Software\Classes\{progId}");
            TryDeleteMachineTree($@"Software\Classes\{progId}");
        }

        foreach (string extension in
            FileAssociationCatalog.VideoExtensions.Concat(FileAssociationCatalog.AudioExtensions))
        {
            RemoveOpenWith(extension);
            RemoveMachineOpenWith(extension);
        }

        TryDeleteTree(@"Software\Wiina");
        TryDeleteMachineTree(@"Software\Wiina");
        RemoveRegisteredApplication(Registry.CurrentUser);
        RemoveRegisteredApplication(Registry.LocalMachine);

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

            // RegisterElevated writes the ProgID as the class default; clear it
            // before deciding whether the extension key itself can go.
            try
            {
                using RegistryKey? defaultKey = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\.{extension}",
                    writable: true);
                if (defaultKey?.GetValue(null) is string defaultProgId &&
                    FileAssociationCatalog.OwnedProgIds.Contains(
                        defaultProgId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    defaultKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                }
            }
            catch
            {
                // Best effort: a protected default must not block uninstall flows.
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

    private static void TryDeleteMachineTree(string path)
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Not elevated, or the key is gone/protected: best effort.
        }
    }

    private static void RemoveMachineOpenWith(string extension)
    {
        try
        {
            using RegistryKey? openWith = Registry.LocalMachine.OpenSubKey(
                $@"Software\Classes\.{extension}\OpenWithProgids",
                writable: true);
            if (openWith is null)
            {
                return;
            }

            foreach (string progId in FileAssociationCatalog.OwnedProgIds)
            {
                openWith.DeleteValue(progId, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Not elevated: the normal per-user unregister still completes.
        }
    }

    private static void RemoveRegisteredApplication(RegistryKey root)
    {
        try
        {
            using RegistryKey? registered = root.OpenSubKey(
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
