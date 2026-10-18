using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

public sealed partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // At narrow widths the title + caption buttons need the space; the
        // toolbar is available again as soon as the window is wide enough.
        ButtonPanel.Visibility = e.NewSize.Width < 620
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public event EventHandler? PinClicked;
    public event EventHandler? LayoutClicked;
    public event EventHandler? OscClicked;
    public event EventHandler? SidebarClicked;
    public event EventHandler? ThemeClicked;
    public event EventHandler? IconsClicked;
    public event EventHandler? WindowClicked;

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public bool IsPinned
    {
        set => PinButton.Content = value ? "\uE77A" : "\uE718";
    }

    public void SetTopOscVisible(bool visible)
    {
        TopOscShell.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetDocked(bool docked)
    {
        TranslucentBackground.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        OpaqueBackground.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPinClick(object sender, RoutedEventArgs e) =>
        PinClicked?.Invoke(this, EventArgs.Empty);

    private void OnLayoutClick(object sender, RoutedEventArgs e) =>
        LayoutClicked?.Invoke(this, EventArgs.Empty);

    private void OnOscClick(object sender, RoutedEventArgs e) =>
        OscClicked?.Invoke(this, EventArgs.Empty);

    private void OnSidebarClick(object sender, RoutedEventArgs e) =>
        SidebarClicked?.Invoke(this, EventArgs.Empty);

    private void OnThemeClick(object sender, RoutedEventArgs e) =>
        ThemeClicked?.Invoke(this, EventArgs.Empty);

    private void OnIconsClick(object sender, RoutedEventArgs e) =>
        IconsClicked?.Invoke(this, EventArgs.Empty);

    private void OnWindowClick(object sender, RoutedEventArgs e) =>
        WindowClicked?.Invoke(this, EventArgs.Empty);
}
