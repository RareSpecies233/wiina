namespace Wiina.Core.State;

/// <summary>
/// Maps the real player volume (0..MaxVolume) onto the 0..100% controls.
/// Keyboard volume-up may go above 100%; the slider then stays pinned at 100
/// until the user deliberately lowers it.
/// </summary>
public static class VolumeMapping
{
    public const double UiMaximum = 100;

    public static double ToUi(double actualVolume) =>
        Math.Clamp(actualVolume, 0, UiMaximum);

    /// <summary>
    /// Applied when the slider (or a click on its track) reports a new value.
    /// A report of exactly 100 while the player is already amplified keeps the
    /// underlying value; anything below 100 lowers it normally.
    /// </summary>
    public static double ApplySliderValue(double actualVolume, double requestedUi)
    {
        double requested = Math.Clamp(requestedUi, 0, UiMaximum);
        if (actualVolume > UiMaximum && requested >= UiMaximum)
        {
            return actualVolume;
        }

        return requested;
    }

    /// <summary>
    /// Wheel/keyboard-independent slider step. Up stops at 100; down walks
    /// naturally back through the amplified range before crossing 100.
    /// </summary>
    public static double ApplyWheelDelta(double actualVolume, double delta)
    {
        if (delta > 0)
        {
            return actualVolume >= UiMaximum
                ? actualVolume
                : Math.Min(UiMaximum, actualVolume + delta);
        }

        if (delta < 0)
        {
            return actualVolume > UiMaximum
                ? Math.Max(UiMaximum, actualVolume + delta)
                : Math.Max(0, actualVolume + delta);
        }

        return actualVolume;
    }

    public static double ClampActual(double value, double maxVolume)
    {
        double maximum = Math.Clamp(maxVolume, 100, 200);
        return Math.Clamp(value, 0, maximum);
    }

    public static string FormatUi(double actualVolume) =>
        $"{Math.Round(ToUi(actualVolume), MidpointRounding.AwayFromZero)}%";
}
