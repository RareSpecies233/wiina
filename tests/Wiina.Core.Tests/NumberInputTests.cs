using Wiina.Core.Settings;
using Xunit;

namespace Wiina.Core.Tests;

/// <summary>
/// Number rows used to be commit-only: a typed value reached settings only after
/// Enter or a focus change, so closing the settings window could drop it. These
/// helpers parse and coalesce what the user types.
/// </summary>
public sealed class NumberInputTests
{
    [Theory]
    [InlineData("12", 0, 10, 10)]
    [InlineData("-5", 0, 10, 0)]
    [InlineData("7.5", 0, 10, 7.5)]
    [InlineData("  42  ", 0, 100, 42)]
    [InlineData("1e3", 0, 1000, 1000)]
    public void ParsesAndClampsToTheRowBounds(
        string text,
        double minimum,
        double maximum,
        double expected)
    {
        Assert.True(NumberInput.TryParse(text, minimum, maximum, out double value));
        Assert.Equal(expected, value, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RejectsTextThatMustNotReachSettings(string text)
    {
        Assert.False(NumberInput.TryParse(text, 0, 100, out _));
    }

    [Fact]
    public void BlankInputKeepsThePreviousValueRatherThanZero()
    {
        // A cleared box must not look like "user wants 0"; the caller leaves the
        // stored setting untouched because parsing failed.
        Assert.False(NumberInput.TryParse(" ", 5, 10, out double value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void ClampToleratesReversedBounds()
    {
        Assert.Equal(5, NumberInput.Clamp(9, 5, 1));
        Assert.Equal(1, NumberInput.Clamp(-3, 5, 1));
    }

    [Fact]
    public void CoalescedEditKeepsOnlyTheNewestValue()
    {
        var edit = new CoalescedEdit<double>();
        edit.Set(1);
        edit.Set(2);
        edit.Set(3);

        Assert.True(edit.HasPending);
        Assert.Equal(3, edit.PendingValue);
        Assert.True(edit.TryTake(out double value));
        Assert.Equal(3, value);
        Assert.False(edit.HasPending);
        Assert.False(edit.TryTake(out _));
    }

    [Fact]
    public void CoalescedEditClearsWithoutCommitting()
    {
        var edit = new CoalescedEdit<int>();
        edit.Set(7);
        edit.Clear();

        Assert.False(edit.HasPending);
        Assert.Null(edit.PendingValue);
        Assert.False(edit.TryTake(out _));
    }
}
