using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Wiina.App.Controls;

/// <summary>
/// Stage 3 minimal OSD: one message at a time, 1 s hold, 0.5 s fade. Stage 5
/// replaces the presentation model, but this control stays reusable for
/// low-frequency messages (errors, screenshots).
/// </summary>
public sealed partial class OsdText : UserControl
{
    private readonly DispatcherQueueTimer _timer;
    private Storyboard? _fade;
    private bool? _animationsEnabled;

    public OsdText()
    {
        InitializeComponent();
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => FadeOut();
    }

    public bool IsShowing => OsdRoot.Visibility == Visibility.Visible;

    public void Show(string message, string? secondary = null, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _fade?.Stop();
        _timer.Stop();
        OsdMessageText.Text = message;
        if (string.IsNullOrWhiteSpace(secondary))
        {
            OsdSecondaryText.Text = string.Empty;
            OsdSecondaryText.Visibility = Visibility.Collapsed;
        }
        else
        {
            OsdSecondaryText.Text = secondary;
            OsdSecondaryText.Visibility = Visibility.Visible;
        }

        OsdRoot.Visibility = Visibility.Visible;
        OsdRoot.Opacity = 1;
        _timer.Interval = duration ?? TimeSpan.FromSeconds(1);
        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();
        _fade?.Stop();
        OsdRoot.Visibility = Visibility.Collapsed;
        OsdRoot.Opacity = 0;
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
            Duration = TimeSpan.FromMilliseconds(500),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, OsdRoot);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _fade = new Storyboard();
        _fade.Children.Add(animation);
        _fade.Completed += (_, _) =>
        {
            OsdRoot.Visibility = Visibility.Collapsed;
            OsdRoot.Opacity = 0;
        };
        _fade.Begin();
    }

    private bool AnimationsEnabled
    {
        get
        {
            if (_animationsEnabled is null)
            {
                try
                {
                    _animationsEnabled =
                        new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
                }
                catch
                {
                    _animationsEnabled = true;
                }
            }

            return _animationsEnabled.Value;
        }
    }
}
