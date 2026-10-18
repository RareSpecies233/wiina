using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wiina.App.Controls;

public sealed partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        // App actions live in the OSC More menu; the titlebar keeps only the
        // document icon/title so it can fade with the rest of the chrome.
        ButtonPanel.Visibility = Visibility.Collapsed;
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // App actions live in the OSC; never show this toolbar.
        ButtonPanel.Visibility = Visibility.Collapsed;

        // Top OSC compression: toolbar, volume, then time labels. The
        // transport and timeline always stay usable.
        TopToolbar.Visibility = e.NewSize.Width < 700
            ? Visibility.Collapsed
            : Visibility.Visible;
        TopVolume.Visibility = e.NewSize.Width < 560
            ? Visibility.Collapsed
            : Visibility.Visible;
        TopDuration.Visibility = e.NewSize.Width < 440
            ? Visibility.Collapsed
            : Visibility.Visible;
        TopCurrentTime.Visibility = e.NewSize.Width < 360
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
    public event EventHandler? MoreClicked;

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public bool IsPinned
    {
        set => PinButton.Content = value ? "\uE77A" : "\uE718";
    }

    public TimelineControl TopTimeline => TopTimelineControl;

    public VolumeSlider TopVolumeControl => TopVolume;

    public void SetOscDataContext(object? dataContext) => TopOscRoot.DataContext = dataContext;

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

    private void OnMoreClick(object sender, RoutedEventArgs e) =>
        MoreClicked?.Invoke(this, EventArgs.Empty);
}
