using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

/// <summary>Mute glyph + 70 px volume slider with a notch at the 100% mark.</summary>
public sealed partial class VolumeSlider : UserControl
{
    private const double Maximum = 130;
    private const double Nominal = 100;

    public VolumeSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateNotch();
        SizeChanged += (_, _) => UpdateNotch();
    }

    private void UpdateNotch()
    {
        double width = SliderHost.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        // Slider tracks reserve room for the thumb at both ends; approximate
        // that inset so the notch lines up with the nominal 100% stop.
        const double inset = 8;
        double usable = Math.Max(1, width - inset * 2);
        double x = inset + usable * (Nominal / Maximum) - 1;
        UnityNotch.Margin = new Thickness(Math.Round(x), 0, 0, 0);
    }
}
