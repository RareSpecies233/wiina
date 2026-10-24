namespace Wiina.Core.Settings;

/// <summary>The eleven pages of the Stage 7 settings window (spec §12.1 order).</summary>
public enum SettingsPageId
{
    General,
    Ui,
    Video,
    Audio,
    Subtitles,
    Network,
    Control,
    KeyBindings,
    Advanced,
    Integration,
    Plugin,
}

/// <summary>Static page metadata used by navigation, search and headers.</summary>
public sealed record SettingsPageInfo(
    SettingsPageId Id,
    string Title,
    string Summary,
    string Glyph);

/// <summary>How the settings window renders a row's right-hand control.</summary>
public enum SettingsRowKind
{
    /// <summary>ToggleSwitch bound to a bool.</summary>
    Toggle,

    /// <summary>ComboBox of <see cref="SettingsRowDefinition.Choices"/> (int index).</summary>
    Choice,

    /// <summary>NumberBox with min/max/step (double).</summary>
    Number,

    /// <summary>Editable TextBox (string).</summary>
    Text,

    /// <summary>
    /// ComboBox of the audio output devices libmpv reports (<c>audio-device-list</c>);
    /// the value is the mpv device name, "auto" meaning "follow the system default".
    /// </summary>
    AudioDevice,

    /// <summary>Button that runs <see cref="SettingsRowDefinition.ActionId"/>.</summary>
    Action,

    /// <summary>Read-only value; <see cref="SettingsRowDefinition.Get"/> or a runtime id.</summary>
    Info,

    /// <summary>Button that opens the font picker; value is the family name.</summary>
    Font,

    /// <summary>The key-binding editor panel.</summary>
    KeyBindings,

    /// <summary>The Stage 9 plugin list placeholder.</summary>
    PluginList,
}

/// <summary>
/// One settings row. Getters/setters live in Core so the window only renders and
/// the catalog can be unit-tested (no reflection, no WinUI dependency).
/// </summary>
public sealed class SettingsRowDefinition
{
    public required string Id { get; init; }

    public required SettingsPageId Page { get; init; }

    public required string Section { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Extra search terms (English ids, aliases).</summary>
    public string Keywords { get; init; } = string.Empty;

    public SettingsRowKind Kind { get; init; } = SettingsRowKind.Info;

    public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 100;

    public double Step { get; init; } = 1;

    public string Suffix { get; init; } = string.Empty;

    /// <summary>Non-null disables the row and explains why (spec §12.3).</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>Handler id for <see cref="SettingsRowKind.Action"/>.</summary>
    public string? ActionId { get; init; }

    public Func<AppSettings, object?>? Get { get; init; }

    public Action<AppSettings, object?>? Set { get; init; }

    public bool IsAvailable => string.IsNullOrEmpty(UnavailableReason);

    public object? Value(AppSettings settings) => Get?.Invoke(settings);

    public void Apply(AppSettings settings, object? value) => Set?.Invoke(settings, value);
}
