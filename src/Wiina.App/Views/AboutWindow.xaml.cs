using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Wiina.App.Services;
using Wiina.Core;
using Wiina.Core.Models;
using Wiina.Mpv;

namespace Wiina.App.Views;

/// <summary>About window: identity, license, mpv/WinUI versions and disclaimer.</summary>
public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Title = "关于 Wiina";
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        WindowShell.Prepare(
            this,
            440,
            430,
            resizable: false,
            dark: App.Settings.Theme != ThemeMode.Light,
            minWidth: 440,
            minHeight: 430);

        VersionText.Text = $"版本 {Branding.Version} · {Branding.Tagline}";
        BuildInfo();
        Closed += (_, _) => WindowShell.Detach(this);
    }

    private void BuildInfo()
    {
        Add("许可证", "GNU GPL-3.0-only（含无担保声明）");
        Add("播放引擎", $"mpv / libmpv（客户端 API {MpvRuntime.ClientApiVersion}）");
        Add("界面", $"WinUI 3 / Windows App SDK {WinUiVersion()}");
        Add("运行时", $".NET {Environment.Version} · {Environment.OSVersion.VersionString}");
        Add("项目主页", Branding.ProjectUrl);
        Add("声明", Branding.Disclaimer);
        Add("字体/图标", "Segoe UI · Segoe Fluent Icons（Microsoft 许可，未使用 SF Symbols）");
        Add("说明", "Wiina 为原创实现；未复制 IINA 的源码、图片或应用图标。");
    }

    private void Add(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var text = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        InfoPanel.Children.Add(grid);
    }

    private static string WinUiVersion()
    {
        try
        {
            Assembly assembly = typeof(Microsoft.UI.Xaml.UIElement).Assembly;
            string? version = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                int plus = version.IndexOf('+', StringComparison.Ordinal);
                return plus > 0 ? version[..plus] : version;
            }

            return assembly.GetName().Version?.ToString(3) ?? "未知";
        }
        catch
        {
            return "未知";
        }
    }

    private string BuildCopyText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{Branding.Name} {Branding.Version}");
        builder.AppendLine(Branding.Tagline);
        builder.AppendLine($"libmpv client API: {MpvRuntime.ClientApiVersion}");
        builder.AppendLine($"WinUI: {WinUiVersion()}");
        builder.AppendLine($"Runtime: .NET {Environment.Version}");
        builder.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        builder.AppendLine(Branding.ProjectUrl);
        builder.AppendLine(Branding.Disclaimer);
        return builder.ToString();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(BuildCopyText());
        Clipboard.SetContent(package);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
