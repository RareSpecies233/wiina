using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.App.Views;
using Wiina.Core;
using Wiina.Core.History;
using Wiina.Core.Input;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Application host. Stage 6 makes this the owner of shared state for multiple
/// player windows: one settings object/store, one window registry and one
/// SystemMediaTransportControls session routed to the focused window.
/// </summary>
public partial class App : Application
{
    private static App? _instance;
    private static SmtcService? _mediaSession;
    private static SettingsWindow? _settingsWindow;
    private static KeyBindingWindow? _keyBindingWindow;
    private static WelcomeWindow? _welcomeWindow;
    private static AboutWindow? _aboutWindow;
    private static LogViewerWindow? _logWindow;
    private static InspectorWindow? _inspectorWindow;
    private static FiltersWindow? _filtersWindow;
    private static VideoQualityWindow? _qualityWindow;
    private readonly Dictionary<string, MainWindow> _windows = new();

    /// <summary>True when settings.json did not exist before this launch.</summary>
    public static bool IsFirstRun { get; private set; }

    /// <summary>File/folder/URL arguments passed on the command line.</summary>
    public static IReadOnlyList<string> StartupPaths { get; private set; } =
        Array.Empty<string>();

    /// <summary>Shared settings object (one instance for every player window).</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>Shared atomic JSON store.</summary>
    public static SettingsStore SettingsStore { get; } = new();

    /// <summary>Window registry + SMTC routing decisions (pure Core model).</summary>
    public static WindowCoordinator WindowRegistry { get; } = new();

    /// <summary>Raised after settings are saved; every window re-applies.</summary>
    public static event Action? SettingsChanged;

    public static MainWindow? ActiveWindow { get; private set; }

    public static App CurrentApp => (App)Application.Current;

    public App()
    {
        _instance = this;
        InitializeComponent();
        StartupPaths = ParseStartupPaths(Environment.GetCommandLineArgs().Skip(1));
        IsFirstRun = !File.Exists(SettingsStore.Path);
        Settings = SettingsStore.Load();
        Loc.Initialize(Settings.Language);
        Loc.LanguageChanged += (_, _) => ApplyLanguageToOpenWindows();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
    }

