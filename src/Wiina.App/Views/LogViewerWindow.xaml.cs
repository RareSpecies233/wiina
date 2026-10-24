using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core.Models;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Wiina.App.Views;

/// <summary>Trace/crash log viewer with filter and manual/automatic refresh.</summary>
public sealed partial class LogViewerWindow : Window
{
    private const int MaxCharacters = 200_000;
    private readonly DispatcherQueueTimer _timer;

    public LogViewerWindow()
    {
        InitializeComponent();
        Title = Loc.T("log.title");
        RootGrid.RequestedTheme = App.ResolveTheme();
        WindowShell.Prepare(
            this,
            760,
            540,
            resizable: true,
            dark: App.IsDarkTheme,
            minWidth: 520,
            minHeight: 360,
            extendTitleBar: true,
            titleBar: DragRegion);

        AddEscapeAccelerator();

        FilterBox.ItemsSource = new[] { Loc.T("log.filterAll"), Loc.T("log.filterTrace"), Loc.T("log.filterCrash") };
        FilterBox.SelectedIndex = 0;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) =>
        {
            if (AutoRefreshSwitch.IsOn)
            {
                Load();
            }
        };
        _timer.Start();

        Load();
        Closed += (_, _) =>
        {
            _timer.Stop();
            WindowShell.Detach(this);
        };
    }

    private void Load()
    {
        string trace = App.TraceLogPath;
        string crash = App.CrashLogPath;
        string text = FilterBox.SelectedIndex switch
        {
            1 => ReadTail(trace),
            2 => ReadTail(crash),
            _ => Loc.F("log.traceHeader", trace) + $"{Environment.NewLine}{ReadTail(trace)}{Environment.NewLine}"
                + Loc.F("log.crashHeader", crash) + $"{Environment.NewLine}{ReadTail(crash)}",
        };

        PathText.Text = FilterBox.SelectedIndex switch
        {
            1 => trace,
            2 => crash,
            _ => App.LogsFolder,
        };

        if (!string.Equals(LogText.Text, text, StringComparison.Ordinal))
        {
            LogText.Text = text;
            LogText.SelectionStart = LogText.Text.Length;
        }
    }

    private static string ReadTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Loc.T("log.missing");
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - MaxCharacters);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            return start > 0 ? Loc.T("log.truncated") + "\n" + text : text;
        }
        catch (Exception ex)
        {
            return Loc.F("log.readFailed", ex.Message);
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => Load();

    private void OnRefreshClick(object sender, RoutedEventArgs e) => Load();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        App.OpenFolder(App.LogsFolder);

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(LogText.Text);
        Clipboard.SetContent(package);
    }

    private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshSwitch.IsOn)
        {
            Load();
        }
    }

    /// <summary>Esc closes the window, matching the other secondary windows.</summary>
    private void AddEscapeAccelerator()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            args.Handled = true;
            Close();
        };
        RootGrid.KeyboardAccelerators.Add(escape);
    }
}
