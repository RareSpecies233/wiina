using System.Globalization;

namespace Wiina.Core.Settings;

/// <summary>
/// Parsing and coalescing helpers for numeric settings rows. A WinUI
/// <c>NumberBox</c> only raises <c>ValueChanged</c> when the text is committed
/// (Enter, focus loss, spinner), so typing "1" then "0" and closing the window
/// used to lose the edit entirely. These helpers let the window apply typed text
/// after a short debounce and collapse a burst of keystrokes into one save.
/// </summary>
public static class NumberInput
{
    /// <summary>
    /// Parses user text into a value clamped to <paramref name="minimum"/> –
    /// <paramref name="maximum"/>. Returns false for blank, non-numeric, NaN or
    /// infinite text so the caller can keep the previous (valid) value instead of
    /// writing garbage into settings. Both the current and invariant cultures are
    /// accepted, so "1,5" works on a German machine and "1.5" always works.
    /// </summary>
    public static bool TryParse(
        string? text,
        double minimum,
        double maximum,
        out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        const NumberStyles styles = NumberStyles.Float;
        if (!double.TryParse(trimmed, styles, CultureInfo.CurrentCulture, out double parsed) &&
            !double.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out parsed))
        {
            return false;
        }

        if (!double.IsFinite(parsed))
        {
            return false;
        }

        value = Clamp(parsed, minimum, maximum);
        return true;
    }

    /// <summary>Clamps to the row bounds, tolerating a reversed pair.</summary>
    public static double Clamp(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value))
        {
            return double.IsFinite(minimum) ? minimum : 0;
        }

        double low = Math.Min(minimum, maximum);
        double high = Math.Max(minimum, maximum);
        if (!double.IsFinite(low))
        {
            low = value;
        }

        if (!double.IsFinite(high))
        {
            high = value;
        }

        return Math.Clamp(value, low, high);
    }
}

/// <summary>
/// Holds the newest pending value of one edit field until the debounce timer
/// fires. <see cref="Set"/> overwrites older edits (last keystroke wins) and
/// <see cref="TryTake"/> clears the slot, which is what keeps one edit burst to a
/// single settings write and a single apply.
/// </summary>
public sealed class CoalescedEdit<T>
    where T : struct
{
    private T _value;
    private bool _hasValue;

    /// <summary>True when <see cref="TryTake"/> has something to deliver.</summary>
    public bool HasPending => _hasValue;

    /// <summary>Newest pending value, or <c>null</c> when nothing is queued.</summary>
    public T? PendingValue => _hasValue ? _value : (T?)null;

    /// <summary>Records <paramref name="value"/> as the value to commit next.</summary>
    public void Set(T value)
    {
        _value = value;
        _hasValue = true;
    }

    /// <summary>
    /// Takes the pending value and clears the slot. Returns false when nothing was
    /// queued, so a flush with no edits performs no apply and no save.
    /// </summary>
    public bool TryTake(out T value)
    {
        value = _value;
        if (!_hasValue)
        {
            return false;
        }

        _hasValue = false;
        return true;
    }

    /// <summary>Drops the pending value without committing it.</summary>
    public void Clear()
    {
        _hasValue = false;
        _value = default;
    }
}
