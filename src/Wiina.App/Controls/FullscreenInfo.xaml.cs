using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.Core.Text;

namespace Wiina.App.Controls;

/// <summary>Fullscreen bottom-left clock/battery/title strip.</summary>
public sealed partial class FullscreenInfo : UserControl
{
    private bool _clockEnabled = true;
    private bool _batteryEnabled = true;
    private bool _titleEnabled;

    public FullscreenInfo()
    {
        InitializeComponent();
    }

    public bool IsShowing => Root.Visibility == Visibility.Visible;

    public void Configure(bool clockEnabled, bool batteryEnabled, bool titleEnabled)
    {
        _clockEnabled = clockEnabled;
        _batteryEnabled = batteryEnabled;
        _titleEnabled = titleEnabled;
        ClockText.Visibility = clockEnabled ? Visibility.Visible : Visibility.Collapsed;
        BatteryGroup.Visibility = batteryEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Update(
        DateTimeOffset now,
        int? batteryPercent,
        bool charging,
        bool onAcPower,
        string? title)
    {
        ClockText.Text = FullscreenClock.Format(now);
        ClockText.Visibility = _clockEnabled ? Visibility.Visible : Visibility.Collapsed;

        if (_batteryEnabled)
        {
            string text = FullscreenClock.FormatBattery(batteryPercent, charging, onAcPower);
            BatteryText.Text = text;
            BatteryIcon.Glyph = FullscreenClock.BatteryGlyph(batteryPercent, charging);
            BatteryGroup.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        else
        {
            BatteryGroup.Visibility = Visibility.Collapsed;
        }

        bool showTitle = _titleEnabled && !string.IsNullOrWhiteSpace(title);
        TitleText.Text = showTitle ? title : string.Empty;
        TitleText.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetVisible(bool visible)
    {
        Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
