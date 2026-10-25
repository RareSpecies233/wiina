using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wiina.App.Interop;
using Wiina.Core.Settings;

namespace Wiina.App.Controls;

public sealed partial class TitleBarView : UserControl
{
    private ChromeBlurLayer? _blur;
    private bool _docked;

    public TitleBarView()
    {
        InitializeComponent();
        // App actions live in the OSC More menu; the titlebar keeps only the
        // document icon/title so it can fade with the rest of the chrome.
        ButtonPanel.Visibility = Visibility.Collapsed;
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

    /// <summary>
    /// Keeps the hand-drawn caption buttons only on platforms where the title
    /// bar can be extended and recoloured. On Windows 10 the native caption
    /// buttons stay, so the XAML copies are hidden.
    /// </summary>
    public bool ShowCustomCaptionButtons
    {
        get => WindowControlPanel.Visibility == Visibility.Visible;
        set => WindowControlPanel.Visibility =
            value ? Visibility.Visible : Visibility.Collapsed;
    }
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    public event EventHandler? CloseRequested;

    private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
        MinimizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        MaximizeRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public bool IsPinned
    {        set
        {
            PinButton.Content = value ? "\uE77A" : "\uE718";
            PinIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
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
        _docked = docked;
        OpaqueBackground.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        ApplyBlurVisuals();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.ModeChanged += OnBlurModeChanged;
        EnsureAttached();
        ApplyBlurVisuals();
    }

    private void EnsureAttached()
    {
        // Early calls can arrive before the element is in a XamlRoot; retry
        // once the layout pass has produced one.
        if (_blur is not null || XamlRoot is null)
        {
            return;
        }

        // Real blurred frames of the video sitting behind the titlebar. Until
        // the first frame arrives, and in Solid mode, the opaque/solid
        // ChromeSolidBrush stays in place.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 0, out ChromeBlurLayer layer))
        {
            return;
        }

        _blur = layer;
        _blur.FirstFrame += OnFirstBlurFrame;
        _blur.RaiseFirstFrameIfReady();
    }

    private void OnBlurModeChanged(object? sender, EventArgs e)
    {
        EnsureAttached();
        ApplyBlurVisuals();
    }

    private void OnFirstBlurFrame(object? sender, EventArgs e) => ApplyBlurVisuals();

    private void ApplyBlurVisuals()
    {
        bool live = ChromeBlur.Current.Mode != ChromeBlurMode.Solid;
        if (_blur is not null)
        {
            // The canvas is an effect, not chrome: never let it eat input.
            _blur.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        }

        // Docked videos live in the layout below the titlebar, so the blurred
        // overlay frame would not match what is behind the bar; stay opaque.
        bool blurred = live && !_docked && _blur is { HasFrame: true };
        if (blurred)
        {
            TranslucentBackground.Visibility = Visibility.Collapsed;
            BlurTint.Visibility = Visibility.Visible;
            TopOscAcrylicLayer.Visibility = Visibility.Collapsed;
            TopOscBlurTintLayer.Visibility = Visibility.Visible;
            return;
        }

        BlurTint.Visibility = Visibility.Collapsed;
        TopOscBlurTintLayer.Visibility = Visibility.Collapsed;
        TopOscAcrylicLayer.Visibility = Visibility.Visible;
        TranslucentBackground.Visibility = _docked
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.ModeChanged -= OnBlurModeChanged;
        if (_blur is not null)
        {
            _blur.FirstFrame -= OnFirstBlurFrame;
        }

        _blur = null;
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
