using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Wiina.App.Interop;
using Wiina.Core.Osd;
using Wiina.Core.Settings;

namespace Wiina.App.Controls;

/// <summary>
/// Stage 5 OSD: one coalesced message at a time with an icon, optional
/// secondary line and a progress bar.
/// </summary>
public sealed partial class OsdText : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    private readonly OsdQueue _queue = new();
    private Storyboard? _fade;
    private ChromeBlurLayer? _blur;
    private bool? _systemAnimationsEnabled;
    private bool _appAnimationsEnabled = true;
    private bool _enabled = true;
    private bool _showing;
    private double _scalePercent = 100;
    private double _progressValue;
    private TimeSpan _duration = TimeSpan.FromSeconds(1);

    public OsdText()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = _duration;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => FadeOut();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => ApplyBlurVisuals();
        ApplyScale();
    }

    public bool IsShowing => _showing;

    public bool AnimationsEnabled => _appAnimationsEnabled && SystemAnimationsEnabled;

    public void SetAnimationsEnabled(bool enabled) => _appAnimationsEnabled = enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Hide();
        }
    }

    public void SetScale(double percent)
    {
        _scalePercent = OsdSettings.ClampSize(percent);
        ApplyScale();
    }

    /// <summary>Aligns the card inside its host so six-position placement reads naturally.</summary>
    public void SetAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        OsdRoot.HorizontalAlignment = horizontal;
        OsdRoot.VerticalAlignment = vertical;
    }

    public void SetDuration(TimeSpan duration)
    {
        _duration = duration <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(OsdSettings.DefaultDurationSeconds)
            : duration;
    }

    public void Show(string message, string? secondary = null, TimeSpan? duration = null) =>
        Show(OsdMessages.FromText(message, secondary), duration);

    public void Show(OsdMessage message, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_enabled && !message.AlwaysShow)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        _timer.Stop();
        _fade?.Stop();
        _queue.Enqueue(message);
        ApplyMessage(message);

        IsHitTestVisible = false;
        OsdRoot.MaxWidth = 460;
        OsdRoot.IsHitTestVisible = false;
        Reveal(message.Duration ?? duration ?? _duration);
    }

    public void Hide()
    {
        _timer.Stop();
        _fade?.Stop();
        _fade = null;
        _showing = false;
        IsHitTestVisible = false;
        OsdRoot.Visibility = Visibility.Collapsed;
        OsdRoot.Opacity = 0;
        OsdRoot.IsHitTestVisible = false;
    }

    private void Reveal(TimeSpan duration)
    {
        ShowRoot();
        _timer.Interval = duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : duration;
        _timer.Start();
    }

    private void ShowRoot()
    {
        bool wasVisible = OsdRoot.Visibility == Visibility.Visible &&
            OsdRoot.Opacity > 0.01;
        OsdRoot.Visibility = Visibility.Visible;
        OsdRoot.Opacity = 1;
        _showing = true;

        // A new message while the card is already visible updates in place; it
        // must not restart a fade-in animation (spec §8.3).
        if (!wasVisible && AnimationsEnabled)
        {
            OsdRoot.Opacity = 0;
            var animation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(120),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, OsdRoot);
            Storyboard.SetTargetProperty(animation, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
    }

    private void FadeOut()
    {
        _timer.Stop();
        if (!AnimationsEnabled)
        {
            Hide();
            return;
        }

        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromSeconds(OsdSettings.FadeOutSeconds),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, OsdRoot);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _fade = new Storyboard();
        _fade.Children.Add(animation);
        _fade.Completed += (_, _) =>
        {
            _showing = false;
            OsdRoot.Visibility = Visibility.Collapsed;
            OsdRoot.Opacity = 0;
        };
        _fade.Begin();
    }

    private void ApplyMessage(OsdMessage message)
    {
        OsdMessageText.Text = message.Text;
        OsdMessageText.Visibility = Visibility.Visible;
        OsdSecondaryText.Text = message.Secondary ?? string.Empty;
        OsdSecondaryText.Visibility = string.IsNullOrWhiteSpace(message.Secondary)
            ? Visibility.Collapsed
            : Visibility.Visible;

        OsdIcon.Glyph = message.IconGlyph;
        OsdIcon.Visibility = string.IsNullOrEmpty(message.IconGlyph)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (message.Progress is double progress)
        {
            _progressValue = Math.Clamp(progress, 0, 1);
            OsdProgressHost.Visibility = Visibility.Visible;
            UpdateProgressWidth();
        }
        else
        {
            _progressValue = 0;
            OsdProgressHost.Visibility = Visibility.Collapsed;
        }

        FlowDirection direction = message.LeftToRight
            ? FlowDirection.LeftToRight
            : FlowDirection.LeftToRight;
        OsdRoot.FlowDirection = direction;
        TitleRow.FlowDirection = direction;

        ApplyScale();
        OsdContent.Padding = new Thickness(16, 8, 16, 8);
    }

    private void OnProgressSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateProgressWidth();

    private void UpdateProgressWidth()
    {
        double width = OsdProgressHost.ActualWidth;
        OsdProgressFill.Width = width > 0
            ? Math.Max(0, width * _progressValue)
            : 0;
    }

    private void ApplyScale()
    {
        double main = OsdSettings.MainFontSize(_scalePercent);
        OsdMessageText.FontSize = main;
        OsdSecondaryText.FontSize = OsdSettings.SecondaryFontSize(_scalePercent);
        OsdIcon.FontSize = OsdSettings.IconFontSize(_scalePercent);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ChromeBlur.ModeChanged += OnBlurModeChanged;
        EnsureAttached();
        ApplyBlurVisuals();
    }

    private void EnsureAttached()
    {
        if (_blur is not null || XamlRoot is null)
        {
            return;
        }

        // Same 12 DIP radius as the rounded card so the blurred crop cannot
        // bleed past its corners.
        if (!ChromeBlur.TryAttach(BackdropBlurHost, 12, out ChromeBlurLayer layer))
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
            _blur.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        }

        bool blurred = live && _blur is { HasFrame: true };
        OsdBlurTintLayer.Visibility = blurred ? Visibility.Visible : Visibility.Collapsed;
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

    private bool SystemAnimationsEnabled
    {
        get
        {
            if (_systemAnimationsEnabled is null)
            {
                try
                {
                    _systemAnimationsEnabled =
                        new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
                }
                catch
                {
                    _systemAnimationsEnabled = true;
                }
            }

            return _systemAnimationsEnabled.Value;
        }
    }
}
