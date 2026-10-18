using System.IO;
using Microsoft.UI.Xaml;

namespace Wiina.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>File/folder/URL arguments passed on the command line.</summary>
    public static IReadOnlyList<string> StartupPaths { get; private set; } =
        Array.Empty<string>();

    public App()
    {
        InitializeComponent();
        StartupPaths = ParseStartupPaths(Environment.GetCommandLineArgs().Skip(1));
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
    }

    private static IReadOnlyList<string> ParseStartupPaths(IEnumerable<string> args)
    {
        var paths = new List<string>();
        foreach (string arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            // Launchers may hand us flags; only take paths/URLs.
            if (arg.StartsWith('-'))
            {
                continue;
            }

            paths.Add(arg);
        }

        return paths;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
    }

    private static void LogCrash(Exception? exception)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wiina",
                "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging hide the original failure.
        }
    }
}
