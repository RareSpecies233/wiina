using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wiina.App.Services;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.State;

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
    private readonly Dictionary<string, MainWindow> _windows = new();

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
        Settings = SettingsStore.Load();
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
            CreateWindow(StartupPaths);
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
