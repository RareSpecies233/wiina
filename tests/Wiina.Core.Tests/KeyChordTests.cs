using Xunit;
using Wiina.Core.Input;

namespace Wiina.Core.Tests;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("Space", AppKey.Space, KeyModifiers.None)]
    [InlineData("P", AppKey.P, KeyModifiers.None)]
    [InlineData("Ctrl+Shift+O", AppKey.O, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("Ctrl+,", AppKey.Comma, KeyModifiers.Control)]
    [InlineData("Alt+Left", AppKey.Left, KeyModifiers.Alt)]
    [InlineData("Shift+Right", AppKey.Right, KeyModifiers.Shift)]
    [InlineData("Ctrl+Plus", AppKey.Plus, KeyModifiers.Control)]
    [InlineData("Ctrl+PageDown", AppKey.PageDown, KeyModifiers.Control)]
    [InlineData("F11", AppKey.F11, KeyModifiers.None)]
    [InlineData("Esc", AppKey.Escape, KeyModifiers.None)]
    [InlineData("Ctrl+0", AppKey.D0, KeyModifiers.Control)]
    [InlineData("Ctrl+Backspace", AppKey.Backspace, KeyModifiers.Control)]
    public void ParseRecognizesDefaultBindings(
        string text,
        AppKey expectedKey,
        KeyModifiers expectedModifiers)
    {
        Assert.True(KeyChord.TryParse(text, out KeyChord chord));
        Assert.Equal(expectedKey, chord.Key);
        Assert.Equal(expectedModifiers, chord.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+Shift+O", "Ctrl+Shift+O")]
    [InlineData("shift+ctrl+o", "Ctrl+Shift+O")]
    [InlineData("Ctrl+,", "Ctrl+,")]
    [InlineData("Alt+Left", "Alt+Left")]
    [InlineData("Ctrl+Plus", "Ctrl+Plus")]
    [InlineData("Win+F12", "Win+F12")]
    [InlineData("Space", "Space")]
    public void FormatRoundTripsToCanonicalText(string input, string expected)
    {
        Assert.True(KeyChord.TryParse(input, out KeyChord chord));
        Assert.Equal(expected, chord.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+O")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData(null)]
    public void InvalidTextIsRejected(string? text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Fact]
    public void ParseThrowsForInvalidText()
    {
        Assert.Throws<FormatException>(() => KeyChord.Parse("Ctrl+Nope"));
    }

    [Fact]
    public void DefaultMapContainsAllRequiredActions()
    {
        KeyMap map = KeyMap.CreateDefault();
        string[] actions = map.Bindings.Values.ToArray();

        foreach (string required in new[]
                 {
                     PlayerActions.PlayPause,
                     PlayerActions.Stop,
                     PlayerActions.SeekBack5,
                     PlayerActions.SeekForward5,
                     PlayerActions.SeekBack15,
                     PlayerActions.SeekForward15,
                     PlayerActions.SeekBack600,
                     PlayerActions.SeekForward600,
                     PlayerActions.VolumeUp5,
                     PlayerActions.VolumeDown5,
                     PlayerActions.VolumeUp1,
                     PlayerActions.VolumeDown1,
                     PlayerActions.Mute,
                     PlayerActions.FramePrevious,
                     PlayerActions.FrameNext,
                     PlayerActions.SubtitleSeekPrevious,
                     PlayerActions.SubtitleSeekNext,
                     PlayerActions.Screenshot,
                     PlayerActions.AbLoop,
                     PlayerActions.LoopMode,
                     PlayerActions.PlaylistPrevious,
                     PlayerActions.PlaylistNext,
                     PlayerActions.ChapterPrevious,
                     PlayerActions.ChapterNext,
                     PlayerActions.SpeedHalf,
                     PlayerActions.SpeedDouble,
                     PlayerActions.SpeedSlower,
                     PlayerActions.SpeedFaster,
                     PlayerActions.SpeedReset,
                     PlayerActions.ToggleFullscreen,
                     PlayerActions.ExitFullscreen,
                     PlayerActions.EnterFullscreen,
                     PlayerActions.ToggleTopmost,
                     PlayerActions.ShowProgress,
                     PlayerActions.CycleVideo,
                     PlayerActions.CycleAudio,
                     PlayerActions.CycleSubtitle,
                     PlayerActions.CycleEdition,
                     PlayerActions.VideoPanel,
                     PlayerActions.AudioPanel,
                     PlayerActions.SubtitlesPanel,
                     PlayerActions.PlaylistPanel,
                     PlayerActions.ChapterPanel,
                     PlayerActions.SubtitleVisibility,
                     PlayerActions.SecondarySubtitleVisibility,
                     PlayerActions.SubtitleScaleIncrease,
                     PlayerActions.SubtitleScaleDecrease,
                     PlayerActions.SubtitleDelayDecrease,
                     PlayerActions.SubtitleDelayIncrease,
                     PlayerActions.SubtitleDelayReset,
                     PlayerActions.SubtitlePositionUp,
                     PlayerActions.SubtitlePositionDown,
                     PlayerActions.AudioDelayIncrease05,
                     PlayerActions.AudioDelayDecrease05,
                     PlayerActions.AudioDelayIncrease01,
                     PlayerActions.AudioDelayDecrease01,
                     PlayerActions.AudioDelayReset,
                     PlayerActions.WindowHalf,
                     PlayerActions.WindowNormal,
                     PlayerActions.WindowDouble,
                     PlayerActions.Quit,
                 })
        {
            Assert.Contains(required, actions);
        }
    }

    [Fact]
    public void DefaultMapHasNoDuplicateChordsAndAllRoundTrip()
    {
        KeyMap map = KeyMap.CreateDefault();

        foreach (KeyChord chord in map.Bindings.Keys)
        {
            Assert.False(chord.IsEmpty);
            string text = chord.ToString();
            Assert.True(KeyChord.TryParse(text, out KeyChord parsed));
            Assert.Equal(chord.Key, parsed.Key);
            Assert.Equal(chord.Modifiers, parsed.Modifiers);
            Assert.True(map.TryGetAction(parsed, out string? action));
            Assert.False(string.IsNullOrEmpty(action));
        }
    }

    [Fact]
    public void LookupReturnsBoundAction()
    {
        KeyMap map = KeyMap.CreateDefault();
        Assert.True(KeyChord.TryParse("Ctrl+Shift+L", out KeyChord chord));

        Assert.True(map.TryGetAction(chord, out string? action));
        Assert.Equal(PlayerActions.LoopMode, action);
    }

    [Fact]
    public void BindFirstReportsConflict()
    {
        var map = new KeyMap();
        Assert.True(map.BindFirst("Ctrl+O", PlayerActions.Open));
        Assert.False(map.BindFirst("Ctrl+O", PlayerActions.OpenUrl));

        Assert.True(map.TryGetAction(KeyChord.Parse("Ctrl+O"), out string? action));
        Assert.Equal(PlayerActions.Open, action);
    }

    [Fact]
    public void BindOverwritesAndReportsLastConflict()
    {
        var map = new KeyMap();
        map.Bind(KeyChord.Parse("Ctrl+O"), PlayerActions.Open);
        map.Bind(KeyChord.Parse("Ctrl+O"), PlayerActions.OpenUrl);

        Assert.Equal(PlayerActions.Open, map.LastConflictAction);
        Assert.Equal(PlayerActions.OpenUrl, map.FindAction(KeyChord.Parse("Ctrl+O")));
    }
}
