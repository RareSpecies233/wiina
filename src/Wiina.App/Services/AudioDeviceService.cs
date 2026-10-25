using System;
using System.Collections.Generic;
using System.Globalization;
using Wiina.Mpv;

namespace Wiina.App.Services;

/// <summary>
/// Builds the audio-output-device dropdown shown by Settings → 音频 → 音量与输出.
/// The list comes from mpv's observed <c>audio-device-list</c> (the same cache the
/// sidebar quick settings use), always starts with "auto" (follow the system
/// default) and keeps a persisted device mpv no longer reports as an
/// "unavailable" placeholder so the stored setting is never silently rewritten.
/// </summary>
public static class AudioDeviceService
{
    /// <summary>mpv's name for "let the operating system pick"; persisted verbatim.</summary>
    public const string AutoDeviceName = "auto";

    /// <summary>One dropdown entry. Only <see cref="Name"/> is written to the settings file.</summary>
    public sealed record AudioDeviceOption(string Name, string Label, bool IsUnavailable)
    {
        public string DisplayName => Label;
    }

    /// <summary>
    /// Projects the devices the main window received from mpv. Safe to call before
    /// a player exists: the dropdown then only offers the system default.
    /// </summary>
    public static IReadOnlyList<AudioDeviceOption> Build(
        string? selected,
        string autoLabel,
        string unavailableFormat) =>
        Build(
            App.ActiveWindow?.AudioDeviceList ?? Array.Empty<MpvAudioDevice>(),
            selected,
            autoLabel,
            unavailableFormat);

    /// <summary>Pure projection (no UI, no mpv calls) so the fallbacks stay testable.</summary>
    public static IReadOnlyList<AudioDeviceOption> Build(
        IReadOnlyList<MpvAudioDevice> devices,
        string? selected,
        string autoLabel,
        string unavailableFormat)
    {
        var options = new List<AudioDeviceOption>(devices.Count + 2)
        {
            new(AutoDeviceName, autoLabel, false),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal) { AutoDeviceName };
        var descriptions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (MpvAudioDevice device in devices)
        {
            string name = device.Name?.Trim() ?? string.Empty;
            string description = device.Description?.Trim() ?? string.Empty;
            if (name.Length == 0 || description.Length == 0 ||
                string.Equals(description, name, StringComparison.Ordinal))
            {
                continue;
            }

            descriptions[description] = descriptions.TryGetValue(description, out int count)
                ? count + 1
                : 1;
        }

        foreach (MpvAudioDevice device in devices)
        {
            string name = device.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            string description = device.Description?.Trim() ?? string.Empty;
            // Prefer the human-readable description; fall back to the mpv device
            // name when there is none, and disambiguate same-named devices.
            string label = description.Length == 0 ||
                           string.Equals(description, name, StringComparison.Ordinal)
                ? name
                : descriptions[description] > 1
                    ? $"{description} ({name})"
                    : description;
            options.Add(new AudioDeviceOption(name, label, false));
        }

        string? current = selected?.Trim();
        if (!string.IsNullOrEmpty(current) &&
            !string.Equals(current, AutoDeviceName, StringComparison.OrdinalIgnoreCase) &&
            !seen.Contains(current))
        {
            options.Add(new AudioDeviceOption(
                current,
                string.Format(CultureInfo.InvariantCulture, unavailableFormat, current),
                true));
        }

        return options;
    }

    /// <summary>Pre-selects the stored device when it is known, otherwise "auto".</summary>
    public static int IndexOf(IReadOnlyList<AudioDeviceOption> options, string? selected)
    {
        string? current = selected?.Trim();
        for (int index = 0; index < options.Count; index++)
        {
            if (string.Equals(options[index].Name, current, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }
}
