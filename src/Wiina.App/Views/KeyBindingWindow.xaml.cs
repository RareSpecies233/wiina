using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Wiina.App.Localization;
using Wiina.App.Services;
using Wiina.Core.Localization;
using Wiina.Core.Models;
using Wiina.Core.Settings;

namespace Wiina.App.Views;

/// <summary>
/// Dedicated key-binding editor window, opened from Control → 键盘方案.
/// </summary>
public sealed partial class KeyBindingWindow : Window
{
    public KeyBindingWindow()
    {
        InitializeComponent();
        Title = Loc.T("keybindings.windowTitle");

        bool dark = App.Settings.Theme != ThemeMode.Light;
        RootGrid.RequestedTheme = App.Settings.Theme switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default,
        };

        WindowShell.Prepare(
            this,
            (int)Math.Clamp(App.Settings.KeyBindingWindowWidth, 760, 2000),
            (int)Math.Clamp(App.Settings.KeyBindingWindowHeight, 500, 1400),
            resizable: true,
            extendTitleBar: true,
            titleBar: DragRegion,
            dark: dark,
            minWidth: 760,
            minHeight: 500);
        RestoreWindowBounds();

        Editor.AttachWindow(this);
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = Loc.T("keybindings.windowTitle");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
            App.Settings.KeyBindingWindowWidth = Math.Max(760, AppWindow.Size.Width / scale);
            App.Settings.KeyBindingWindowHeight = Math.Max(500, AppWindow.Size.Height / scale);
            App.Settings.KeyBindingWindowX = AppWindow.Position.X;
            App.Settings.KeyBindingWindowY = AppWindow.Position.Y;
            App.SaveSettings();
        }
        catch
        {
            // Bounds persistence must never block closing.
        }

        Loc.LanguageChanged -= OnLanguageChanged;
        WindowShell.Detach(this);
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (App.Settings.KeyBindingWindowX == int.MinValue ||
                App.Settings.KeyBindingWindowY == int.MinValue)
            {
                return;
            }

            DisplayArea area = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Primary);
            RectInt32 work = area.WorkArea;
            int width = AppWindow.Size.Width;
            int height = AppWindow.Size.Height;
            int x = Math.Clamp(
                App.Settings.KeyBindingWindowX,
                work.X - width + 120,
                work.X + work.Width - 120);
            int y = Math.Clamp(
                App.Settings.KeyBindingWindowY,
                work.Y,
                work.Y + work.Height - 80);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
            // Fall back to the default placement.
        }
    }
}
