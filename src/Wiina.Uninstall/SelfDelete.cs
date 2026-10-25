using System.Diagnostics;
using System.Text;

namespace Wiina.Uninstall;

/// <summary>
/// A running executable cannot delete itself, so the portable uninstaller
/// writes a tiny batch file to %TEMP% and starts it detached. The batch retries
/// <c>rmdir</c> for up to ~30 seconds, which covers the window where this
/// process (and the OS image lock on the .exe) still exists.
/// </summary>
internal static class SelfDelete
{
    private const string BatchMarker = "Wiina portable uninstaller";

    /// <summary>Reasons we refuse to recursively delete a directory.</summary>
    public static bool IsSafeTarget(string directory, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory) ||
            !Directory.Exists(directory))
        {
            reason = "the application folder was not found";
            return false;
        }

        string trimmed = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Never delete a drive root or a well-known user/system folder: the zip
        // can legitimately be extracted into Desktop\wiina-portable, but a
        // mis-extraction into Desktop itself must not wipe the user's files.
        string? root = Path.GetPathRoot(trimmed)?.TrimEnd('\\');
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
        {
            reason = "the application folder is a drive root";
            return false;
        }

        Environment.SpecialFolder[] userFolders =
        [
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonApplicationData,
        ];

        foreach (Environment.SpecialFolder folder in userFolders)
        {
            string known = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(known) &&
                string.Equals(trimmed, Path.GetFullPath(known).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                reason = $"the application folder is the {folder} folder";
                return false;
            }
        }

        Environment.SpecialFolder[] systemFolders =
        [
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
        ];

        foreach (Environment.SpecialFolder folder in systemFolders)
        {
            string special = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(special))
            {
                continue;
            }

            string normalized = Path.GetFullPath(special).TrimEnd('\\');
            if (string.Equals(trimmed, normalized, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase))
            {
                reason = "the application folder is inside a protected system folder";
                return false;
            }
        }

        // Only delete a folder that actually looks like the portable payload.
        bool payload = File.Exists(Path.Combine(trimmed, "Wiina.exe")) ||
            File.Exists(Path.Combine(trimmed, "libmpv-2.dll"));
        if (!payload)
        {
            reason = "the folder does not contain the portable Wiina payload";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Starts the detached deletion script. Returns false (and logs why) when
    /// the folder is unsafe to delete or the process cannot be started.
    /// </summary>
    public static bool TrySchedule(string directory, List<string> log, out string batchPath)
    {
        batchPath = string.Empty;

        if (!IsSafeTarget(directory, out string reason))
        {
            log.Add($"self-delete: refused ({reason})");
            return false;
        }

        try
        {
            batchPath = Path.Combine(
                Path.GetTempPath(),
                $"wiina-uninstall-{Environment.ProcessId}.cmd");

            // Quotes in a Windows path are impossible, so the path can be
            // embedded verbatim inside the batch's quoted variables.
            string script =
                "@echo off\r\n" +
                $"rem {BatchMarker}\r\n" +
                "setlocal\r\n" +
                $"set \"TARGET={directory}\"\r\n" +
                "for /l %%i in (1,1,30) do (\r\n" +
                "  if not exist \"%TARGET%\" goto done\r\n" +
                "  rmdir /s /q \"%TARGET%\" >nul 2>&1\r\n" +
                "  if not exist \"%TARGET%\" goto done\r\n" +
                "  ping -n 2 127.0.0.1 >nul\r\n" +
                ")\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n" +
                "exit /b 1\r\n" +
                ":done\r\n" +
                "del \"%~f0\" >nul 2>&1\r\n" +
                "exit /b 0\r\n";

            File.WriteAllText(batchPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            ProcessStartInfo startInfo = new()
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath(),
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(batchPath);

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                log.Add("self-delete: cmd.exe did not start");
                return false;
            }

            log.Add($"self-delete: scheduled via pid {process.Id} ({batchPath})");
            return true;
        }
        catch (Exception ex)
        {
            log.Add($"self-delete: failed to schedule ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }
}
