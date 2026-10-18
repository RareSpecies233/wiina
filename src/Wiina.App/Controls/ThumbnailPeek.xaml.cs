using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Wiina.App.Controls;

/// <summary>Hover thumbnail above the timeline (4 DIP corners, 160 DIP wide).</summary>
public sealed partial class ThumbnailPeek : UserControl
{
    public ThumbnailPeek()
    {
        InitializeComponent();
    }

    public bool IsShowing => Visibility == Visibility.Visible;

    public void ShowLoading(string timeText, double width, double height)
    {
        ApplySize(width, height);
        PreviewImage.Source = null;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        TimeText.Text = timeText;
        TimeBadge.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void Show(BitmapSource image, string timeText, double width, double height)
    {
        ApplySize(width, height);
        PreviewImage.Source = image;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        TimeText.Text = timeText;
        TimeBadge.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        LoadingRing.IsActive = false;
        PreviewImage.Source = null;
        TimeBadge.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    private void ApplySize(double width, double height)
    {
        Width = Math.Max(64, width);
        Height = Math.Max(36, height);
        Root.Width = Width;
        Root.Height = Height;
    }
}
