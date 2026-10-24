using Microsoft.Win32;
using Wiina.Core.Files;

namespace Wiina.Uninstall;

/// <summary>
/// Removes only the registry state that Wiina owns (issues 8/9). This mirrors
/// <c>Wiina.App.Services.FileAssociationService.Unregister</c>: the per-user
/// half and the machine-wide half created by the app's elevated "associate as
/// administrator" action are both removed, because the uninstaller always runs
/// elevated (app.manifest) — <see cref="MachineLeftovers"/> stays as a safety
/// net for keys protected by policy or ACLs. The catalog of progids and
/// extensions is compiled in from Wiina.Core (FileAssociationCatalog) so the
/// uninstaller cannot drift from the app.
/// </summary>
internal static class RegistryCleanup
{
    private const string Hkcu = "HKCU";
    private const string Hklm = "HKLM";

    /// <summary>True when any Wiina association or capabilities key exists.</summary>
    public static bool HasRegistration() =>
        HasRegistration(Registry.CurrentUser) || HasRegistration(Registry.LocalMachine);

    private static bool HasRegistration(RegistryKey root)
    {
        try
        {
            if (RegisteredApplicationValue(root) is not null)
            {
                return true;
            }

            if (OpenWithValues(root) > 0)
            {
                return true;
            }

            using RegistryKey? capabilities = root.OpenSubKey(FileAssociationCatalog.CapabilitiesPath);
            return capabilities is not null;
        }
        catch
        {
            // A locked or redirected hive means we cannot prove the association
            // exists; treat it as absent.
            return false;
        }
    }

    /// <summary>Deletes every key/value Wiina registered in HKCU and, when
    /// permitted, in HKLM; never touches another application's data.</summary>
    public static int RemoveAll(List<string> log)
    {
        int removed = 0;
        removed += RemoveFromHive(Registry.CurrentUser, Hkcu, log);
        removed += RemoveFromHive(Registry.LocalMachine, Hklm, log);

        NativeMethods.NotifyAssociationsChanged();
        return removed;
    }

