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
        Assert.Equal(2, KeyBindingProfiles.All.Count);
        Assert.Equal(
            new[] { KeyBindingProfiles.Wiina, KeyBindingProfiles.Mpv },
            KeyBindingProfiles.All);
        Assert.DoesNotContain("IINA", KeyBindingProfiles.All);
    }

    [Theory]
    [InlineData("IINA")]
    [InlineData("iina")]
    [InlineData("  iina  ")]
    [InlineData("IINA 默认")]
    public void RemovedIinaProfileMigratesToWiina(string persisted)
    {
        Assert.Equal(KeyBindingProfiles.Wiina, KeyBindingProfiles.Normalize(persisted));
        Assert.Equal("Wiina 默认", KeyBindingProfiles.Label(persisted));
    }

    [Theory]
    [InlineData(KeyBindingProfiles.Wiina)]
    [InlineData(KeyBindingProfiles.Mpv)]
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
        Assert.Equal(PlayerActions.Settings, ActionFor(map, "Ctrl+,"));
        Assert.False(map.TryGetAction(KeyChord.Parse("F11"), out _));
    }

    [Fact]
    public void SwitchingProfileRebuildsDifferentMap()
    {
        KeyMap wiinaMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Wiina);
        KeyMap mpvMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Mpv);

        Assert.Equal(PlayerActions.PlayPause, ActionFor(wiinaMap, "P"));
        Assert.Equal(PlayerActions.PlayPause, ActionFor(mpvMap, "P"));

        // Ctrl+, opens Settings only in the Wiina profile.
        Assert.Equal(PlayerActions.Settings, ActionFor(wiinaMap, "Ctrl+,"));
        Assert.False(mpvMap.TryGetAction(KeyChord.Parse("Ctrl+,"), out _));

        // Ctrl+0 is a Wiina window-scale binding that mpv's stock profile lacks.
        Assert.Equal(PlayerActions.WindowHalf, ActionFor(wiinaMap, "Ctrl+0"));
        Assert.False(mpvMap.TryGetAction(KeyChord.Parse("Ctrl+0"), out _));
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
            KeyBindings.CreateDefaults().Count,
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
    [InlineData("IINA", KeyBindingProfiles.Wiina)]
    [InlineData("iina", KeyBindingProfiles.Wiina)]
    [InlineData("  iina  ", KeyBindingProfiles.Wiina)]
    [InlineData("IINA 默认", KeyBindingProfiles.Wiina)]
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
        Assert.Equal("mpv", KeyBindingProfiles.Label(KeyBindingProfiles.Mpv));
        Assert.Equal("Wiina Default", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Wiina));
        Assert.Equal("mpv", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Mpv));
        Assert.DoesNotContain("默认", KeyBindingProfiles.Label(KeyBindingProfiles.Mpv));
        Assert.DoesNotContain("Default", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Mpv));
        Assert.False(string.IsNullOrWhiteSpace(KeyBindingProfiles.Label("我的方案")));
    }

    private static string? ActionFor(KeyMap map, string chordText)
    {
        Assert.True(KeyChord.TryParse(chordText, out KeyChord chord), chordText);
        Assert.True(map.TryGetAction(chord, out string? action), chordText);
        return action;
    }
}
