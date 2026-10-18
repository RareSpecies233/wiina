using System.Globalization;

namespace Wiina.Core.Text;

/// <summary>Fullscreen status line formatting (clock + battery).</summary>
public static class FullscreenClock
{
    /// <summary>24-hour clock. Seconds are optional for the compact variant.</summary>
    public static string Format(DateTimeOffset now, bool includeSeconds = true)
    {
        DateTimeOffset local = now.ToLocalTime();
        return local.ToString(
            includeSeconds ? "HH:mm:ss" : "HH:mm",
            CultureInfo.InvariantCulture);
    }

    public static string FormatTime(TimeSpan timeOfDay, bool includeSeconds = true)
    {
        TimeSpan normalized = timeOfDay < TimeSpan.Zero
            ? timeOfDay + TimeSpan.FromDays(1)
            : timeOfDay;
        int hours = (int)normalized.TotalHours % 24;
        int minutes = normalized.Minutes;
        return includeSeconds
            ? $"{hours:00}:{minutes:00}:{normalized.Seconds:00}"
            : $"{hours:00}:{minutes:00}";
    }

    public static string FormatBattery(int? percent, bool charging, bool onAcPower)
    {
        if (percent is null)
        {
            return onAcPower ? "交流电源" : string.Empty;
        }

        int clamped = Math.Clamp(percent.Value, 0, 100);
        if (charging)
        {
            return $"{clamped}% · 充电中";
        }

        return onAcPower ? $"{clamped}% · 电源" : $"{clamped}%";
    }

    /// <summary>Segoe Fluent Icons battery glyph; charging uses the bolt glyph.</summary>
    public static string BatteryGlyph(int? percent, bool charging)
    {
        if (charging)
        {
            return "\uE83E";
        }

        if (percent is null)
        {
            return "\uE83F";
        }

        return percent.Value switch
        {
            <= 10 => "\uE83F",
            <= 30 => "\uE850",
            <= 60 => "\uE851",
            <= 90 => "\uE852",
            _ => "\uE83E",
        };
    }
}
