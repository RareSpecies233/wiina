using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class SettingsSearchTests
{
    [Fact]
    public void CatalogHasTwelvePagesInSpecOrder()
    {
        Assert.Equal(12, SettingsCatalog.Pages.Count);
        Assert.Equal(
            new[]
            {
                SettingsPageId.General,
                SettingsPageId.Ui,
                SettingsPageId.Video,
                SettingsPageId.Audio,
                SettingsPageId.Subtitles,
                SettingsPageId.Network,
                SettingsPageId.Control,
                SettingsPageId.KeyBindings,
                SettingsPageId.Advanced,
                SettingsPageId.Integration,
                SettingsPageId.Plugin,
                SettingsPageId.Utilities,
            },
            SettingsCatalog.Pages.Select(page => page.Id));
    }

    [Fact]
    public void EveryPageHasRowsAndSections()
    {
        foreach (SettingsPageInfo page in SettingsCatalog.Pages)
        {
            Assert.NotEmpty(SettingsCatalog.Sections(page.Id));
            Assert.NotEmpty(SettingsCatalog.RowsFor(page.Id));
            Assert.False(string.IsNullOrWhiteSpace(page.Title));
            Assert.False(string.IsNullOrWhiteSpace(page.Glyph));
        }
    }

    [Fact]
    public void RowIdsAreUniqueAndAddressable()
    {
        var ids = SettingsCatalog.Rows.Select(row => row.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (string id in ids)
        {
            Assert.NotNull(SettingsCatalog.Find(id));
        }

        Assert.Null(SettingsCatalog.Find("does-not-exist"));
    }

    [Fact]
    public void SearchIsEmptyForBlankQuery()
    {
        Assert.Empty(SettingsCatalog.SearchIndex.Search(""));
        Assert.Empty(SettingsCatalog.SearchIndex.Search("   "));
        Assert.Empty(SettingsCatalog.SearchIndex.Search(null));
    }

    [Fact]
    public void SearchRanksExactTitleFirst()
    {
        IReadOnlyList<SettingsSearchHit> hits = SettingsCatalog.SearchIndex.Search("主题");
        Assert.NotEmpty(hits);
        Assert.Equal("general.theme", hits[0].Row?.Id);
    }

    [Fact]
    public void SearchFindsEnglishKeywords()
    {
        IReadOnlyList<SettingsSearchHit> hits =
            SettingsCatalog.SearchIndex.Search("thumbnail");
        Assert.Contains(hits, hit => hit.Row?.Id == "ui.thumbnail-preview");
        Assert.Contains(hits, hit => hit.Row?.Id == "ui.thumbnail-width");
    }

    [Fact]
    public void SearchFindsDescriptionTerms()
    {
        IReadOnlyList<SettingsSearchHit> hits =
            SettingsCatalog.SearchIndex.Search("断点续播");
        Assert.Contains(hits, hit => hit.Row?.Id == "general.resume");
    }

    [Fact]
    public void SearchMatchesPageWhenNoRowMatches()
    {
        IReadOnlyList<SettingsSearchHit> hits =
            SettingsCatalog.SearchIndex.Search("工具");
        Assert.Contains(hits, hit => hit.Page.Id == SettingsPageId.Utilities);
    }

    [Fact]
    public void SearchIsCaseInsensitiveAndLimited()
    {
        IReadOnlyList<SettingsSearchHit> upper =
            SettingsCatalog.SearchIndex.Search("OSD", 3);
        Assert.True(upper.Count <= 3);
        Assert.All(upper, hit => Assert.Equal(SettingsPageId.Ui, hit.Page.Id));
        Assert.NotEmpty(SettingsCatalog.SearchIndex.Search("osd"));
    }

    [Fact]
    public void SearchPrefersTitleOverDescription()
    {
        var page = new SettingsPageInfo(
            SettingsPageId.General, "通用", "summary", "\uE713");
        var titleRow = new SettingsRowDefinition
        {
            Id = "test.title",
            Page = SettingsPageId.General,
            Section = "测试",
            Title = "缓存路径",
            Get = settings => string.Empty,
        };
        var descriptionRow = new SettingsRowDefinition
        {
            Id = "test.description",
            Page = SettingsPageId.General,
            Section = "测试",
            Title = "其他",
            Description = "这里也提到缓存",
            Get = settings => string.Empty,
        };
        var index = new SettingsSearchIndex(new[] { page }, new[] { descriptionRow, titleRow });
        IReadOnlyList<SettingsSearchHit> hits = index.Search("缓存");
        Assert.Equal(2, hits.Count);
        Assert.Equal("test.title", hits[0].Row?.Id);
    }

    [Fact]
    public void SearchLimitIsHonoured()
    {
        IReadOnlyList<SettingsSearchHit> hits =
            SettingsCatalog.SearchIndex.Search("字幕", 2);
        Assert.True(hits.Count <= 2);
    }

    [Fact]
    public void ToggleAndChoiceAndNumberRowsRoundTrip()
    {
        foreach (SettingsRowDefinition row in SettingsCatalog.Rows)
        {
            if (row.Set is null)
            {
                continue;
            }

            var settings = new AppSettings();
            switch (row.Kind)
            {
                case SettingsRowKind.Toggle:
                    bool flag = row.Value(settings) is true;
                    row.Apply(settings, !flag);
                    Assert.Equal(!flag, row.Value(settings));
                    break;
                case SettingsRowKind.Choice:
                    Assert.NotEmpty(row.Choices);
                    int choice = Math.Min(
                        row.Choices.Count - 1,
                        ((int)(row.Value(settings) ?? 0) + 1) % row.Choices.Count);
                    row.Apply(settings, choice);
                    Assert.Equal(choice, Convert.ToInt32(row.Value(settings)));
                    break;
                case SettingsRowKind.Number:
                    double number = Convert.ToDouble(row.Value(settings));
                    double target = Math.Abs(row.Minimum - number) < 0.0001
                        ? row.Maximum
                        : row.Minimum;
                    row.Apply(settings, target);
                    Assert.Equal(
                        Math.Clamp(target, row.Minimum, row.Maximum),
                        Convert.ToDouble(row.Value(settings)),
                        3);
                    break;
                case SettingsRowKind.Text:
                case SettingsRowKind.Font:
                    row.Apply(settings, "Sample Family");
                    Assert.Equal("Sample Family", row.Value(settings));
                    break;
            }
        }
    }

    [Fact]
    public void NumberRowsHaveValidBounds()
    {
        foreach (SettingsRowDefinition row in SettingsCatalog.Rows
                     .Where(row => row.Kind == SettingsRowKind.Number))
        {
            Assert.True(row.Maximum > row.Minimum, row.Id);
            Assert.True(row.Step > 0, row.Id);
        }
    }

    [Fact]
    public void ActionRowsDeclareHandlers()
    {
        foreach (SettingsRowDefinition row in SettingsCatalog.Rows
                     .Where(row => row.Kind == SettingsRowKind.Action))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.ActionId), row.Id);
        }

        var unavailable = SettingsCatalog.Rows
            .Where(row => !row.IsAvailable)
            .ToArray();
        Assert.NotEmpty(unavailable);
        Assert.All(unavailable, row => Assert.False(
            string.IsNullOrWhiteSpace(row.UnavailableReason)));
    }

    [Fact]
    public void KeyBindingsPageHasEditorAndPluginPageIsDisabled()
    {
        Assert.Equal(
            SettingsRowKind.KeyBindings,
            SettingsCatalog.RowsFor(SettingsPageId.KeyBindings).Single().Kind);

        SettingsRowDefinition install = Assert.Single(
            SettingsCatalog.Rows,
            row => row.Id == "plugin.install");
        Assert.False(install.IsAvailable);
        Assert.Contains("Stage 9", install.UnavailableReason);

        SettingsRowDefinition list = Assert.Single(
            SettingsCatalog.Rows,
            row => row.Id == "plugin.list");
        Assert.Equal(SettingsRowKind.PluginList, list.Kind);
    }

    [Fact]
    public void AdvancedPageExposesMaintenanceActions()
    {
        string[] expected =
        {
            "reset-all-settings",
            "open-config-folder",
            "open-log-folder",
            "export-diagnostics",
            "clear-history",
            "clear-thumbnails",
        };
        var actions = SettingsCatalog.Rows
            .Where(row => row.Page == SettingsPageId.Advanced)
            .Select(row => row.ActionId)
            .Where(id => id is not null)
            .ToArray();
        foreach (string id in expected)
        {
            Assert.Contains(id, actions);
        }
    }
}
