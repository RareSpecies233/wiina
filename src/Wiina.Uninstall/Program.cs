using System.Text;

namespace Wiina.Uninstall;

/// <summary>
/// Portable uninstaller (issues 8/9). The portable zip has no installer, so this
/// executable is the uninstall entry point shipped next to <c>Wiina.exe</c>:
///
/// 1. confirm the uninstall (default No — it deletes files);
/// 2. always remove the file associations Wiina registered (HKCU, plus HKLM
///    when this process has the rights) — this is never asked, because leaving
///    a broken ProgID behind is strictly worse than removing a working one;
/// 3. ask whether to keep the per-user settings (default Yes), matching the
///    installer's uninstall prompt and a silent uninstall's keep-by-default;
///    only an explicit No deletes <c>%LOCALAPPDATA%\Wiina</c> (settings,
///    history, plugins, mpv config, logs, crash log) — never any user media;
/// 4. schedule deletion of the portable folder after this process exits.
///
/// Exit codes: 0 = done or cancelled, 1 = the folder could not be removed
/// automatically (the user is told to delete it manually).
/// </summary>
internal static class Program
{
    private const string ProductName = "Wiina";

    private static int Main(string[] args)
    {
        UninstallOptions options = UninstallOptions.Parse(args);

        string appDirectory = (AppContext.BaseDirectory ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);

        List<string> log =
        [
            $"Wiina portable uninstaller, pid {Environment.ProcessId}",
            $"time: {DateTimeOffset.Now:O}",
            $"app: {appDirectory}",
            $"config: {configDirectory}",
        ];

        if (string.IsNullOrEmpty(appDirectory))
        {
            return Fail(log, options, "无法确定 Wiina 所在文件夹。\n\nCould not determine the Wiina application folder.");
        }

        // ---------------------------------------------------------- questions
        if (!options.Silent && !ConfirmUninstall(appDirectory))
        {
            log.Add("cancelled at the confirmation prompt");
            return 0;
        }

        bool keepConfig = options.KeepConfig ??
            (options.Silent || AskKeepConfig(configDirectory));

        log.Add($"keepConfig: {keepConfig}");
        log.Add($"selfDelete: {!options.NoSelfDelete}");
        log.Add($"dryRun: {options.DryRun}");

        // ------------------------------------------------------------ dry run
        if (options.DryRun)
        {
            log.Add("dry-run: no changes made");
            if (!keepConfig)
            {
                log.Add($"dry-run: would delete {configDirectory}");
            }

            log.Add(RegistryCleanup.HasRegistration()
                ? "dry-run: would remove the Wiina file associations from HKCU and HKLM"
                : "dry-run: no Wiina file associations are registered");

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
                    "Wiina");
            }

            return 0;
        }

        // --------------------------------------------------------- deletions
        // Associations are always removed, whether or not the config is kept:
        // a leftover Wiina.Video/Wiina.Audio ProgID would point at a folder
        // that is about to disappear.
        int registryRemoved = RegistryCleanup.RemoveAll(log);
        log.Add($"registry: {registryRemoved} item(s) removed");

        int machineLeftovers = RegistryCleanup.MachineLeftovers();
        if (machineLeftovers > 0)
        {
            log.Add($"registry: {machineLeftovers} machine-wide item(s) still need administrator rights");
        }

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
            message.Append("\n\n部分文件关联写入了系统（HKLM）注册表，需要管理员权限才能删除。\n");
            message.Append("如确认要清理，请右键卸载程序 →“以管理员身份运行”。");
            message.Append("\n\nSome Wiina file associations were registered machine-wide (HKLM) and need");
            message.Append(" administrator rights to remove. Run the uninstaller as administrator to clean them up.");
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
            "确定要卸载 Wiina 吗？\n" +
            "请先退出 Wiina。\n\n" +
            $"文件夹：\n{appDirectory}\n\n" +
            "只会删除这个文件夹里的 Wiina 程序文件；\n" +
            "你的视频、音乐和其他个人文件不会被删除。\n\n" +
            "Uninstall Wiina from this folder?\n" +
            "Close Wiina first.\n\n" +
            "Only Wiina's program files in this folder are removed;\n" +
            "your videos, music and other personal files are never touched.",
            "卸载 Wiina / Uninstall Wiina",
            defaultYes: false);

    private static bool AskKeepConfig(string configDirectory) =>
        NativeMethods.Ask(
            "是否保留你的 Wiina 配置文件？\n\n" +
            "是(Yes)：保留设置、播放历史、插件、mpv 配置和日志，\n" +
            "重新安装后可以继续使用。\n" +
            "否(No)：删除下面的文件夹。\n" +
            $"{configDirectory}\n\n" +
            "Wiina 注册的文件关联始终会被移除，不会影响其他播放器。\n\n" +
            "Keep your Wiina settings?\n\n" +
            "Yes: keep settings, playback history, plugins, mpv config and logs.\n" +
            "No: delete the folder above.\n\n" +
            "Wiina's file associations are always removed; other players are unaffected.",
            "Wiina",
            defaultYes: true);

    // ---------------------------------------------------------------- helpers

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
