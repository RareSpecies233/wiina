using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Wiina.App.Controls;

/// <summary>Mute glyph + native WinUI 3 volume slider.</summary>
public sealed partial class VolumeSlider : UserControl
{
    private const double WheelStep = 5;

    public VolumeSlider()
    {
        InitializeComponent();
    }

    /// <summary>Wheel over the slider always changes volume (spec §6.2 override).</summary>
    public event Action<double>? WheelVolumeRequested;

    private void OnSliderWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(VolumeInput).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        WheelVolumeRequested?.Invoke(Math.Sign(delta) * WheelStep);
        e.Handled = true;
    }
}
