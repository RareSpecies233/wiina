using Wiina.Core.Input;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class KeyBindingTests
{
    [Fact]
    public void DefaultsCoverTheDocumentedShortcuts()
    {
        IReadOnlyList<KeyBindingSetting> defaults = KeyBindings.CreateDefaults();
        Assert.True(defaults.Count >= 50);
        Assert.Contains(defaults, binding =>
            binding.Action == PlayerActions.PlayPause && binding.Chord == "Space");
        Assert.Contains(defaults, binding =>
            binding.Action == PlayerActions.Screenshot && binding.Chord == "Ctrl+S");
        Assert.Contains(defaults, binding =>
            binding.Action == PlayerActions.ChapterNext && binding.Chord == "Ctrl+Shift+.");
        Assert.Contains(defaults, binding =>
            binding.Action == PlayerActions.PlayPause && binding.Chord == "MediaPlayPause");
        Assert.All(defaults, binding => Assert.True(
            PlayerActionCatalog.IsKnown(binding.Action),
            binding.Action));
        Assert.All(defaults, binding => Assert.NotNull(KeyBindings.NormalizeChord(binding.Chord)));
    }

    [Fact]
    public void EmptyStoredListMeansDefaults()
    {
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.Resolve(Array.Empty<KeyBindingSetting>()).Count);
        Assert.Equal(
            KeyBindings.CreateDefaults().Count,
            KeyBindings.Resolve(null).Count);
    }

    [Fact]
    public void ResolveDropsUnknownActionsAndBadChords()
    {
        var stored = new[]
        {
            new KeyBindingSetting("not-a-real-action", "Ctrl+K"),
            new KeyBindingSetting(PlayerActions.Open, "Definitely+Not+A+Chord"),
            new KeyBindingSetting(PlayerActions.Open, "ctrl+o"),
        };
        IReadOnlyList<KeyBindingSetting> resolved = KeyBindings.Resolve(stored);
        KeyBindingSetting only = Assert.Single(resolved);
        Assert.Equal(PlayerActions.Open, only.Action);
        Assert.Equal("Ctrl+O", only.Chord);
    }

    [Fact]
    public void CustomChordOverridesDefaultInKeyMap()
    {
        var bindings = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.Open,
            "Ctrl+Shift+K");
        var map = KeyBindings.BuildKeyMap(bindings);
        Assert.True(map.TryGetAction(KeyChord.Parse("Ctrl+Shift+K"), out string? action));
        Assert.Equal(PlayerActions.Open, action);
        Assert.False(map.TryGetAction(KeyChord.Parse("Ctrl+O"), out _));
    }

    [Fact]
    public void ConflictIsDetectedBeforeApplying()
    {
        IReadOnlyList<KeyBindingSetting> bindings = KeyBindings.CreateDefaults();
        string? conflict = KeyBindings.FindConflict(bindings, "Ctrl+S", PlayerActions.Open);
        Assert.Equal(PlayerActions.Screenshot, conflict);

        // The action's own binding is not a conflict.
        Assert.Null(KeyBindings.FindConflict(bindings, "Ctrl+S", PlayerActions.Screenshot));
        Assert.Null(KeyBindings.FindConflict(bindings, "Ctrl+Alt+Shift+F12"));
    }

    [Fact]
    public void SetBindingStealsTheChordFromTheOtherAction()
    {
        IReadOnlyList<KeyBindingSetting> bindings = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.Open,
            "Ctrl+S");
        Assert.DoesNotContain(bindings, binding =>
            binding.Action == PlayerActions.Screenshot &&
            binding.Chord == "Ctrl+S");
        Assert.Equal(
            1,
            bindings.Count(binding =>
                binding.Action == PlayerActions.Open && binding.Chord == "Ctrl+S"));
        Assert.Null(KeyBindings.FindConflict(bindings, "Ctrl+S", PlayerActions.Open));
    }

    [Fact]
    public void EmptyChordClearsTheAction()
    {
        IReadOnlyList<KeyBindingSetting> bindings = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.PlayPause,
            null);
        Assert.DoesNotContain(bindings, binding => binding.Action == PlayerActions.PlayPause);
    }

    [Fact]
    public void ResetActionRestoresItsDefaults()
    {
        IReadOnlyList<KeyBindingSetting> cleared = KeyBindings.ClearAction(
            KeyBindings.CreateDefaults(),
            PlayerActions.PlayPause);
        IReadOnlyList<KeyBindingSetting> reset = KeyBindings.ResetAction(
            cleared,
            PlayerActions.PlayPause);
        Assert.Equal(
            new[] { "MediaPlayPause", "P", "Space" },
            reset
                .Where(binding => binding.Action == PlayerActions.PlayPause)
                .Select(binding => binding.Chord)
                .OrderBy(chord => chord, StringComparer.Ordinal)
                .ToArray());
        Assert.Contains(reset, binding => binding.Action == PlayerActions.Screenshot);
    }

    [Fact]
    public void ExportImportRoundTrips()
    {
        IReadOnlyList<KeyBindingSetting> bindings = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.Screenshot,
            "Ctrl+Alt+S");
        string json = KeyBindings.ExportJson(bindings);
        Assert.Contains("\"bindings\"", json);
        Assert.True(KeyBindings.TryImportJson(json, out IReadOnlyList<KeyBindingSetting> imported, out string error));
        Assert.True(string.IsNullOrEmpty(error));
        Assert.Equal(bindings.Count, imported.Count);
        Assert.Contains(imported, binding =>
            binding.Action == PlayerActions.Screenshot && binding.Chord == "Ctrl+Alt+S");
    }

    [Fact]
    public void ImportRejectsMalformedOrEmptyPayloads()
    {
        Assert.False(KeyBindings.TryImportJson("{not json", out _, out string malformed));
        Assert.False(string.IsNullOrWhiteSpace(malformed));

        Assert.False(KeyBindings.TryImportJson("", out _, out string empty));
        Assert.False(string.IsNullOrWhiteSpace(empty));

        Assert.False(KeyBindings.TryImportJson(
            "{\"version\":1,\"bindings\":[]}",
            out _,
            out string none));
        Assert.False(string.IsNullOrWhiteSpace(none));

        Assert.False(KeyBindings.TryImportJson(
            "{\"version\":1,\"bindings\":[{\"action\":\"nope\",\"chord\":\"Ctrl+Q\"}]}",
            out _,
            out string invalid));
        Assert.False(string.IsNullOrWhiteSpace(invalid));
    }

    [Fact]
    public void ImportSkipsDuplicatesButKeepsFirst()
    {
        const string json = """
            {
              "version": 1,
              "bindings": [
                { "action": "open", "chord": "Ctrl+O" },
                { "action": "screenshot", "chord": "Ctrl+O" },
                { "action": "screenshot", "chord": "Ctrl+S" }
              ]
            }
            """;
        Assert.True(KeyBindings.TryImportJson(json, out IReadOnlyList<KeyBindingSetting> imported, out string note));
        Assert.Contains("跳过", note);
        Assert.Equal(2, imported.Count);
        Assert.Equal(PlayerActions.Open, imported[0].Action);
        Assert.Equal(PlayerActions.Screenshot, imported[1].Action);
    }

    [Fact]
    public void ChordNormalizationIsCanonical()
    {
        Assert.Equal("Ctrl+Shift+O", KeyBindings.NormalizeChord(" ctrl + shift + o "));
        Assert.Equal("Ctrl+,", KeyBindings.NormalizeChord("control+,"));
        Assert.Null(KeyBindings.NormalizeChord("bogus"));
        Assert.Null(KeyBindings.NormalizeChord(""));
        Assert.Null(KeyBindings.NormalizeChord(null));
    }

    [Fact]
    public void EveryActionHasALabelAndCategory()
    {
        foreach (PlayerActionInfo info in PlayerActionCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Label), info.Action);
            Assert.False(string.IsNullOrWhiteSpace(info.Category), info.Action);
            Assert.Equal(info.Label, PlayerActionCatalog.Label(info.Action));
            Assert.True(PlayerActionCatalog.IsKnown(info.Action));
        }

        Assert.Equal("未知动作", PlayerActionCatalog.Label("unknown-action"));
    }

    [Fact]
    public void RawMpvCommandsAreFirstClassBindings()
    {
        Assert.True(PlayerActionCatalog.IsKnown("mpv:cycle stats"));
        Assert.True(PlayerActionCatalog.IsRawCommand("mpv:cycle stats"));
        Assert.Equal("cycle stats", PlayerActionCatalog.RawCommand("mpv:cycle stats"));
        Assert.Contains("mpv 命令", PlayerActionCatalog.Label("mpv:cycle stats"));
        Assert.Equal("mpv 命令", PlayerActionCatalog.CategoryOf("mpv:cycle stats"));

        var stored = new[]
        {
            new KeyBindingSetting("mpv:cycle stats", "Ctrl+Alt+X"),
        };
        IReadOnlyList<KeyBindingSetting> resolved = KeyBindings.Resolve(stored, "我的方案");
        KeyBindingSetting only = Assert.Single(resolved);
        Assert.Equal("mpv:cycle stats", only.Action);

        KeyMap map = KeyBindings.BuildKeyMap(resolved, "我的方案");
        Assert.True(map.TryGetAction(KeyChord.Parse("Ctrl+Alt+X"), out string? action));
        Assert.Equal("mpv:cycle stats", action);
    }

    [Fact]
    public void PrimaryChordsFollowTheEffectiveBindings()
    {
        IReadOnlyList<KeyBindingSetting> stored = KeyBindings.SetBinding(
            KeyBindings.CreateDefaults(),
            PlayerActions.Open,
            "Ctrl+Shift+K");
        IReadOnlyDictionary<string, KeyChord> map = KeyBindings.PrimaryChords(stored, "我的方案");

        Assert.Equal(KeyChord.Parse("Ctrl+Shift+K"), map[PlayerActions.Open]);

        // Built-in profiles ignore stored rows and resolve their own preset.
        IReadOnlyDictionary<string, KeyChord> wiina =
            KeyBindings.PrimaryChords(stored, KeyBindingProfiles.Wiina);
        Assert.Equal(KeyChord.Parse("Space"), wiina[PlayerActions.PlayPause]);
        Assert.Equal(KeyChord.Parse("Ctrl+S"), wiina[PlayerActions.Screenshot]);
    }
}
