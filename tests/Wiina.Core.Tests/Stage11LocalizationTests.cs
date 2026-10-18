using System.Text.RegularExpressions;
using Wiina.Core.Localization;
using Wiina.Core.Menu;
using Wiina.Core.Osd;
using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Stage 11 localization contract tests: the shipped catalog must have a full
/// English overlay, the resolver must be deterministic, and the menu / settings
/// / OSD projections must never leak untranslated Chinese into English.
/// </summary>
public class Stage11LocalizationTests
{
    private static readonly Regex Cjk = new(@"[\u3400-\u9fff\uf900-\ufaff]", RegexOptions.Compiled);

    [Fact]
    public void Catalog_has_no_missing_english_and_every_value_is_populated()
    {
        Assert.Empty(UiStrings.MissingEnglishKeys);
        Assert.True(UiStrings.Keys.Count >= 500, $"catalog has only {UiStrings.Keys.Count} keys");
        foreach ((string key, string zh, string en) in UiStrings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.False(string.IsNullOrWhiteSpace(zh));
            Assert.False(string.IsNullOrWhiteSpace(en));
        }
    }

    [Fact]
    public void Catalog_english_never_contains_untranslated_chinese()
    {
        foreach ((string key, _, string en) in UiStrings.All)
        {
            Assert.DoesNotMatch(Cjk, en);
        }
    }

    [Fact]
    public void Catalog_keys_resolve_in_both_languages_and_are_unique()
    {
        var keys = UiStrings.Keys;
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (string key in keys)
        {
            Assert.True(UiStrings.Contains(key));
            Assert.NotEqual(UiStrings.Get(key, AppLanguage.ZhHans), string.Empty);
            Assert.NotEqual(UiStrings.Get(key, AppLanguage.English), string.Empty);
        }

        Assert.Throws<KeyNotFoundException>(() => UiStrings.Get("missing.key"));
    }

    [Fact]
    public void Formatted_strings_use_invariant_culture()
    {
        string zh = UiStrings.Format("osd.volume", AppLanguage.ZhHans, 42.5);
        string en = UiStrings.Format("osd.volume", AppLanguage.English, 42.5);
        Assert.Contains("42.5", zh);
        Assert.Contains("42.5", en);
    }

    [Fact]
    public void Settings_catalog_has_english_overlay_for_every_page_row_section_and_choice()
    {
        foreach (SettingsPageInfo page in SettingsCatalog.Pages)
        {
            Assert.True(SettingsLocalization.HasPage(page.Id.ToString()), $"page '{page.Id}' has no overlay");
            Assert.False(string.IsNullOrWhiteSpace(SettingsLocalization.PageTitle(page, AppLanguage.English)));
            Assert.False(string.IsNullOrWhiteSpace(SettingsLocalization.PageSummary(page, AppLanguage.English)));
        }

        var sections = new HashSet<string>(StringComparer.Ordinal);
        var choiceProblems = new List<string>();
        foreach (SettingsRowDefinition row in SettingsCatalog.Rows)
        {
            Assert.True(SettingsLocalization.HasRow(row.Id), $"row '{row.Id}' has no English overlay");
            string title = SettingsLocalization.Title(row, AppLanguage.English);
            string description = SettingsLocalization.Description(row, AppLanguage.English);
            Assert.False(string.IsNullOrWhiteSpace(title), $"row '{row.Id}' has an empty English title");
            Assert.DoesNotMatch(Cjk, title);
            Assert.DoesNotMatch(Cjk, description);
            sections.Add(row.Section);

            IReadOnlyList<string> choices = SettingsLocalization.Choices(row, AppLanguage.English);
            Assert.Equal(row.Choices.Count, choices.Count);
            foreach (string choice in choices)
            {
                if (Cjk.IsMatch(choice)) choiceProblems.Add($"{row.Id}: {choice}");
            }
        }

        Assert.True(choiceProblems.Count == 0, string.Join("; ", choiceProblems));

        foreach (string section in sections)
        {
            Assert.True(SettingsLocalization.HasSection(section), $"section '{section}' has no English overlay");
            string english = SettingsLocalization.Section(section, AppLanguage.English);
            Assert.False(string.IsNullOrWhiteSpace(english));
            Assert.DoesNotMatch(Cjk, english);
        }
    }

