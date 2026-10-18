using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.Core.Filters;

namespace Wiina.App.Controls;

public sealed class EqualizerGainsChangedEventArgs : EventArgs
{
    public IReadOnlyList<double> Gains { get; init; } = Array.Empty<double>();
}

/// <summary>
/// Ten vertical equalizer sliders shared by the quick-settings card and the
/// audio filter window. The control only owns slider state; callers persist
/// and apply the gains.
/// </summary>
public sealed partial class EqualizerView : UserControl
{
    private readonly List<Slider> _sliders = new();
    private readonly List<TextBlock> _valueLabels = new();
    private bool _loading;
    private bool _enabled = true;

    public EqualizerView()
    {
        InitializeComponent();
        BuildBands();
    }

    public event EventHandler<EqualizerGainsChangedEventArgs>? GainsChanged;

    public IReadOnlyList<double> Gains =>
        _sliders.Select(slider => Math.Round(slider.Value, 2)).ToArray();

    public void SetGains(IEnumerable<double>? gains)
    {
        List<double> values = EqualizerBands.NormalizeGains(gains);
        _loading = true;
        try
        {
            for (int i = 0; i < _sliders.Count; i++)
            {
                _sliders[i].Value = values[i];
                _valueLabels[i].Text = FormatGain(values[i]);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        foreach (Slider slider in _sliders)
        {
            slider.IsEnabled = enabled;
        }

        Opacity = enabled ? 1 : 0.55;
    }

    private void BuildBands()
    {
        for (int i = 0; i < EqualizerBands.BandCount; i++)
        {
            var band = new StackPanel
            {
                Width = 36,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var value = new TextBlock
            {
                Text = "0.0",
                FontSize = 9,
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _valueLabels.Add(value);

            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = EqualizerBands.MinimumGain,
                Maximum = EqualizerBands.MaximumGain,
                StepFrequency = 0.5,
                Height = 150,
                Width = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsDirectionReversed = false,
                Tag = i,
            };
            slider.ValueChanged += OnSliderValueChanged;
            _sliders.Add(slider);

            band.Children.Add(value);
            band.Children.Add(slider);
            band.Children.Add(new TextBlock
            {
                Text = EqualizerBands.Labels[i],
                FontSize = 9,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            band.Children.Add(new TextBlock
            {
                Text = "Hz",
                FontSize = 9,
                Opacity = 0.4,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            ToolTipService.SetToolTip(
                slider,
                $"{EqualizerBands.Frequencies[i]} Hz · −12…+12 dB");
            BandsPanel.Children.Add(band);
        }
    }

    private void OnSliderValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider slider || slider.Tag is not int index)
        {
            return;
        }

        if (index >= 0 && index < _valueLabels.Count)
        {
            _valueLabels[index].Text = FormatGain(e.NewValue);
        }

        if (_loading || !_enabled)
        {
            return;
        }

        GainsChanged?.Invoke(this, new EqualizerGainsChangedEventArgs
        {
            Gains = Gains,
        });
    }

    private static string FormatGain(double value) =>
        value.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
}
