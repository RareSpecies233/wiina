namespace Wiina.Core.Settings;

/// <summary>What an "open settings" request should do to the window.</summary>
public enum SettingsOpenAction
{
    /// <summary>The window is already open and the request carries no target page or
    /// row: bring it forward and leave the user exactly where they were.</summary>
    ActivateExisting,

    /// <summary>Create the window (or jump an open one) to the requested page/row.</summary>
    NavigateThenActivate,
}

/// <summary>
/// Decides how <c>App.ShowSettingsWindow</c> should treat a request. A plain
/// "open settings" call has no page, so re-opening from the menu must not reset an
/// open window to 通用 — that loses the page, the scroll offset and any expanded
/// sections. Only explicit deep links (a page, a row, or both) navigate.
/// </summary>
public static class SettingsNavigation
{
    /// <summary>
    /// Classifies a request. <paramref name="windowOpen"/> is false for the first
    /// open, which always opens a page.
    /// </summary>
    public static SettingsOpenAction Decide(
        bool windowOpen,
        SettingsPageId? page,
        string? rowId)
    {
        if (!windowOpen)
        {
            return SettingsOpenAction.NavigateThenActivate;
        }

        return page is null && string.IsNullOrWhiteSpace(rowId)
            ? SettingsOpenAction.ActivateExisting
            : SettingsOpenAction.NavigateThenActivate;
    }

    /// <summary>
    /// Page a newly created window opens on: the requested page when the caller gave
    /// one, otherwise whatever page was open last (persisted on every navigation).
    /// </summary>
    public static SettingsPageId InitialPage(
        SettingsPageId? requestedPage,
        string? persistedPage) =>
        requestedPage ?? ParsePage(persistedPage);

    /// <summary>Parses a persisted page name, falling back to 通用.</summary>
    public static SettingsPageId ParsePage(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out SettingsPageId page)
            ? page
            : SettingsPageId.General;
}
