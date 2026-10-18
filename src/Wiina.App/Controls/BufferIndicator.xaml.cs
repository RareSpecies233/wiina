using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

/// <summary>Center status pill for cache buffering, seeking and network opens.</summary>
public sealed partial class BufferIndicator : UserControl
{
    public BufferIndicator()
    {
        InitializeComponent();
    }

    public bool IsShowing => Root.Visibility == Visibility.Visible;

    public void Show(string label, string? detail = null, bool indeterminate = true)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            Hide();
            return;
        }

        LabelText.Text = label;
        if (string.IsNullOrWhiteSpace(detail))
        {
            DetailText.Text = string.Empty;
            DetailText.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailText.Text = detail;
            DetailText.Visibility = Visibility.Visible;
        }

        Ring.IsActive = indeterminate;
        Root.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Ring.IsActive = false;
        Root.Visibility = Visibility.Collapsed;
    }
}
