using Microsoft.Win32;
using Wiina.Core.Files;

namespace Wiina.Uninstall;

/// <summary>
/// Removes only the per-user registry state that Wiina owns. This mirrors
/// <c>Wiina.App.Services.FileAssociationService.Unregister</c>; the catalog of
/// progids/extensions is compiled in from Wiina.Core (FileAssociationCatalog)
/// so the uninstaller cannot drift from the app.
/// </summary>
internal static class RegistryCleanup
{
    /// <summary>True when any Wiina association or capabilities key exists.</summary>
    public static bool HasRegistration()
    {
        try
        {
            foreach (string progId in FileAssociationCatalog.OwnedProgIds)
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{progId}\shell\open\command");
                if (key?.GetValue(null) is string command &&
                    command.Contains(FileAssociationCatalog.ApplicationName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            using RegistryKey? capabilities = Registry.CurrentUser.OpenSubKey(
                FileAssociationCatalog.CapabilitiesPath);
            if (capabilities is not null)
            {
                return true;
            }

            using RegistryKey? registered = Registry.CurrentUser.OpenSubKey(
                @"Software\RegisteredApplications");
            return registered?.GetValue(FileAssociationCatalog.ApplicationName) is not null;
        }
        catch
        {
            // A locked or redirected registry hive means we cannot prove the
            // association exists; treat it as absent and do not prompt.
            return false;
        }
    }

    /// <summary>Deletes every key/value Wiina registered; never touches other apps' data.</summary>
    public static int RemoveAll(List<string> log)
    {
        int removed = 0;

        foreach (string progId in FileAssociationCatalog.OwnedProgIds)
        {
            string path = $@"Software\Classes\{progId}";
            if (DeleteTree(path))
            {
                log.Add($"registry: removed HKCU\\{path}");
                removed++;
            }
        }

        int extensionsCleaned = 0;
        foreach (string extension in FileAssociationCatalog.VideoExtensions
            .Concat(FileAssociationCatalog.AudioExtensions))
        {
            if (RemoveOpenWith(extension))
            {
                extensionsCleaned++;
            }
        }

        if (extensionsCleaned > 0)
        {
            log.Add($"registry: removed Wiina OpenWithProgids values from {extensionsCleaned} extensions");
            removed += extensionsCleaned;
        }

        if (DeleteTree(@"Software\Wiina"))
        {
            log.Add(@"registry: removed HKCU\Software\Wiina");
            removed++;
        }

        try
        {
            using RegistryKey? registered = Registry.CurrentUser.OpenSubKey(
                @"Software\RegisteredApplications",
                writable: true);
            if (registered?.GetValue(FileAssociationCatalog.ApplicationName) is not null)
            {
                registered.DeleteValue(FileAssociationCatalog.ApplicationName, throwOnMissingValue: false);
                log.Add(@"registry: removed the RegisteredApplications\Wiina value");
                removed++;
            }
        }
        catch (Exception ex)
        {
            log.Add($"registry: could not update RegisteredApplications ({ex.GetType().Name})");
        }

        NativeMethods.NotifyAssociationsChanged();
        return removed;
    }

    private static bool RemoveOpenWith(string extension)
    {
        bool changed = false;
        string openWithPath = $@"Software\Classes\.{extension}\OpenWithProgids";
        try
        {
            using (RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(openWithPath, writable: true))
            {
                if (openWith is null)
                {
                    return false;
                }

                foreach (string progId in FileAssociationCatalog.OwnedProgIds)
                {
                    if (openWith.GetValue(progId) is not null)
                    {
                        openWith.DeleteValue(progId, throwOnMissingValue: false);
                        changed = true;
                    }
                }
            }

            // Delete the extension key only when Wiina's marker was its only
            // content; another handler or a system default may live there.
            using RegistryKey? extensionKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\.{extension}");
            if (extensionKey is null)
            {
                return changed;
            }

            bool empty = extensionKey.GetValueNames().Length == 0 &&
                extensionKey.GetSubKeyNames().Length == 1 &&
                string.Equals(
                    extensionKey.GetSubKeyNames()[0],
                    "OpenWithProgids",
                    StringComparison.OrdinalIgnoreCase);
            if (empty)
            {
                DeleteTree($@"Software\Classes\.{extension}");
            }
        }
        catch
        {
            // A locked key must never block the rest of the uninstall.
        }

        return changed;
    }

    private static bool DeleteTree(string path)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null)
            {
                return false;
            }

            key.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
