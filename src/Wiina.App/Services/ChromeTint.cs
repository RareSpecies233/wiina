using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Wiina.App.Services;

/// <summary>
/// Opaque chrome paint: a solid panel colour tinted with the system accent.
/// Issue 2 replaced the old per-frame video capture + Win2D Gaussian blur
/// (docs/architecture/0003-acrylic.md) with this single cheap colour block.
/// </summary>
internal static class ChromeTint
{
    /// <summary>How much of the system accent is mixed into the base panel.</summary>
    private const double AccentWeight = 0.16;

    private static readonly Color DarkBase = Color.FromArgb(255, 0x20, 0x22, 0x27);
    private static readonly Color LightBase = Color.FromArgb(255, 0xF3, 0xF3, 0xF3);

    private static UISettings? _uiSettings;
    private static bool _hooked;

    /// <summary>
    /// Recompute the opaque chrome brushes for the dark and light theme
    /// dictionaries. Call on the UI thread after app resources are loaded.
    /// </summary>
    public static void Recolor()
    {
        HookAccentChanges();

        Color accent = new UISettings().GetColorValue(UIColorType.Accent);
        Recolor("Dark", DarkBase, accent);
        Recolor("Light", LightBase, accent);
    }

    private static void HookAccentChanges()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += (_, _) =>
        {
            // Colour notifications arrive on a worker thread; brushes are
            // thread-affine, so bounce back to the shell's UI thread.
            if (App.ActiveWindow?.DispatcherQueue is { } dispatcher)
            {
                dispatcher.TryEnqueue(Recolor);
            }
        };
    }

    private static void Recolor(string themeKey, Color baseColor, Color accent)
    {
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out object? value) &&
            value is ResourceDictionary dictionary &&
            dictionary.TryGetValue("ChromeSolidBrush", out object? brush) &&
            brush is SolidColorBrush solid)
        {
            solid.Color = Mix(baseColor, accent, AccentWeight);
        }
    }

    private static Color Mix(Color from, Color to, double weight)
    {
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + (to.R - from.R) * weight),
            (byte)Math.Round(from.G + (to.G - from.G) * weight),
            (byte)Math.Round(from.B + (to.B - from.B) * weight));
    }
}
