namespace Wiina.Core.Settings;

/// <summary>How the live chrome blur obtains its video frame.</summary>
public enum ChromeBlurMode
{
    /// <summary>Capture once when the chrome becomes visible, then reuse it.</summary>
    Fixed = 0,

    /// <summary>Capture while the OSC/OSD sidebar chrome is visible (default).</summary>
    Dynamic = 1,

    /// <summary>Keep calculating even when no chrome is visible.</summary>
    Always = 2,

    /// <summary>No capture or blur: the opaque solid chrome brush is shown.</summary>
    Solid = 3,
}
