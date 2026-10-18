using System;
using System.Collections.Generic;
using System.Linq;
using Wiina.Core.Input;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class KeyBindingProfileTests
{
    [Fact]
    public void IinaProfileUsesOnlyKnownPlayerActions()
    {
        foreach (KeyBindingSetting binding in KeyBindings.CreateIinaDefaults())
        {
            Assert.True(
                PlayerActionCatalog.IsKnown(binding.Action),
                $"Unknown IINA action: {binding.Action}");
            Assert.False(string.IsNullOrWhiteSpace(binding.Chord));
        }
    }

    [Fact]
    public void IinaProfileHasNoDuplicateChords()
    {
        var duplicates = KeyBindings.CreateIinaDefaults()
            .GroupBy(binding => KeyBindings.NormalizeChord(binding.Chord), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("Space", PlayerActions.PlayPause)]
    [InlineData("Left", PlayerActions.SeekBack5)]
    [InlineData("Right", PlayerActions.SeekForward5)]
    [InlineData("Up", PlayerActions.VolumeUp5)]
    [InlineData("Down", PlayerActions.VolumeDown5)]
    [InlineData("F", PlayerActions.ToggleFullscreen)]
    [InlineData("M", PlayerActions.Mute)]
    [InlineData("S", PlayerActions.Screenshot)]
    [InlineData("O", PlayerActions.Open)]
    [InlineData("P", PlayerActions.PlaylistNext)]
    [InlineData("C", PlayerActions.ChapterNext)]
    [InlineData("V", PlayerActions.SubtitleVisibility)]
    [InlineData("A", PlayerActions.Mute)]
    [InlineData("[", PlayerActions.SpeedHalf)]
    [InlineData("]", PlayerActions.SpeedDouble)]
    [InlineData("J", PlayerActions.SubtitleDelayDecrease)]
    [InlineData("K", PlayerActions.SubtitleDelayIncrease)]
    public void IinaMapBindsExpectedChord(string chord, string expectedAction)
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Iina);

        Assert.True(map.TryGetAction(KeyChord.Parse(chord), out string? action));
        Assert.Equal(expectedAction, action);
    }

    [Fact]
    public void DefaultProfileKeepsExistingBindings()
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Default);

        Assert.True(map.TryGetAction(KeyChord.Parse("Space"), out string? action));
        Assert.Equal(PlayerActions.PlayPause, action);
        Assert.True(map.TryGetAction(KeyChord.Parse("F11"), out action));
        Assert.Equal(PlayerActions.ToggleFullscreen, action);
        Assert.True(map.TryGetAction(KeyChord.Parse("F"), out action));
        Assert.Equal(PlayerActions.SubtitleScaleDecrease, action);
        Assert.True(map.TryGetAction(KeyChord.Parse("Ctrl+,"), out action));
        Assert.Equal(PlayerActions.Settings, action);
    }

    [Fact]
    public void SwitchingProfileRebuildsDifferentMap()
    {
        KeyMap defaultMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Default);
        KeyMap iinaMap = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Iina);

        Assert.True(defaultMap.TryGetAction(KeyChord.Parse("P"), out string? defaultAction));
        Assert.True(iinaMap.TryGetAction(KeyChord.Parse("P"), out string? iinaAction));
        Assert.Equal(PlayerActions.PlayPause, defaultAction);
        Assert.Equal(PlayerActions.PlaylistNext, iinaAction);

        Assert.True(defaultMap.TryGetAction(KeyChord.Parse("F11"), out _));
        Assert.False(iinaMap.TryGetAction(KeyChord.Parse("F11"), out _));
    }

    [Fact]
    public void IinaProfileIgnoresStoredCustomBindings()
    {
        var stored = new List<KeyBindingSetting>
        {
            new(PlayerActions.PlayPause, "Ctrl+Space"),
        };

        KeyMap map = KeyBindings.BuildKeyMap(stored, KeyBindingProfiles.Iina);

        Assert.True(map.TryGetAction(KeyChord.Parse("Space"), out string? action));
        Assert.Equal(PlayerActions.PlayPause, action);
        Assert.False(map.TryGetAction(KeyChord.Parse("Ctrl+Space"), out _));
    }

    [Fact]
    public void CustomProfileRetainsEdits()
    {
        IReadOnlyList<KeyBindingSetting> edited = KeyBindings.SetBinding(
            KeyBindings.CreateIinaDefaults(),
            PlayerActions.PlayPause,
            "Ctrl+Space");

        KeyMap map = KeyBindings.BuildKeyMap(edited, KeyBindingProfiles.Custom);

        Assert.True(map.TryGetAction(KeyChord.Parse("Ctrl+Space"), out string? action));
        Assert.Equal(PlayerActions.PlayPause, action);
        Assert.False(map.TryGetAction(KeyChord.Parse("Space"), out _));
        Assert.Equal(KeyBindings.CreateIinaDefaults().Count, KeyBindings.Resolve(
            edited,
            KeyBindingProfiles.Custom).Count);
    }

    [Fact]
    public void CustomWithNoStoredBindingsUsesDefaultProfile()
    {
        KeyMap map = KeyBindings.BuildKeyMap(null, KeyBindingProfiles.Custom);

        Assert.True(map.TryGetAction(KeyChord.Parse("F11"), out string? action));
        Assert.Equal(PlayerActions.ToggleFullscreen, action);
    }

    [Fact]
    public void ProfileDefaultsSelectRequestedProfile()
    {
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.CreateProfileDefaults(KeyBindingProfiles.Default).Count);
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.CreateProfileDefaults(KeyBindingProfiles.Custom).Count);
        Assert.Equal(
            KeyBindings.CreateIinaDefaults().Count,
            KeyBindings.CreateProfileDefaults("iina").Count);
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.CreateProfileDefaults("unknown").Count);
    }

    [Theory]
    [InlineData("Default", KeyBindingProfiles.Default)]
    [InlineData("default", KeyBindingProfiles.Default)]
    [InlineData("IINA", KeyBindingProfiles.Iina)]
    [InlineData("iina", KeyBindingProfiles.Iina)]
    [InlineData("Custom", KeyBindingProfiles.Custom)]
    [InlineData("custom", KeyBindingProfiles.Custom)]
    [InlineData("  iina  ", KeyBindingProfiles.Iina)]
    public void ProfileNamesNormalize(string value, string expected)
    {
        Assert.Equal(expected, KeyBindingProfiles.Normalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void UnknownProfileFallsBackToDefault(string? value)
    {
        Assert.Equal(KeyBindingProfiles.Default, KeyBindingProfiles.Normalize(value));
        Assert.False(KeyBindingProfiles.IsKnown(value));
    }

    [Fact]
    public void ProfileLabelsAreLocalized()
    {
        Assert.Equal("IINA", KeyBindingProfiles.Label(KeyBindingProfiles.Iina));
        Assert.Equal("IINA", KeyBindingProfiles.LabelEnglish(KeyBindingProfiles.Iina));
        Assert.False(string.IsNullOrWhiteSpace(KeyBindingProfiles.Label(KeyBindingProfiles.Custom)));
        Assert.Equal(3, KeyBindingProfiles.All.Count);
    }
}
