namespace Wiina.Uninstall;

/// <summary>
/// Command-line switches for the portable uninstaller. Interactive use passes
/// nothing at all; the switches exist for the packaging pipeline's smoke tests
/// and for silent/managed deployments where a message box would block.
/// </summary>
internal sealed class UninstallOptions
{
    /// <summary>Never show a message box; failures are reported by exit code.</summary>
    public bool Silent { get; private set; }

    /// <summary>null: ask (or keep, in silent mode).</summary>
    public bool? KeepConfig { get; private set; }

    /// <summary>null: ask (or remove, in silent mode).</summary>
    public bool? RemoveAssociations { get; private set; }

    /// <summary>Leave the folder alone; useful when files are in use or for tests.</summary>
    public bool NoSelfDelete { get; private set; }

    /// <summary>Log the planned actions to %TEMP% and change nothing.</summary>
    public bool DryRun { get; private set; }

    public static UninstallOptions Parse(string[] args)
    {
        UninstallOptions options = new();

        foreach (string argument in args)
        {
            switch (argument.Trim().ToLowerInvariant())
            {
                case "--silent":
                case "-s":
                    options.Silent = true;
                    break;
                case "--keep-config":
                    options.KeepConfig = true;
                    break;
                case "--remove-config":
                    options.KeepConfig = false;
                    break;
                case "--keep-associations":
                    options.RemoveAssociations = false;
                    break;
                case "--remove-associations":
                    options.RemoveAssociations = true;
                    break;
                case "--no-self-delete":
                    options.NoSelfDelete = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                default:
                    // Unknown switches are ignored so a shortcut or a future
                    // installer can pass extra arguments harmlessly.
                    break;
            }
        }

        return options;
    }
}
