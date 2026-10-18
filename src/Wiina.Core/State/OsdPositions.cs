using Wiina.Core.Models;

namespace Wiina.Core.State;

/// <summary>
/// Ordered mapping between <see cref="OsdPosition"/> and the six UI choices.
/// Existing values keep their serialized names; new members are appended so old
/// settings files stay readable.
/// </summary>
public static class OsdPositions
{
    public static IReadOnlyList<OsdPosition> All { get; } = new[]
    {
        OsdPosition.BottomRight,
        OsdPosition.BottomLeft,
        OsdPosition.TopRight,
        OsdPosition.TopLeft,
        OsdPosition.TopCenter,
        OsdPosition.BottomCenter,
    };

    public static int ToIndex(OsdPosition position)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] == position)
            {
                return i;
            }
        }

        return 0;
    }

    public static OsdPosition FromIndex(int index) =>
        All[Math.Clamp(index, 0, All.Count - 1)];

    public static bool IsTop(OsdPosition position) =>
        position is OsdPosition.TopLeft or OsdPosition.TopRight or OsdPosition.TopCenter;

    public static bool IsLeft(OsdPosition position) =>
        position is OsdPosition.TopLeft or OsdPosition.BottomLeft;

    public static bool IsCenter(OsdPosition position) =>
        position is OsdPosition.TopCenter or OsdPosition.BottomCenter;
}
