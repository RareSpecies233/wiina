using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Wiina.App.Controls;

/// <summary>Mute glyph + compact, DPI-safe 70 px volume slider.</summary>
public sealed partial class VolumeSlider : UserControl
{
    private const double WheelStep = 5;
    private bool _dragging;

    public VolumeSlider()
    {
        InitializeComponent();
    }

    /// <summary>Wheel over the slider always changes volume (spec §6.2 override).</summary>
    public event Action<double>? WheelVolumeRequested;

    private void OnSliderLoaded(object sender, RoutedEventArgs e)
    {
        UpdateVisuals();
    }

    private void OnSliderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void OnVolumeValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void OnSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        SliderHost.CapturePointer(e.Pointer);
        ApplyPointer(e.GetCurrentPoint(SliderHost).Position.X);
        e.Handled = true;
    }

    private void OnSliderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        ApplyPointer(e.GetCurrentPoint(SliderHost).Position.X);
        e.Handled = true;
    }

    private void OnSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ApplyPointer(e.GetCurrentPoint(SliderHost).Position.X);
        SliderHost.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
    }

    private void OnSliderWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(SliderHost).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        WheelVolumeRequested?.Invoke(Math.Sign(delta) * WheelStep);
        e.Handled = true;
    }

    private void ApplyPointer(double x)
    {
        double usable = Math.Max(1, SliderHost.ActualWidth - Thumb.Width);
        double ratio = Math.Clamp((x - (Thumb.Width / 2)) / usable, 0, 1);
        VolumeInput.Value = VolumeInput.Minimum + (ratio * (VolumeInput.Maximum - VolumeInput.Minimum));
    }

    private void UpdateVisuals()
    {
        if (SliderHost is null || VolumeInput is null || Thumb is null || Fill is null)
        {
            return;
        }

        double range = VolumeInput.Maximum - VolumeInput.Minimum;
        double ratio = range <= 0 ? 0 : Math.Clamp((VolumeInput.Value - VolumeInput.Minimum) / range, 0, 1);
        double usable = Math.Max(0, SliderHost.ActualWidth - Thumb.Width);
        double thumbLeft = ratio * usable;

        Thumb.Margin = new Thickness(thumbLeft, 0, 0, 0);
        Fill.Width = Math.Max(0, Math.Min(SliderHost.ActualWidth, thumbLeft + (Thumb.Width / 2)));
    }
}
