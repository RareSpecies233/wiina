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

    /// <summary>True while a decoded frame (not just the placeholder) is shown.</summary>
    public bool HasFrame => PreviewImage.Source is not null;

    /// <summary>
    /// Shows the placeholder for a new hover position. A frame that is already
    /// on screen is intentionally kept: the black backplate used to flash on
    /// every pointer move because this reset the image (§3.1/§3.2 of the 1.3.8
    /// plan). The progress ring only spins while there is nothing to show.
    /// </summary>
    public void ShowLoading(string timeText, double width, double height)
    {
        ApplySize(width, height);
        bool hasFrame = PreviewImage.Source is not null;
        if (!hasFrame)
        {
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
        }
        else
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }

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

    /// <summary>
    /// Clears the frame but keeps the card visible with the ring: used when the
    /// policy forbids a preview (empty backplate, no stale frame).
    /// </summary>
    public void ShowEmpty(string timeText, double width, double height)
    {
        ApplySize(width, height);
        PreviewImage.Source = null;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        TimeText.Text = timeText;
        TimeBadge.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    private void ApplySize(double width, double height)
    {
        Width = Math.Max(64, width);
        Height = Math.Max(36, height);
        Root.Width = Width;
        Root.Height = Height;
    }
}
