using System.Text;

namespace Wiina.Uninstall;

/// <summary>
/// Portable uninstaller (issues 8/9). The portable zip has no installer, so this
/// executable is the uninstall entry point shipped next to <c>Wiina.exe</c>:
///
/// 0. require administrator rights (app.manifest requests them, and the token is
///    checked again so an unmanifested `dotnet Wiina.Uninstall.dll` run fails
///    cleanly instead of half-uninstalling);
/// 1. confirm the uninstall (default No — it deletes files);
/// 2. if Wiina is still running from the folder being removed, ask whether to
///    force-close it (default No; silent runs close it automatically) and wait
///    for it to exit — otherwise its locked files and registry writes survive;
/// 3. always remove the file associations Wiina registered (HKCU, HKLM, plus the
///    loaded hives of another user when UAC elevated us across accounts);
/// 4. ask whether to keep the per-user settings (default Yes), matching the
///    installer's uninstall prompt; only an explicit No deletes
///    <c>%LOCALAPPDATA%\Wiina</c> (settings, history, plugins, mpv config, logs,
///    crash log) — never any user media;
/// 5. schedule deletion of the portable folder after this process exits.
///
/// Exit codes: 0 = done or cancelled, 1 = the folder or a process could not be
/// handled automatically (the user is told what remains), 2 = not elevated.
/// </summary>
internal static class Program
{
    private const string ProductName = "Wiina";

    /// <summary>
    /// Test/CI escape hatch: the published manifest makes Windows show UAC for
    /// every launch, so an automated test on a non-elevated desktop could not
    /// exercise the rest of the flow. This only skips the "you are not elevated"
    /// guard; it grants no rights, and HKLM writes still fail unelevated.
    /// </summary>
    private const string AllowUnelevatedVariable = "WIINA_UNINSTALL_ALLOW_UNELEVATED";