    /// <summary>
    /// Removes Wiina's keys from the loaded hives of <em>other</em> people's
    /// profiles (issue 9, item 7). When UAC elevates a standard user by typing an
    /// administrator's credentials, the app's associations live in HKCU of the
    /// standard user, not of the elevated administrator; <paramref name="preferredSid"/>
    /// (the shell owner found by <see cref="ShellUser"/>) is tried first, then
    /// every other loaded hive that actually contains Wiina keys. Only Wiina's
    /// own progids/values are deleted, never another application's data.
    /// Unloaded profiles are skipped: those keys are removed the next time that
    /// user runs the uninstaller themselves.
    /// </summary>
    public static int RemoveOtherUsers(List<string> log, string? preferredSid)
    {
        int removed = 0;
        string currentSid = CurrentUserSid();

        List<string> candidates = [];
        try
        {
            foreach (string name in Registry.Users.GetSubKeyNames())
            {
                if (string.IsNullOrEmpty(name) ||
                    name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ".DEFAULT", StringComparison.OrdinalIgnoreCase) ||
                    name is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" ||
                    string.Equals(name, currentSid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(name);
            }
        }
        catch (Exception ex)
        {
            log.Add($"registry: cannot enumerate HKEY_USERS ({ex.GetType().Name}: {ex.Message})");
            return removed;
        }

        if (preferredSid is not null)
        {
            candidates = candidates
                .OrderByDescending(name => string.Equals(name, preferredSid, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (string sid in candidates)
        {
            try
            {
                using RegistryKey? user = Registry.Users.OpenSubKey(sid, writable: true);
                if (user is null || !HasRegistration(user))
                {
                    continue;
                }

                int count = RemoveFromHive(user, $@"{user.Name}", log);
                if (count > 0)
                {
                    log.Add($"registry: removed {count} item(s) from another user's hive ({sid})");
                    removed += count;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                log.Add($"registry: another user's hive {sid} is protected; skipped");
            }
            catch (Exception ex)
            {
                log.Add($"registry: another user's hive {sid} skipped ({ex.GetType().Name}: {ex.Message})");
            }
        }

        return removed;
    }

    private static string CurrentUserSid()
    {
        string name = Registry.CurrentUser.Name ?? string.Empty;
        int separator = name.LastIndexOf('\\');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    /// <summary>
    /// Counts machine-wide items still present after <see cref="RemoveAll"/>.
    /// Non-zero means an elevated uninstall is required to finish the job.
    /// </summary>
    public static int MachineLeftovers()
    {
        try
        {
            int leftovers = 0;
            foreach (string progId in FileAssociationCatalog.OwnedProgIds)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{progId}");
                if (key is not null)
                {
                    leftovers++;
                }
            }

            using (RegistryKey? capabilities = Registry.LocalMachine.OpenSubKey(
                FileAssociationCatalog.CapabilitiesPath))
            {
                if (capabilities is not null)
                {
                    leftovers++;
                }
            }

            if (RegisteredApplicationValue(Registry.LocalMachine) is not null)
            {
                leftovers++;
            }

            return leftovers + OpenWithValues(Registry.LocalMachine);
        }
        catch
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------ removal

    private static int RemoveFromHive(RegistryKey root, string hive, List<string> log)
    {
        int removed = 0;

        foreach (string progId in FileAssociationCatalog.OwnedProgIds)
        {
            if (DeleteTree(root, $@"Software\Classes\{progId}"))
            {
                log.Add($@"registry: removed {hive}\Software\Classes\{progId}");
                removed++;
            }
        }

        int cleaned = 0;
        foreach (string extension in FileAssociationCatalog.VideoExtensions
            .Concat(FileAssociationCatalog.AudioExtensions))
        {
            if (RemoveExtension(root, hive, extension, log))
            {
                cleaned++;
            }
        }

        if (cleaned > 0)
        {
            log.Add($@"registry: removed Wiina values from {hive}\Software\Classes\.<ext> ({cleaned} extension(s))");
            removed += cleaned;
        }

        if (DeleteTree(root, @"Software\Wiina"))
        {
            log.Add($@"registry: removed {hive}\Software\Wiina (capabilities)");
            removed++;
        }

        if (DeleteRegisteredApplication(root))
        {
            log.Add($@"registry: removed {hive}\Software\RegisteredApplications\Wiina");
            removed++;
        }

        return removed;
    }

    /// <summary>
    /// Clears one extension's Wiina markers. A class default pointing at a
    /// Wiina progid (written by the elevated registration) is cleared too, and
    /// keys left completely empty are deleted. Anything another handler wrote
    /// is left untouched.
    /// </summary>
    private static bool RemoveExtension(RegistryKey root, string hive, string extension, List<string> log)
    {
        bool changed = false;
        string extensionPath = $@"Software\Classes\.{extension}";

        try
        {
            using (RegistryKey? openWith = root.OpenSubKey(
                $@"{extensionPath}\OpenWithProgids",
                writable: true))
            {
                if (openWith is not null)
                {
                    foreach (string progId in FileAssociationCatalog.OwnedProgIds)
                    {
                        if (openWith.GetValue(progId) is not null)
                        {
                            openWith.DeleteValue(progId, throwOnMissingValue: false);
                            changed = true;
                        }
                    }

                    // An OpenWithProgids key that now holds nothing is dead
                    // weight; other apps' values keep it alive.
                    if (openWith.ValueCount == 0 && openWith.SubKeyCount == 0)
                    {
                        openWith.Dispose();
                        DeleteTree(root, $@"{extensionPath}\OpenWithProgids");
                    }
                }
            }

            using (RegistryKey? extensionKey = root.OpenSubKey(extensionPath, writable: true))
            {
                if (extensionKey is null)
                {
                    return changed;
                }

                string? defaultProgId = extensionKey.GetValue(null) as string;
                if (defaultProgId is not null &&
                    FileAssociationCatalog.OwnedProgIds.Contains(
                        defaultProgId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    extensionKey.DeleteValue(string.Empty, throwOnMissingValue: false);
                    changed = true;
                }

                bool empty = extensionKey.ValueCount == 0 && extensionKey.SubKeyCount == 0;
                extensionKey.Dispose();
                if (empty)
                {
                    DeleteTree(root, extensionPath);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Not elevated: HKLM cleanup is completed by an elevated uninstall
            // and reported by MachineLeftovers().
        }
        catch
        {
            // A locked key must never block the rest of the uninstall.
        }

        return changed;
    }

    private static bool DeleteRegisteredApplication(RegistryKey root)
    {
        try
        {
            using RegistryKey? registered = root.OpenSubKey(
                @"Software\RegisteredApplications",
                writable: true);
            if (registered?.GetValue(FileAssociationCatalog.ApplicationName) is null)
            {
                return false;
            }

            registered.DeleteValue(FileAssociationCatalog.ApplicationName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteTree(RegistryKey root, string path)
    {
        try
        {
            using (RegistryKey? key = root.OpenSubKey(path))
            {
                if (key is null)
                {
                    return false;
                }
            }

            root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ probes

    private static string? RegisteredApplicationValue(RegistryKey root)
    {
        try
        {
            using RegistryKey? registered = root.OpenSubKey(@"Software\RegisteredApplications");
            return registered?.GetValue(FileAssociationCatalog.ApplicationName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static int OpenWithValues(RegistryKey root)
    {
        int count = 0;
        try
        {
            foreach (string extension in FileAssociationCatalog.VideoExtensions
                .Concat(FileAssociationCatalog.AudioExtensions))
            {
                using RegistryKey? openWith = root.OpenSubKey(
                    $@"Software\Classes\.{extension}\OpenWithProgids");
                if (openWith is null)
                {
                    continue;
                }

                foreach (string progId in FileAssociationCatalog.OwnedProgIds)
                {
                    if (openWith.GetValue(progId) is not null)
                    {
                        count++;
                    }
                }
            }
        }
        catch
        {
            // Best effort: an unreadable hive counts as clean here.
        }

        return count;
    }
}
