using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
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
        Title = "Wiina 日志";
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        WindowShell.Prepare(
            this,
            760,
            540,
            resizable: true,
            dark: App.Settings.Theme != ThemeMode.Light,
            minWidth: 520,
            minHeight: 360);

        FilterBox.ItemsSource = new[] { "全部", "跟踪日志", "崩溃日志" };
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
            _ => $"== 跟踪日志：{trace} =={Environment.NewLine}{ReadTail(trace)}{Environment.NewLine}"
                + $"== 崩溃日志：{crash} =={Environment.NewLine}{ReadTail(crash)}",
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
                return "（文件不存在）";
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
            return start > 0 ? "…（已截断较早内容）\n" + text : text;
        }
        catch (Exception ex)
        {
            return "读取失败：" + ex.Message;
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
}