    private static int Main(string[] args)
    {
        UninstallOptions options = UninstallOptions.Parse(args);

        string? currentUser = NativeMethods.GetCurrentUserSid();
        List<string> log =
        [
            $"Wiina portable uninstaller, pid {Environment.ProcessId}",
            $"time: {DateTimeOffset.Now:O}",
            $"elevated: {NativeMethods.IsElevated()}",
            $"user: {currentUser ?? "(unknown)"}",
        ];

        // --------------------------------------------------------- elevation
        bool elevated = NativeMethods.IsElevated();
        if (!elevated && !AllowUnelevated())
        {
            log.Add("blocked: administrator rights are required");
            string path = WriteLog(log);
            if (!options.Silent)
            {
                NativeMethods.Error(
                    "Wiina 卸载程序需要管理员权限。\n\n" +
                    "请右键“Uninstall Wiina.exe”，选择“以管理员身份运行”，\n" +
                    "然后在 UAC 提示中选择“是”。\n\n" +
                    "This uninstaller must run as administrator.\n" +
                    "Right-click it, choose \"Run as administrator\" and accept the\n" +
                    "UAC prompt.\n\n" +
                    "日志 / Log:\n" + path,
                    ProductName);
            }

            return 2;
        }

        if (!elevated)
        {
            log.Add($"continuing unelevated: {AllowUnelevatedVariable}=1 (tests only)");
        }

        // ------------------------------------------------------- environment
        string appDirectory = (AppContext.BaseDirectory ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(appDirectory))
        {
            return Fail(log, options, "无法确定 Wiina 所在文件夹。\n\nCould not determine the Wiina application folder.");
        }

        // UAC can elevate under a different account (standard user + admin
        // credentials). In that case the settings live in the *invoking* user's
        // profile, not the elevated admin's, so the shell owner is consulted.
        ShellUserInfo? shellUser = ShellUser.FindInvokingUser(log);
        string localAppData = shellUser?.LocalAppData ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string configDirectory = Path.Combine(localAppData, ProductName);
        log.Add($"app: {appDirectory}");
        log.Add($"config: {configDirectory}");

        // ------------------------------------------------------ confirmation
        if (!options.Silent && !ConfirmUninstall(appDirectory))
        {
            log.Add("cancelled at the confirmation prompt");
            return 0;
        }

        // ------------------------------------------------- running processes
        List<RunningWiina> running = ProcessGuard.Find(appDirectory, log);
        List<RunningWiina> blocking = running.FindAll(item => item.Blocks);

        if (options.DryRun)
        {
            if (blocking.Count > 0)
            {
                log.Add($"dry-run: would force-close {blocking.Count} process(es) in the app folder");
            }

            log.Add($"dry-run: would remove Wiina file associations from HKCU, HKLM" +
                (shellUser is null ? string.Empty : $" and the hive of {shellUser.Sid}"));
            log.Add("dry-run: would ask whether to keep the configuration folder (default: keep)");
            if (!options.NoSelfDelete)
            {
                bool safe = SelfDelete.IsSafeTarget(appDirectory, out string reason);
                log.Add(safe
                    ? $"dry-run: would schedule deletion of {appDirectory}"
                    : $"dry-run: would refuse self-delete ({reason})");
            }

            string dryRunLog = WriteLog(log);
            if (!options.Silent)
            {
                NativeMethods.Info(
                    $"预演完成，未修改任何内容。\n\nDry run complete; nothing was changed.\n\n日志 / Log:\n{dryRunLog}",
                    ProductName);
            }

            return 0;
        }

        if (blocking.Count > 0)
        {
            if (!options.Silent && !ConfirmForceClose(blocking, running))
            {
                log.Add("cancelled at the running-process prompt; no changes were made");
                WriteLog(log);
                return 0;
            }

            if (options.Silent)
            {
                log.Add($"silent: force-closing {blocking.Count} process(es)");
            }

            if (!ProcessGuard.Terminate(appDirectory, blocking, log))
            {
                string path = WriteLog(log);
                if (!options.Silent)
                {
                    NativeMethods.Error(
                        "无法结束所有 Wiina 进程，卸载已取消。\n" +
                        "没有修改任何文件或注册表项。\n\n" +
                        "Could not stop every Wiina process, so the uninstall was cancelled\n" +
                        "before changing any file or registry entry.\n\n" +
                        "日志 / Log:\n" + path,
                        ProductName);
                }

                return 1;
            }
        }
        else if (running.Count > 0)
        {
            log.Add($"process: {running.Count} copy/copies outside the app folder are left running");
        }

        // -------------------------------------------------------- associations
        // Always removed, whether or not the config is kept: a leftover
        // Wiina.Video/Wiina.Audio ProgID would point at a folder that is about
        // to disappear. Elevated, so HKLM (and another user's loaded hive) go too.
        int registryRemoved = RegistryCleanup.RemoveAll(log);
        int otherUsersRemoved = RegistryCleanup.RemoveOtherUsers(log, shellUser?.Sid);
        log.Add($"registry: {registryRemoved + otherUsersRemoved} item(s) removed");

        int machineLeftovers = RegistryCleanup.MachineLeftovers();
        if (machineLeftovers > 0)
        {
            log.Add($"registry: {machineLeftovers} machine-wide item(s) could not be removed");
        }

        // ------------------------------------------------------------ settings
        bool keepConfig = options.KeepConfig ??
            (options.Silent || AskKeepConfig(configDirectory));
        log.Add($"keepConfig: {keepConfig}");
        log.Add($"selfDelete: {!options.NoSelfDelete}");

        bool configRemoved = true;
        if (!keepConfig)
        {
            configRemoved = TryDeleteConfig(configDirectory, log);
            if (!configRemoved)
            {
                log.Add("config: deletion failed; some files are probably in use");
            }
        }

        // ---------------------------------------------------------- self delete
        bool scheduled = true;
        if (!options.NoSelfDelete)
        {
            scheduled = SelfDelete.TrySchedule(appDirectory, log, out _);
        }
        else
        {
            log.Add("self-delete: disabled by --no-self-delete");
        }

        // ------------------------------------------------------------- result
        StringBuilder message = new();
        message.Append("Wiina 已卸载。\n\n");
        if (scheduled && !options.NoSelfDelete)
        {
            message.Append("应用文件夹将在几秒后自动删除：\n");
            message.Append(appDirectory);
            message.Append("\n\nUninstall complete. The application folder will be removed in a few seconds.");
        }
        else
        {
            message.Append("请手动删除应用文件夹：\n");
            message.Append(appDirectory);
            message.Append("\n\nUninstall complete. Please delete the application folder manually.");
        }

        if (!configRemoved)
        {
            message.Append("\n\n部分配置文件未能删除（可能正被占用）：\n");
            message.Append(configDirectory);
            message.Append("\n\nSome settings files could not be deleted because they are in use.");
        }

        if (machineLeftovers > 0)
        {
            message.Append("\n\n部分系统级文件关联无法删除（可能被系统策略或权限保护）：\n");
            message.Append("HKLM\\Software\\Classes\\Wiina.Video / Wiina.Audio、HKLM\\Software\\Wiina。\n");
            message.Append("\n\nSome machine-wide Wiina file associations could not be removed.\n");
            message.Append("They may be protected by system policy or permissions.");
        }

        if (!scheduled && !options.DryRun)
        {
            log.Add("self-delete: could not be scheduled; the folder was left in place");
        }

        // Only keep a diagnostic log when something needs explaining: silent
        // runs, a refused self-delete, settings that could not be removed or
        // machine-wide associations that need an elevated uninstall.
        bool needsLog = options.Silent || !scheduled || !configRemoved || machineLeftovers > 0;
        string? logPath = needsLog ? WriteLog(log) : null;
        if (logPath is not null)
        {
            message.Append("\n\n日志 / Log:\n");
            message.Append(logPath);
        }

        if (!options.Silent)
        {
            if (scheduled)
            {
                NativeMethods.Info(message.ToString(), ProductName);
            }
            else
            {
                NativeMethods.Warn(message.ToString(), ProductName);
            }
        }

        return scheduled || options.NoSelfDelete ? 0 : 1;
    }

