using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

public sealed partial class SidebarHost : UserControl
{
    public SidebarHost()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseClicked;

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseClicked?.Invoke(this, EventArgs.Empty);
}
