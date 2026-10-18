namespace Wiina.Core.Release;

/// <summary>
/// Names of the icon-only controls whose accessible name must never be empty.
/// The list is pure data so a unit test can verify that every key exists in the
/// UI catalog and that the title bar XAML wires it up.
/// </summary>
public static class AccessibilityMetadata
{
    /// <summary>Localization keys used as AutomationProperties.Name.</summary>
    public static IReadOnlyList<string> IconOnlyAutomationKeys { get; } = new[]
    {
        "ui.pin",
        "ui.layout",
        "ui.oscPosition",
        "ui.sidebar",
        "ui.theme",
        "ui.iconMapping",
        "ui.windowSize",
        "ui.more",
        "ui.previous",
        "ui.playPause",
        "ui.next",
    };
}