    // ---------------------------------------------------------------- prompts

    private static bool ConfirmUninstall(string appDirectory) =>
        NativeMethods.Confirm(
            "确定要卸载 Wiina 吗？\n\n" +
            $"文件夹：\n{appDirectory}\n\n" +
            "只会删除这个文件夹里的 Wiina 程序文件；\n" +
            "你的视频、音乐和其他个人文件不会被删除。\n\n" +
            "Uninstall Wiina from this folder?\n\n" +
            "Only Wiina's program files in this folder are removed;\n" +
            "your videos, music and other personal files are never touched.",
            "卸载 Wiina / Uninstall Wiina",
            defaultYes: false);

    private static bool ConfirmForceClose(List<RunningWiina> blocking, List<RunningWiina> running)
    {
        StringBuilder details = new();
        foreach (RunningWiina item in blocking)
        {
            details.Append($"  {item.Name} (pid {item.ProcessId})");
            if (item.Path is not null)
            {
                details.Append($" — {item.Path}");
            }

            details.AppendLine();
        }

        int elsewhere = running.Count - blocking.Count;
        string extra = elsewhere > 0
            ? $"\n另有 {elsewhere} 个其他位置的 Wiina 进程不会被结束。\n" +
              $"{elsewhere} other Wiina process(es) outside this folder will be left running.\n"
            : string.Empty;

        return NativeMethods.Confirm(
            "Wiina 正在运行，是否强制结束并继续卸载？\n\n" +
            details + extra + "\n" +
            "是(Yes)：强制结束上面的进程并继续卸载。\n" +
            "否(No)：取消卸载，不删除任何文件或注册表项。\n\n" +
            "Wiina is running. Force-close it and continue?\n\n" +
            "Yes: close the processes above and continue.\n" +
            "No: cancel the uninstall without touching any file or registry entry.",
            "卸载 Wiina / Uninstall Wiina",
            defaultYes: false);
    }

    private static bool AskKeepConfig(string configDirectory) =>
        NativeMethods.Ask(
            "是否保留你的 Wiina 配置文件？\n\n" +
            "是(Yes)：保留设置、播放历史、插件、mpv 配置和日志，\n" +
            "重新安装后可以继续使用。\n" +
            "否(No)：删除下面的文件夹。\n" +
            $"{configDirectory}\n\n" +
            "Wiina 注册的文件关联已经被移除，不会影响其他播放器。\n\n" +
            "Keep your Wiina settings?\n\n" +
            "Yes: keep settings, playback history, plugins, mpv config and logs.\n" +
            "No: delete the folder above.\n\n" +
            "Wiina's file associations are already removed; other players are unaffected.",
            "Wiina",
            defaultYes: true);

    // ---------------------------------------------------------------- helpers

    private static bool AllowUnelevated() =>
        string.Equals(Environment.GetEnvironmentVariable(AllowUnelevatedVariable), "1", StringComparison.Ordinal);

    private static bool TryDeleteConfig(string configDirectory, List<string> log)
    {
        if (!Directory.Exists(configDirectory))
        {
            log.Add("config: folder does not exist");
            return true;
        }

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                Directory.Delete(configDirectory, recursive: true);
                log.Add($"config: deleted {configDirectory}");
                return true;
            }
            catch (Exception ex) when (attempt < 4)
            {
                log.Add($"config: attempt {attempt} failed ({ex.GetType().Name})");
                Thread.Sleep(250 * attempt);
            }
            catch (Exception ex)
            {
                log.Add($"config: could not delete {configDirectory} ({ex.GetType().Name}: {ex.Message})");
                return false;
            }
        }

        return false;
    }

    private static string WriteLog(List<string> log)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"wiina-uninstall-{Environment.ProcessId}.log");
        try
        {
            File.WriteAllLines(path, log, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return "(could not write the uninstall log)";
        }
    }

    private static int Fail(List<string> log, UninstallOptions options, string message)
    {
        log.Add("failed: " + message);
        log.Add("self-delete: not attempted");
        string path = WriteLog(log);
        if (!options.Silent)
        {
            NativeMethods.Error(message + "\n\n日志 / Log:\n" + path, ProductName);
        }

        return 1;
    }
}
