using System;
using System.Linq;
using Wiina.Core.Input;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Locks the shipped "Wiina 默认" profile to the embedded wiina-defaults.conf.
/// </summary>
public sealed class WiinaInputDefaultsTests
{
    [Fact]
    public void ShippedProfileParsesEveryBinding()
    {
        IReadOnlyList<KeyBindingSetting> bindings = WiinaInputDefaults.Create();

        // 82 binding lines; PLAY/PAUSE/PLAYPAUSE/PLAYONLY/PAUSEONLY collapse onto
        // the single Windows media play/pause key, leaving 78 unique chords.
        Assert.Equal(78, bindings.Count);
        Assert.All(bindings, binding => Assert.True(
            PlayerActionCatalog.IsKnown(binding.Action),
            binding.Action));
        Assert.All(bindings, binding => Assert.NotNull(
            KeyBindings.NormalizeChord(binding.Chord)));
    }

    [Fact]
    public void MediaPlayPauseLinesCollapseToTheFirstBinding()
    {
        IReadOnlyList<KeyBindingSetting> bindings = WiinaInputDefaults.Create();

        KeyBindingSetting single = Assert.Single(bindings, binding =>
            binding.Chord == KeyChord.Parse("MediaPlayPause").ToString());
        Assert.Equal(PlayerActions.PlayPause, single.Action);
    }

    [Theory]
    [InlineData("Shift+Alt+V", PlayerActions.VideoPanel)]
    [InlineData("Shift+Alt+A", PlayerActions.AudioPanel)]
    [InlineData("Shift+Alt+S", PlayerActions.SubtitlesPanel)]
    [InlineData("Shift+Alt+P", PlayerActions.PlaylistPanel)]
    [InlineData("Shift+Alt+C", PlayerActions.ChapterPanel)]
    [InlineData("Ctrl+T", PlayerActions.ToggleTopmost)]
    [InlineData("Ctrl+/", PlayerActions.Mute)]
    [InlineData("Ctrl+Shift+.", PlayerActions.ChapterNext)]
    [InlineData("Ctrl+Shift+,", PlayerActions.ChapterPrevious)]
    [InlineData("Ctrl+Shift+L", PlayerActions.LoopMode)]
    [InlineData("Shift+PageUp", PlayerActions.SeekForward600)]
    [InlineData("Shift+PageDown", PlayerActions.SeekBack600)]
    [InlineData("MediaPlayPause", PlayerActions.PlayPause)]
    [InlineData("MediaPower", PlayerActions.Quit)]
    [InlineData("MediaVolumeUp", PlayerActions.VolumeUp2)]
    [InlineData("Ctrl+Alt+V", PlayerActions.CycleVideo)]
    [InlineData("Shift+9", PlayerActions.AudioDelayIncrease05)]
    [InlineData("Shift+0", PlayerActions.AudioDelayDecrease05)]
    [InlineData("Shift+-", PlayerActions.AudioDelayReset)]
    [InlineData("Ctrl+\\", PlayerActions.SpeedReset)]
    [InlineData("Alt+Ctrl+[", PlayerActions.SpeedSlower)]
    [InlineData("Alt+Ctrl+]", PlayerActions.SpeedFaster)]
    public void RepresentativeChordsMapToActions(string chordText, string expectedAction)
    {
        Assert.True(KeyChord.TryParse(chordText, out KeyChord chord), chordText);
        KeyBindingSetting binding = Assert.Single(
            WiinaInputDefaults.Create(),
            item => item.Chord == chord.ToString());
        Assert.Equal(expectedAction, binding.Action);
    }

    [Fact]
    public void MpvParsingImpliesShiftForUppercaseLettersAndShiftedPunctuation()
    {
        Assert.True(KeyChord.TryParseMpv("P", out KeyChord uppercase));
        Assert.Equal(KeyChord.Parse("Shift+P"), uppercase);

        Assert.True(KeyChord.TryParseMpv("Shift+Ctrl+>", out KeyChord chapter));
        Assert.Equal(KeyChord.Parse("Ctrl+Shift+."), chapter);

        Assert.True(KeyChord.TryParseMpv("Shift+(", out KeyChord delay));
        Assert.Equal(KeyChord.Parse("Shift+9"), delay);

        Assert.True(KeyChord.TryParseMpv("Shift+_", out KeyChord reset));
        Assert.Equal(KeyChord.Parse("Shift+-"), reset);

        // Generic parsing keeps the previous behaviour (no implied Shift).
        Assert.Equal(
            KeyChord.Parse("P"),
            KeyChord.Parse(KeyChord.Parse("P").ToString()));
    }

    [Fact]
    public void UnknownCommandsArePreservedAsRawMpvActions()
    {
        var parsed = WiinaInputDefaults.Parse("x cycle stats\nCtrl+F9 cycle-values loop \"inf\" \"no\"");

        Assert.Equal(2, parsed.Count);
        Assert.True(PlayerActionCatalog.IsKnown(parsed[0].Action));
        Assert.Equal("mpv:cycle stats", parsed[0].Action);
        Assert.Equal(PlayerActions.LoopMode, parsed[1].Action);
    }

    [Fact]
    public void CommentsAndInvalidLinesAreIgnored()
    {
        var parsed = WiinaInputDefaults.Parse(
            "# comment\n\nCtrl+Q quit   # trailing comment\nnot-a-chord\nSPACE cycle pause\n");

        Assert.Equal(2, parsed.Count);
        Assert.Equal(PlayerActions.Quit, parsed[0].Action);
        Assert.Equal(PlayerActions.PlayPause, parsed[1].Action);
    }

    [Fact]
    public void EmbeddedConfIsReadable()
    {
        string conf = WiinaInputDefaults.ReadConf();
        Assert.Contains("Shift+Alt+v", conf, StringComparison.Ordinal);
    }
}