    /// <summary>
    /// Walks every open window's live visual tree after a language switch. The
    /// weak attached-property registry alone can miss controls whose element
    /// instance was recreated while the language event was being raised.
    /// </summary>
    private static void ApplyLanguageToOpenWindows()
    {
        var seen = new HashSet<Window>();
        foreach (MainWindow window in CurrentApp._windows.Values)
        {
            Apply(window);
        }

        Apply(ActiveWindow);
        Apply(_settingsWindow);
        Apply(_keyBindingWindow);
        Apply(_welcomeWindow);
        Apply(_aboutWindow);
        Apply(_logWindow);
        Apply(_inspectorWindow);
        Apply(_filtersWindow);
        Apply(_qualityWindow);

        void Apply(Window? window)
        {
            if (window is { Content: DependencyObject root } && seen.Add(window))
            {
                Loc.ApplyToTree(root);
            }
        }
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
            // Welcome is a standalone launcher: do not create an empty player
            // window behind it. A player window is created only when the user
            // opens media (or when the welcome experience is disabled).
            if (StartupPaths.Count == 0 && Settings.ShowWelcomeWindow)
            {
                ShowWelcomeWindow();
            }
            else
            {
                CreateWindow(StartupPaths);
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    /// <summary>
    /// Creates an independent player window. Stage 6: every window owns its
    /// MpvPlayer/OSC/sidebar state; only settings + SMTC are shared.
    /// </summary>
    public static MainWindow CreateWindow(IEnumerable<string>? paths = null)
    {
        App app = _instance ?? throw new InvalidOperationException("App is not initialized.");
        int existing = app._windows.Count;
        var window = new MainWindow((paths ?? Array.Empty<string>()).ToArray());
        if (existing > 0)
        {
            // Cascade secondary windows so they never land exactly on top of
            // the first player and look like nothing happened.
            Windows.Graphics.PointInt32 position = window.AppWindow.Position;
            window.AppWindow.Move(new Windows.Graphics.PointInt32(
                position.X + (32 * existing),
                position.Y + (32 * existing)));
        }

        app._windows[window.WindowId] = window;
        WindowRegistry.Register(window.WindowId, window.TitleText);
        window.Activated += (_, _) => ActivateWindow(window);
        window.Closed += (_, _) => OnWindowClosed(window);
        window.Activate();

        EnsureMediaSession();
        ActivateWindow(window);
        return window;
    }

    /// <summary>Window menu: open the given media in a brand-new window.</summary>
    public static MainWindow OpenInNewWindow(IEnumerable<string> paths) =>
        CreateWindow(paths);

    internal static void ActivateWindow(MainWindow window)
    {
        App app = CurrentApp;
        if (!app._windows.ContainsKey(window.WindowId))
        {
            return;
        }

        ActiveWindow = window;
        WindowRegistry.Activate(window.WindowId);
        window.PushMediaStateToSession();
    }

    private static void OnWindowClosed(MainWindow window)
    {
        App app = CurrentApp;
        if (!app._windows.Remove(window.WindowId))
        {
            return;
        }

        if (ReferenceEquals(ActiveWindow, window))
        {
            ActiveWindow = null;
        }

        WindowRegistry.Close(window.WindowId);
        if (WindowRegistry.ShouldExit)
        {
            ShutdownMediaSession();
            DispatcherQueue? queue = DispatcherQueue.GetForCurrentThread();
            _ = (queue?.TryEnqueue(() => app.Exit()));
            return;
        }

        string? nextId = WindowRegistry.ResolveMediaTargetId();
        if (nextId is not null && app._windows.TryGetValue(nextId, out MainWindow? next))
        {
            ActiveWindow = next;
            next.PushMediaStateToSession();
        }
    }

    public static void SaveSettings()
    {
        try
        {
            Loc.ApplyPreference(Settings.Language);
            SettingsStore.Save(Settings);
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }

    /// <summary>
    /// Called by every window on playback/title changes. Updates the shared
    /// registry, but only the active window is allowed to drive the SMTC
    /// display so two players never fight over the system overlay.
    /// </summary>
    public static void PublishMediaState(
        MainWindow window,
        bool hasMedia,
        bool playing,
        string? title,
        double duration)
    {
        WindowRegistry.Update(window.WindowId, title, hasMedia);
        if (ReferenceEquals(ActiveWindow, window))
        {
            _mediaSession?.Update(hasMedia, playing, title, duration);
        }
    }

    public static void UpdateWindowTitle(MainWindow window, string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            WindowRegistry.Update(window.WindowId, title);
        }
    }

    /// <summary>True when this window is the SMTC/media-key target.</summary>
    public static bool IsMediaTarget(MainWindow window) =>
        ReferenceEquals(ActiveWindow, window);

    private static void EnsureMediaSession()
    {
        if (_mediaSession is not null)
        {
            return;
        }

        var service = new SmtcService();
        if (!service.Initialize())
        {
            service.Dispose();
            return;
        }

        service.PlayRequested += () => RouteMedia(MediaCommand.Play);
        service.PauseRequested += () => RouteMedia(MediaCommand.Pause);
        service.StopRequested += () => RouteMedia(MediaCommand.Stop);
        service.NextRequested += () => RouteMedia(MediaCommand.Next);
        service.PreviousRequested += () => RouteMedia(MediaCommand.Previous);
        service.SeekRequested += seconds => RouteMedia(MediaCommand.Seek, seconds);
        _mediaSession = service;
    }

    private static void RouteMedia(MediaCommand command, double position = 0)
    {
        MainWindow? target = ActiveWindow;
        if (target is null)
        {
            return;
        }

        target.DispatcherQueue.TryEnqueue(
            () => target.HandleMediaCommand(command, position));
    }

    private static void ShutdownMediaSession()
    {
        _mediaSession?.Dispose();
        _mediaSession = null;
    }

    // ------------------------------------------------------- Stage 7 windows

    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wiina");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    public static string PluginsFolder => Path.Combine(DataFolder, "plugins");

    public static string ConfigFolder => Path.Combine(DataFolder, "mpv");

    public static string TraceLogPath => Path.Combine(Path.GetTempPath(), "wiina-trace.log");

    public static string CrashLogPath => Path.Combine(DataFolder, "crash.log");

    public static void ShowSettingsWindow(
        SettingsPageId page = SettingsPageId.General,
        string? rowId = null)
    {
        if (_settingsWindow is { } existing)
        {
            existing.NavigateTo(page, rowId);
            WindowShell.BringToForeground(existing);
            return;
        }

        var window = new SettingsWindow(page, rowId);
        _settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };
        window.Activate();
    }

    public static void ShowKeyBindingWindow()
    {
        if (_keyBindingWindow is { } existing)
        {
            WindowShell.BringToForeground(existing);
            return;
        }

        var window = new KeyBindingWindow();
        _keyBindingWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_keyBindingWindow, window))
            {
                _keyBindingWindow = null;
            }
        };
        window.Activate();
    }

    public static void ShowWelcomeWindow()
    {
        if (_welcomeWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new WelcomeWindow();
        _welcomeWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_welcomeWindow, window))
            {
                _welcomeWindow = null;
            }

            // Closing the launcher without opening media exits the app; if a
            // player/settings window exists, keep running normally.
            if (CurrentApp._windows.Count == 0 && _settingsWindow is null)
            {
                window.DispatcherQueue.TryEnqueue(() => CurrentApp.Exit());
            }
        };
        window.Activate();
    }

    public static void HideWelcomeWindow()
    {
        WelcomeWindow? window = _welcomeWindow;
        _welcomeWindow = null;
        if (window is null)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch
        {
            // The window may already be closing.
        }
    }

    public static void ShowAboutWindow()
    {
        if (_aboutWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new AboutWindow();
        _aboutWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_aboutWindow, window))
            {
                _aboutWindow = null;
            }
        };
        window.Activate();
    }

    public static void ShowLogViewerWindow()
    {
        if (_logWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new LogViewerWindow();
        _logWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_logWindow, window))
            {
                _logWindow = null;
            }
        };
        window.Activate();
    }

    public static void ShowInspectorWindow()
    {
        if (_inspectorWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new InspectorWindow();
        _inspectorWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_inspectorWindow, window))
            {
                _inspectorWindow = null;
            }
        };
        window.Activate();
    }

    /// <summary>Opens the single video/audio filter + equalizer window.</summary>
    public static void ShowFiltersWindow(bool audioTab = false)
    {
        if (_filtersWindow is { } existing)
        {
            existing.SelectTab(audioTab);
            existing.Activate();
            return;
        }

        var window = new FiltersWindow();
        _filtersWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_filtersWindow, window))
            {
                _filtersWindow = null;
            }
        };
        window.Activate();
        window.SelectTab(audioTab);
    }

    /// <summary>Opens the named quality-profile manager/editor.</summary>
    public static void ShowVideoQualityWindow()
    {
        if (_qualityWindow is { } existing)
        {
            WindowShell.BringToForeground(existing);
            return;
        }

        var window = new VideoQualityWindow();
        _qualityWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_qualityWindow, window))
            {
                _qualityWindow = null;
            }
        };
        window.Activate();
    }

    /// <summary>Opens media in the active window or a new one (welcome/menu).</summary>
    public static void OpenMedia(IEnumerable<string> paths, bool newWindow = false)
    {
        string[] list = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (list.Length == 0)
        {
            return;
        }

        if (newWindow)
        {
            OpenInNewWindow(list);
            HideWelcomeWindow();
            return;
        }

        if (ActiveWindow is { } active)
        {
            active.Activate();
            HideWelcomeWindow();
            active.OpenExternalPaths(list);
            return;
        }

        // Create the player with the paths already attached. Opening them on a
        // just-constructed empty window races the VideoHost/mpv initialization.
        MainWindow created = CreateWindow(list);
        created.Activate();
        HideWelcomeWindow();
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }

    public static void ClearHistory() =>
        new HistoryStore().Save(Array.Empty<HistoryEntry>());

    public static string ExportDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{Branding.Name} {Branding.Version} diagnostics");
        builder.AppendLine($"Time: {DateTimeOffset.Now:O}");
        builder.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        builder.AppendLine($"Runtime: .NET {Environment.Version}");
        builder.AppendLine($"libmpv client API: {MpvRuntime.ClientApiVersion}");
        builder.AppendLine($"First run: {IsFirstRun}");
        if (ActiveWindow is { } window)
        {
            builder.AppendLine();
            builder.AppendLine("== active media ==");
            foreach (InspectorRow row in window.GetInspectorSnapshot())
            {
                builder.AppendLine($"[{row.Section}] {row.Name}: {row.Value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("== settings.json ==");
        builder.AppendLine(JsonSerializer.Serialize(
            Settings,
            new JsonSerializerOptions { WriteIndented = true }));

        Directory.CreateDirectory(LogsFolder);
        string path = Path.Combine(
            LogsFolder,
            $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, builder.ToString());
        OpenFolder(LogsFolder);
        return path;
    }

    /// <summary>
    /// Resets every settings property in place (windows hold the same instance),
    /// preserving key bindings, recent list and the last window frame.
    /// </summary>
    public static void ResetSettings()
    {
        var defaults = new AppSettings();
        List<KeyBindingSetting> keys = Settings.KeyBindings;
        List<string> recent = Settings.RecentUrls;
        int width = Settings.WindowWidth;
        int height = Settings.WindowHeight;
        int x = Settings.WindowX;
        int y = Settings.WindowY;

        foreach (PropertyInfo property in typeof(AppSettings).GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite)
            {
                property.SetValue(Settings, property.GetValue(defaults));
            }
        }

        Settings.KeyBindings = keys;
        Settings.RecentUrls = recent;
        Settings.WindowWidth = width;
        Settings.WindowHeight = height;
        Settings.WindowX = x;
        Settings.WindowY = y;
        SaveSettings();
    }

    private static void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
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

