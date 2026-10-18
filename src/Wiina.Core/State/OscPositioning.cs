namespace Wiina.Core.State;

/// <summary>
/// Pure geometry helpers for the draggable floating OSC. UI code works in
/// device-independent pixels and stores the OSC centre as a 0..1 fraction of
/// the video area.
/// </summary>
public static class OscPositioning
{
    /// <summary>
    /// Keep an element of <paramref name="elementSize"/> fully inside a
    /// container, returning the valid centre coordinate.
    /// </summary>
    public static double ClampCenter(double center, double elementSize, double containerSize)
    {
        if (containerSize <= 0 || elementSize <= 0)
        {
            return 0;
        }

        if (elementSize >= containerSize)
        {
            return containerSize / 2.0;
        }

        double half = elementSize / 2.0;
        return Math.Clamp(center, half, containerSize - half);
    }

    /// <summary>Snap a horizontal centre to the container centre within a threshold.</summary>
    public static double SnapToCenter(double center, double containerSize, double threshold = 5)
    {
        double target = containerSize / 2.0;
        return Math.Abs(center - target) <= threshold ? target : center;
    }

    public static double NormalizeCenter(double center, double containerSize)
    {
        return containerSize <= 0
            ? 0.5
            : Math.Clamp(center / containerSize, 0, 1);
    }

    public static double DenormalizeCenter(double fraction, double containerSize)
    {
        return Math.Clamp(fraction, 0, 1) * Math.Max(0, containerSize);
    }
}
