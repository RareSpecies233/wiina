using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Wiina.Core.Osd;

namespace Wiina.App.Controls;

/// <summary>
/// Stage 5 OSD: one coalesced message at a time with an icon, optional
/// secondary line, progress bar and a persistent technical variant.
/// </summary>
public sealed partial class OsdText : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    private readonly OsdQueue _queue = new();
    private Storyboard? _fade;
    private bool? _systemAnimationsEnabled;
    private bool _appAnimationsEnabled = true;
    private bool _enabled = true;
    private bool _showing;
    private bool _persistent;
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
        ApplyScale();
    }

    public event Action? PersistentCloseRequested;

    public event Action<bool>? PersistentVisibilityChanged;

    public bool IsShowing => _showing;

    public bool IsPersistentShowing => _persistent;

    public bool AnimationsEnabled => _appAnimationsEnabled && SystemAnimationsEnabled;

    public void SetAnimationsEnabled(bool enabled) => _appAnimationsEnabled = enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled && !_persistent)
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
        if (_persistent)
        {
            return;
        }

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

        _persistent = false;
        IsHitTestVisible = false;
        CloseButton.Visibility = Visibility.Collapsed;
        PersistentContent.Visibility = Visibility.Collapsed;
        PersistentBodyScroller.Visibility = Visibility.Collapsed;
        OsdRoot.MaxWidth = 460;
        OsdRoot.IsHitTestVisible = false;
        Reveal(message.Duration ?? duration ?? _duration);
    }

    /// <summary>
    /// Shows the persistent technical OSD. Normal messages are suppressed until
    /// this is closed with the button, Esc, or <see cref="HidePersistent"/>.
    /// </summary>
    public void ShowPersistent(
        string title,
        string? subtitle = null,
        IReadOnlyList<(string Label, string Value)>? details = null,
        string? body = null)
    {
        _timer.Stop();
        _fade?.Stop();
        _queue.Clear();

        OsdMessage message = OsdMessages.Persistent(title, subtitle);
        ApplyMessage(message);
        BuildDetails(details);

        PersistentBody.Text = body ?? string.Empty;
        PersistentBodyScroller.Visibility = string.IsNullOrWhiteSpace(body)
            ? Visibility.Collapsed
            : Visibility.Visible;
        bool hasExtra = (details is { Count: > 0 }) || !string.IsNullOrWhiteSpace(body);
        PersistentContent.Visibility = hasExtra ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        OsdRoot.MaxWidth = 520;
        OsdRoot.Padding = new Thickness(16, 12, 16, 12);
        OsdRoot.IsHitTestVisible = true;

        _persistent = true;
        IsHitTestVisible = true;
        ShowRoot();
        PersistentVisibilityChanged?.Invoke(true);
    }

    public void HidePersistent()
    {
        if (!_persistent)
        {
            return;
        }

        _persistent = false;
        PersistentVisibilityChanged?.Invoke(false);
        Hide();
    }

    public void Hide()
    {
        _timer.Stop();
        _fade?.Stop();
        _fade = null;
        _showing = false;
        _persistent = false;
        IsHitTestVisible = false;
        OsdRoot.Visibility = Visibility.Collapsed;
        OsdRoot.Opacity = 0;
        OsdRoot.IsHitTestVisible = false;
        CloseButton.Visibility = Visibility.Collapsed;
        PersistentContent.Visibility = Visibility.Collapsed;
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
            if (_persistent)
            {
                return;
            }

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
        OsdRoot.Padding = new Thickness(16, 8, 16, 8);
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
        CloseButton.FontSize = Math.Clamp(11 * _scalePercent / 100, 9, 24);
    }

    private void BuildDetails(IReadOnlyList<(string Label, string Value)>? details)
    {
        PersistentDetails.Children.Clear();
        if (details is null)
        {
            return;
        }

        foreach ((string label, string value) in details)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                VerticalAlignment = VerticalAlignment.Top,
            };
            var valueBlock = new TextBlock
            {
                Text = value,
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            valueBlock.FontWeight = FontWeights.Normal;
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(labelBlock);
            row.Children.Add(valueBlock);
            PersistentDetails.Children.Add(row);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_persistent)
        {
            return;
        }

        PersistentCloseRequested?.Invoke();
        HidePersistent();
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
