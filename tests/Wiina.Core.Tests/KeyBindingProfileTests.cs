using System;
using System.Collections.Generic;
using System.Linq;
using Wiina.Core.Input;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class KeyBindingProfileTests
{
    [Fact]
    public void DefaultProfileIsTheWiinaProfile()
    {
        Assert.Equal(KeyBindingProfiles.Wiina, KeyBindingProfiles.Default);
        Assert.Equal(3, KeyBindingProfiles.All.Count);
        Assert.Contains(KeyBindingProfiles.Wiina, KeyBindingProfiles.All);
        Assert.Contains(KeyBindingProfiles.Mpv, KeyBindingProfiles.All);
        Assert.Contains(KeyBindingProfiles.Iina, KeyBindingProfiles.All);
    }

    [Theory]
    [InlineData(KeyBindingProfiles.Wiina)]
    [InlineData(KeyBindingProfiles.Mpv)]
    [InlineData(KeyBindingProfiles.Iina)]
    public void BuiltInProfilesUseOnlyKnownPlayerActions(string profile)
    {
        foreach (KeyBindingSetting binding in KeyBindings.CreateProfileDefaults(profile))
        {
            Assert.True(
                PlayerActionCatalog.IsKnown(binding.Action),
                $"Unknown action in {profile}: {binding.Action}");
            Assert.False(string.IsNullOrWhiteSpace(binding.Chord));
        }
    }

    [Theory]
    [InlineData(KeyBindingProfiles.Wiina)]
    [InlineData(KeyBindingProfiles.Mpv)]
    [InlineData(KeyBindingProfiles.Iina)]
    public void BuiltInProfilesHaveNoDuplicateChords(string profile)
    {
        var duplicates = KeyBindings.CreateProfileDefaults(profile)
            .GroupBy(
                binding => KeyBindings.NormalizeChord(binding.Chord),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void WiinaProfileComesFromTheShippedConfFile()
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Wiina);

        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "Space"));
        Assert.Equal(PlayerActions.Screenshot, ActionFor(map, "Ctrl+S"));
        Assert.Equal(PlayerActions.LoopMode, ActionFor(map, "Ctrl+Shift+L"));
        Assert.Equal(PlayerActions.ChapterNext, ActionFor(map, "Ctrl+Shift+."));
        Assert.Equal(PlayerActions.SeekForward600, ActionFor(map, "Shift+PageUp"));
        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "MediaPlayPause"));
        Assert.Equal(PlayerActions.Quit, ActionFor(map, "MediaPower"));
        Assert.Equal(PlayerActions.ToggleFullscreen, ActionFor(map, "F"));
        Assert.Equal(PlayerActions.SubtitleScaleDecrease, ActionFor(map, "Shift+F"));
        Assert.False(map.TryGetAction(KeyChord.Parse("F11"), out _));
    }

    [Fact]
    public void IinaProfileUsesCorrectedMappings()
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Iina);

        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "Space"));
        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "P"));
        Assert.Equal(PlayerActions.Screenshot, ActionFor(map, "Ctrl+S"));
        Assert.Equal(PlayerActions.SubtitleSeekPrevious, ActionFor(map, "Shift+Left"));
        Assert.Equal(PlayerActions.SubtitleSeekNext, ActionFor(map, "Shift+Right"));
        Assert.Equal(PlayerActions.SubtitleDelayReset, ActionFor(map, "C"));
        Assert.Equal(PlayerActions.SubtitleDelayDecrease, ActionFor(map, "Z"));
        Assert.Equal(PlayerActions.SubtitleDelayIncrease, ActionFor(map, "X"));
        Assert.Equal(PlayerActions.ToggleTopmost, ActionFor(map, "Ctrl+T"));
        Assert.Equal(PlayerActions.ToggleFullscreen, ActionFor(map, "F"));

        // IINA did not bind A; it must not silently mute.
        Assert.False(map.TryGetAction(KeyChord.Parse("A"), out _));
    }

    [Fact]
    public void SwitchingProfileRebuildsDifferentMap()
    {
        KeyMap wiinaMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Wiina);
        KeyMap iinaMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Iina);

        Assert.Equal(PlayerActions.PlayPause, ActionFor(wiinaMap, "P"));
        Assert.Equal(PlayerActions.PlayPause, ActionFor(iinaMap, "P"));

        Assert.Equal(PlayerActions.Settings, ActionFor(iinaMap, "Ctrl+,"));
        Assert.False(wiinaMap.TryGetAction(KeyChord.Parse("Ctrl+,"), out _));
    }

    [Fact]
    public void IinaProfileIgnoresStoredCustomBindings()
    {
        var stored = new List<KeyBindingSetting>
        {
            new(PlayerActions.PlayPause, "Ctrl+Space"),
        };

        KeyMap map = KeyBindings.BuildKeyMap(stored, KeyBindingProfiles.Iina);

        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "Space"));
        Assert.False(map.TryGetAction(KeyChord.Parse("Ctrl+Space"), out _));
    }

    [Fact]
    public void CustomProfileRetainsEdits()
    {
        IReadOnlyList<KeyBindingSetting> edited = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.PlayPause,
            "Ctrl+Space");

        KeyMap map = KeyBindings.BuildKeyMap(edited, "我的方案");

        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "Ctrl+Space"));
        Assert.False(map.TryGetAction(KeyChord.Parse("Space"), out _));
        Assert.Equal(edited.Count, KeyBindings.Resolve(edited, "我的方案").Count);
    }

    [Fact]
    public void CustomWithNoStoredBindingsUsesTheWiinaProfile()
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, "我的方案");

        Assert.Equal(PlayerActions.PlayPause, ActionFor(map, "MediaPlayPause"));
        Assert.Equal(PlayerActions.Screenshot, ActionFor(map, "Ctrl+S"));
    }

    [Fact]
    public void ProfileDefaultsSelectRequestedProfile()
    {
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.CreateProfileDefaults(KeyBindingProfiles.Wiina).Count);
        Assert.Equal(
            KeyBindings.CreateMpvDefaults().Count,
            KeyBindings.CreateProfileDefaults(KeyBindingProfiles.Mpv).Count);
        Assert.Equal(
            KeyBindings.CreateIinaDefaults().Count,
            KeyBindings.CreateProfileDefaults("iina").Count);
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.CreateProfileDefaults("unknown").Count);
    }

    [Theory]
    [InlineData("Default", KeyBindingProfiles.Wiina)]
    [InlineData("default", KeyBindingProfiles.Wiina)]
    [InlineData("默认", KeyBindingProfiles.Wiina)]
    [InlineData("Wiina 默认", KeyBindingProfiles.Wiina)]
    [InlineData("mpv", KeyBindingProfiles.Mpv)]
    [InlineData("mpv 默认", KeyBindingProfiles.Mpv)]
    [InlineData("IINA", KeyBindingProfiles.Iina)]
    [InlineData("iina", KeyBindingProfiles.Iina)]
    [InlineData("  iina  ", KeyBindingProfiles.Iina)]
    public void ProfileNamesNormalize(string value, string expected)
    {
        Assert.Equal(expected, KeyBindingProfiles.Normalize(value));
    }

    [Fact]
    public void LegacyCustomProfileNamesArePreserved()
    {
        // Older builds stored user profiles literally called "自定义"; they must
        // remain selectable instead of collapsing into a virtual profile id.
        Assert.Equal("自定义", KeyBindingProfiles.Normalize("自定义"));
        Assert.Equal("自定义 2", KeyBindingProfiles.Normalize("  自定义 2  "));
        Assert.Equal("garbage", KeyBindingProfiles.Normalize("  garbage  "));
        Assert.True(KeyBindingProfiles.IsKnown("garbage"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyProfileFallsBackToDefault(string? value)
    {
        Assert.Equal(KeyBindingProfiles.Default, KeyBindingProfiles.Normalize(value));
    }

    [Fact]
    public void ProfileLabelsAreLocalized()
    {
        Assert.Equal("Wiina 默认", KeyBindingProfiles.Label(KeyBindingProfiles.Wiina));
        Assert.Equal("mpv 默认", KeyBindingProfiles.Label(KeyBindingProfiles.Mpv));
        Assert.Equal("IINA", KeyBindingProfiles.Label(KeyBindingProfiles.Iina));
        Assert.Equal("Wiina Default", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Wiina));
        Assert.Equal("mpv Default", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Mpv));
        Assert.Equal("IINA", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Iina));
        Assert.False(string.IsNullOrWhiteSpace(KeyBindingProfiles.Label("我的方案")));
    }

    private static string? ActionFor(KeyMap map, string chordText)
    {
        Assert.True(KeyChord.TryParse(chordText, out KeyChord chord), chordText);
        Assert.True(map.TryGetAction(chord, out string? action), chordText);
        return action;
    }
}