    [Fact]
    public void Menu_catalog_has_english_label_for_every_static_item()
    {
        var state = new MenuState
        {
            HasMedia = true,
            HasVideo = true,
            HasAudioTrack = true,
            HasSubtitleTrack = true,
            PlaylistCount = 3,
            IsPlaying = true,
        };

        IReadOnlyList<MenuGroupDefinition> zh = MenuCatalog.Build(state, AppLanguage.ZhHans);
        IReadOnlyList<MenuGroupDefinition> en = MenuCatalog.Build(state, AppLanguage.English);
        Assert.Equal(zh.Count, en.Count);
        for (int i = 0; i < zh.Count; i++)
        {
            Assert.Equal(zh[i].Items.Count, en[i].Items.Count);
            Assert.DoesNotMatch(Cjk, en[i].Title);
            for (int j = 0; j < zh[i].Items.Count; j++)
            {
                string english = en[i].Items[j].Label;
                if (string.IsNullOrEmpty(zh[i].Items[j].Label)) continue;
                Assert.True(!string.IsNullOrWhiteSpace(english), $"menu item {i},{j} empty; zh='{zh[i].Items[j].Label}'");
                Assert.DoesNotMatch(Cjk, english);
            }
        }
    }

    [Fact]
    public void Osd_builders_switch_language()
    {
        Assert.Contains("Volume", OsdMessages.Volume(50, AppLanguage.English).Text);
        Assert.DoesNotMatch(Cjk, OsdMessages.Volume(50, AppLanguage.English).Text);
        Assert.Contains("音量", OsdMessages.Volume(50, AppLanguage.ZhHans).Text);
        Assert.Contains("Mute", OsdMessages.Mute(true, AppLanguage.English).Text);
        Assert.Contains("Speed", OsdMessages.Speed(1.25, AppLanguage.English).Text);
        Assert.Contains("audio", OsdMessages.Track("音频", "English", AppLanguage.English).Text);
        Assert.Contains("Paused", OsdMessages.Pause(true, AppLanguage.English).Text);
        Assert.Contains("Buffering", OsdMessages.Buffer(language: AppLanguage.English).Text);
        Assert.Contains("Opening", OsdMessages.NetworkOpening(language: AppLanguage.English).Text);
        Assert.Contains("Seeking", OsdMessages.NetworkSeeking(AppLanguage.English).Text);
    }

    [Fact]
    public void Language_resolver_follows_preference_then_system_then_english()
    {
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve("en"));
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve("en-GB"));
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve("zh-Hans"));
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve("zh-CN"));
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve("system", new[] { "zh-CN", "en-US" }));
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve("system", new[] { "de-DE", "en-US" }));
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve("system", new[] { "de-DE", "fr-FR" }));
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve("de-DE", new[] { "en-US" }));
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve(null));
        Assert.Equal(AppLanguage.ZhHans, AppLanguage.Resolve(string.Empty));
    }

    [Fact]
    public void Language_choice_indices_round_trip_and_validate()
    {
        Assert.Equal(0, AppLanguage.ToChoiceIndex("system"));
        Assert.Equal(1, AppLanguage.ToChoiceIndex("zh-Hans"));
        Assert.Equal(2, AppLanguage.ToChoiceIndex("en"));
        Assert.Equal("system", AppLanguage.FromChoiceIndex(0));
        Assert.Equal("zh-Hans", AppLanguage.FromChoiceIndex(1));
        Assert.Equal("en", AppLanguage.FromChoiceIndex(2));
        Assert.Equal("system", AppLanguage.FromChoiceIndex(99));
        Assert.True(AppLanguage.IsSupportedPreference("zh-Hans"));
        Assert.False(AppLanguage.IsSupportedPreference("de-DE"));
        Assert.True(AppLanguage.IsRightToLeft("ar-SA"));
        Assert.False(AppLanguage.IsRightToLeft("zh-Hans"));
    }

    [Fact]
    public void Settings_search_hits_are_localized()
    {
        var hit = SettingsCatalog.SearchIndex.Search("language", 5, AppLanguage.English).FirstOrDefault();
        Assert.NotNull(hit);
        Assert.DoesNotMatch(Cjk, hit!.Title);
    }
}





