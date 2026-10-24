using System.Diagnostics;

namespace Wiina.Uninstall;

/// <summary>
/// The user who started the uninstall, when UAC elevated us under a *different*
/// account (standard user typing an administrator's credentials). In that case
/// <c>%LOCALAPPDATA%\Wiina</c> and HKCU belong to the elevated administrator, so
/// the settings folder and per-user registry of the shell owner (explorer.exe in
/// the physical console session) are used instead.
/// </summary>
internal sealed record ShellUserInfo(string LocalAppData, string Sid);

internal static class ShellUser
{
    // KNOWNFOLDERID_LocalAppData — {F1B32785-6FBA-4FCF-9D55-7B8E7F157091}.
    private static readonly Guid LocalAppDataFolder = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    /// <summary>
    /// Returns null when the shell owner is the current user (the normal case:
    /// in-place elevation keeps the same account), or when the shell cannot be
    /// inspected — the caller then falls back to its own profile.
    /// </summary>
    public static ShellUserInfo? FindInvokingUser(List<string> log)
    {
        try
        {
            string? currentSid = NativeMethods.GetCurrentUserSid();

            // UAC consent runs on the desktop of the process's own session, so
            // the invoking user's shell is an explorer.exe in this session. (The
            // WTS "active console" id is not reliable on every configuration —
            // it can disagree with the session the process actually runs in.)
            int session;
            try
            {
                using Process self = Process.GetCurrentProcess();
                session = self.SessionId;
            }
            catch
            {
                session = -1;
            }

            List<Process> candidates = [];
            try
            {
                candidates.AddRange(Process.GetProcessesByName("explorer")
                    .Where(process => session < 0 || process.SessionId == session));
            }
            catch (Exception ex)
            {
                log.Add($"shell: cannot enumerate explorer.exe ({ex.GetType().Name}: {ex.Message})");
                return null;
            }

            if (candidates.Count == 0)
            {
                log.Add($"shell: no explorer.exe in session {session}; using the current profile");
                return null;
            }

            foreach (Process explorer in candidates)
            {
                using (explorer)
                {
                    if (!NativeMethods.TryOpenProcessToken(explorer.Id, out nint processHandle, out nint token))
                    {
                        log.Add($"shell: cannot open the token of explorer.exe pid {explorer.Id}");
                        continue;
                    }

                    try
                    {
                        string? shellSid = NativeMethods.GetTokenUserSid(token);
                        if (shellSid is null)
                        {
                            continue;
                        }

                        if (currentSid is not null &&
                            string.Equals(shellSid, currentSid, StringComparison.OrdinalIgnoreCase))
                        {
                            log.Add($"shell: explorer.exe pid {explorer.Id} runs as the current user; no cross-account cleanup needed");
                            return null;
                        }

                        string? localAppData = NativeMethods.GetKnownFolderPath(token, LocalAppDataFolder);
                        if (string.IsNullOrEmpty(localAppData))
                        {
                            log.Add($"shell: cannot resolve the shell user's %LOCALAPPDATA% (sid {shellSid})");
                            continue;
                        }

                        log.Add(
                            $"shell: elevated as {currentSid ?? "(unknown)"} but the interactive user is " +
                            $"{shellSid}; using {localAppData} and HKEY_USERS\\{shellSid}");
                        return new ShellUserInfo(localAppData, shellSid);
                    }
                    finally
                    {
                        NativeMethods.CloseToken(token, processHandle);
                    }
                }
            }

            log.Add("shell: no matching explorer.exe account found; using the current profile");
        }
        catch (Exception ex)
        {
            log.Add($"shell: lookup failed ({ex.GetType().Name}: {ex.Message})");
        }

        return null;
    }
}
