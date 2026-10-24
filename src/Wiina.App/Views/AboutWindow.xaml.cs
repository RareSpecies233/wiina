using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Wiina.App.Localization;
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
        Title = Loc.T("about.title");
        RootGrid.RequestedTheme = App.ResolveTheme();
        WindowShell.Prepare(
            this,
            440,
            430,
            resizable: false,
            dark: App.IsDarkTheme,
            minWidth: 440,
            minHeight: 430);

        VersionText.Text = Loc.F("about.version", Branding.Version, Branding.Tagline);
        BuildInfo();
        Closed += (_, _) => WindowShell.Detach(this);
    }

    private void BuildInfo()
    {
        Add(Loc.T("about.license"), Loc.T("about.licenseValue"));
        Add(Loc.T("about.engine"), $"mpv / libmpv (client API {MpvRuntime.ClientApiVersion})");
        Add(Loc.T("about.ui"), $"WinUI 3 / Windows App SDK {WinUiVersion()}");
        Add(Loc.T("about.runtime"), $".NET {Environment.Version} · {Environment.OSVersion.VersionString}");
        Add(Loc.T("about.homepage"), Branding.ProjectUrl);
        Add(Loc.T("about.disclaimer"), Branding.Disclaimer);
        Add(Loc.T("about.fonts"), Loc.T("about.fontsValue"));
        Add(Loc.T("about.note"), Loc.T("about.noteValue"));
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

            return assembly.GetName().Version?.ToString(3) ?? Loc.T("about.unknown");
        }
        catch
        {
            return Loc.T("about.unknown");
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
