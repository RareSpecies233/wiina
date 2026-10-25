using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Re-opening settings from the menu used to navigate an already-open window back to
/// 通用, throwing away the page (and its scroll offset). A plain open must only
/// activate; only a page/row deep link may navigate.
/// </summary>
public sealed class SettingsNavigationTests
{
    [Fact]
    public void PlainRequestOnAnOpenWindowOnlyActivates()
    {
        Assert.Equal(
            SettingsOpenAction.ActivateExisting,
            SettingsNavigation.Decide(windowOpen: true, page: null, rowId: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRowIsNotADeepLink(string rowId)
    {
        Assert.Equal(
            SettingsOpenAction.ActivateExisting,
            SettingsNavigation.Decide(windowOpen: true, page: null, rowId: rowId));
    }

    [Fact]
    public void ExplicitPageNavigatesAnOpenWindow()
    {
        Assert.Equal(
            SettingsOpenAction.NavigateThenActivate,
            SettingsNavigation.Decide(windowOpen: true, page: SettingsPageId.Subtitles, rowId: null));
    }

    [Fact]
    public void RowOnlyDeepLinkNavigatesAnOpenWindow()
    {
        // Search hits and row deep links carry a row; they must still jump.
        Assert.Equal(
            SettingsOpenAction.NavigateThenActivate,
            SettingsNavigation.Decide(
                windowOpen: true,
                page: null,
                rowId: "video.thumbnail-mode"));
    }

    [Fact]
    public void FirstOpenAlwaysOpensAPage()
    {
        Assert.Equal(
            SettingsOpenAction.NavigateThenActivate,
            SettingsNavigation.Decide(windowOpen: false, page: null, rowId: null));
    }

    [Fact]
    public void InitialPagePrefersTheRequestedPageThenThePersistedOne()
    {
        Assert.Equal(
            SettingsPageId.Video,
            SettingsNavigation.InitialPage(SettingsPageId.Video, "General"));
        Assert.Equal(
            SettingsPageId.Audio,
            SettingsNavigation.InitialPage(null, "Audio"));
        Assert.Equal(
            SettingsPageId.General,
            SettingsNavigation.InitialPage(null, null));
    }

    [Fact]
    public void ParsePageIsCaseInsensitiveAndFallsBackToGeneral()
    {
        Assert.Equal(SettingsPageId.Video, SettingsNavigation.ParsePage("video"));
        Assert.Equal(SettingsPageId.Advanced, SettingsNavigation.ParsePage("Advanced"));
        Assert.Equal(SettingsPageId.General, SettingsNavigation.ParsePage("not-a-page"));
        Assert.Equal(SettingsPageId.General, SettingsNavigation.ParsePage(""));
        Assert.Equal(SettingsPageId.General, SettingsNavigation.ParsePage(null));
    }
}
