using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

/// <summary>
/// Stage 4 keeps the plugins tab as a container placeholder. The real
/// WebView2 host, Info.json parsing and permissions are Stage 9 work; the
/// install button is disabled on purpose rather than pretending to work.
/// </summary>
public sealed partial class PluginsView : UserControl
{
    public PluginsView()
    {
        InitializeComponent();
    }
}
