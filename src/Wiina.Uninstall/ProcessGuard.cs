using System.Diagnostics;

namespace Wiina.Uninstall;

/// <summary>
/// One running Wiina process found by <see cref="ProcessGuard"/>.
/// <paramref name="Blocks"/> is true when the process lives in the folder being
/// uninstalled (its files are locked, and killing it is the only way to finish),
/// or when it is a "Wiina" process whose image path could not be read and that
/// therefore cannot be proven to belong to another installation.
/// </summary>
internal sealed record RunningWiina(string Name, int ProcessId, string? Path, bool Blocks);

/// <summary>
/// Finds Wiina processes that would keep the uninstall from finishing (issue 9,
/// item 7). The uninstaller used to leave Wiina.Video/Wiina.Audio registry keys
/// and locked files behind when the player was still running; this guard runs
/// before anything is deleted, and only a process in the folder being removed
/// (plus an unreadable-path "Wiina") is treated as blocking. A portable copy
/// running from another folder is reported but never killed.
/// </summary>
internal static class ProcessGuard
{
    private static readonly string[] ProcessNames = ["Wiina", "Wiina.Uninstall", "Uninstall Wiina"];

    private const int KillWaitMilliseconds = 8000;

    public static List<RunningWiina> Find(string appDirectory, List<string> log)
    {
        List<RunningWiina> found = [];
        int self = Environment.ProcessId;
        string folder = Normalize(appDirectory);

        foreach (string name in ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (Process process in processes)
            {
                using (process)
                {
                    if (process.Id == self)
                    {
                        continue;
                    }

                    string? path = null;
                    try
                    {
                        string? image = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(image))
                        {
                            path = image;
                        }
                    }
                    catch
                    {
                        // Another user's process (or a protected one): the path is
                        // unknown, which matters for the blocking decision below.
                    }

                    bool inFolder = path is not null && IsInside(path, folder);
                    bool blocks = inFolder ||
                        (path is null && string.Equals(name, "Wiina", StringComparison.OrdinalIgnoreCase));

                    found.Add(new RunningWiina(name, process.Id, path, blocks));
                    log.Add(
                        $"process: {name} pid {process.Id} path {(path ?? "(unknown)")} " +
                        (blocks ? "blocks the uninstall" : "left alone"));
                }
            }
        }

        return found;
    }

    /// <summary>Kills the blocking processes and waits for them to exit.</summary>
    public static bool Terminate(string appDirectory, IEnumerable<RunningWiina> blocking, List<string> log)
    {
        bool allGone = true;
        List<RunningWiina> attempted = [];

        foreach (RunningWiina item in blocking)
        {
            attempted.Add(item);
            try
            {
                using Process process = Process.GetProcessById(item.ProcessId);
                process.Kill(entireProcessTree: true);
                if (process.WaitForExit(KillWaitMilliseconds))
                {
                    log.Add($"killed: {item.Name} pid {item.ProcessId}");
                }
                else
                {
                    log.Add($"kill: {item.Name} pid {item.ProcessId} did not exit within {KillWaitMilliseconds} ms");
                    allGone = false;
                }
            }
            catch (ArgumentException)
            {
                // Already exited between the scan and the kill: not a failure.
                log.Add($"killed: {item.Name} pid {item.ProcessId} had already exited");
            }
            catch (Exception ex)
            {
                log.Add($"kill: {item.Name} pid {item.ProcessId} failed ({ex.GetType().Name}: {ex.Message})");
                allGone = false;
            }
        }

        // A process can respawn (single-instance relaunch, watchdog): re-scan the
        // folder once so a failed kill is never reported as success.
        if (allGone)
        {
            foreach (RunningWiina leftover in Find(appDirectory, []))
            {
                if (leftover.Blocks && attempted.TrueForAll(item => item.ProcessId != leftover.ProcessId))
                {
                    log.Add($"kill: {leftover.Name} pid {leftover.ProcessId} is still running");
                    allGone = false;
                }
            }
        }

        return allGone;
    }

    private static bool IsInside(string path, string folder)
    {
        try
        {
            string full = Normalize(path);
            return full.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
        }
        catch
        {
            return path;
        }
    }
}
