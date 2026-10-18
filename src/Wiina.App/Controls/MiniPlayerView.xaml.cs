using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Wiina.Core.State;
using Wiina.Core.Text;

namespace Wiina.App.Controls;

/// <summary>
/// Mini / music mode overlay (Stage 6, spec §10). Implemented as a main-window
/// mode rather than a second window: the same mpv surface keeps rendering
/// album art underneath, while this control supplies cover fallback, title /
/// artist, transport, the 148 DIP volume bubble and the 300 DIP playlist panel.
/// </summary>
public sealed partial class MiniPlayerView : UserControl
{
    private readonly DispatcherQueueTimer _volumeHideTimer;
    private bool _controlsShown;
    private bool _updating;
    private bool _seeking;
    private bool _animationsEnabled = true;
    private bool _muted;
    private bool _dragCandidate;
    private bool _dragging;
    private Windows.Foundation.Point _dragStart;

    public MiniPlayerView()
    {
        InitializeComponent();

        _volumeHideTimer = DispatcherQueue.CreateTimer();
        _volumeHideTimer.Interval = TimeSpan.FromSeconds(1);
        _volumeHideTimer.IsRepeating = false;
        _volumeHideTimer.Tick += (_, _) => HideVolumeBubble();

        ProgressSlider.PointerPressed += (_, _) => _seeking = true;
        ProgressSlider.PointerReleased += (_, _) =>
        {
            _seeking = false;
            SeekRequested?.Invoke(ProgressSlider.Value);
        };
        ProgressSlider.PointerCaptureLost += (_, _) => _seeking = false;

        SizeChanged += (_, _) => FallbackInitial.FontSize =
            Math.Clamp(ActualWidth * 0.18, 40, 96);
    }

    public event EventHandler? PlayPauseRequested;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    public event EventHandler? RestoreRequested;

    public event EventHandler? MuteToggleRequested;

    public event Action<double>? SeekRequested;

    public event Action<double>? VolumeRequested;

    /// <summary>Raised when the playlist panel is expanded/collapsed by the user.</summary>
    public event Action<bool>? PlaylistExpandedChanged;

    public event Action<int>? PlaylistItemRequested;

    /// <summary>Window-drag handshake: the compact window has no titlebar.</summary>
    public event EventHandler? DragStarted;

    public event Action<Windows.Foundation.Point>? DragMoved;

    public event EventHandler? DragEnded;

    public bool IsPlaylistExpanded => PlaylistPanel.Visibility == Visibility.Visible;

    public void SetAnimationsEnabled(bool enabled) => _animationsEnabled = enabled;

    public void SetMedia(string? title, string? artist, string? album, bool hasAlbumArt)
    {
        string resolvedTitle = string.IsNullOrWhiteSpace(title) ? "Wiina" : title;
        TitleText.Text = resolvedTitle;
        string secondary = string.Join(
            " · ",
            new[] { artist, album }.Where(value => !string.IsNullOrWhiteSpace(value)));
        ArtistText.Text = secondary;
        ArtistText.Visibility = string.IsNullOrEmpty(secondary)
            ? Visibility.Collapsed
            : Visibility.Visible;

        bool showFallback = !hasAlbumArt;
        FallbackCover.Visibility = showFallback ? Visibility.Visible : Visibility.Collapsed;
        if (showFallback)
        {
            string initial = resolvedTitle.Trim();
            FallbackInitial.Text = initial.Length > 0
                ? initial[..1].ToUpperInvariant()
                : "♪";
        }
    }

    public void SetPlaying(bool playing)
    {
        PlayPauseButton.Content = playing ? "\uE769" : "\uE768";
    }

    public void SetPlaybackState(bool playing, bool muted, double volume)
    {
        SetPlaying(playing);
        SetMuted(muted);
        SetVolume(volume);
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        _updating = true;
        try
        {
            MuteButton.Content = muted ? "\uE74F" : "\uE767";
            BubbleMuteButton.Content = muted ? "\uE74F" : "\uE767";
            VolumeText.Text = muted ? "静音" : $"{Math.Round(VolumeSlider.Value)}%";
        }
        finally
        {
            _updating = false;
        }
    }

    public void SetVolume(double volume)
    {
        double clamped = Math.Clamp(volume, 0, 130);
        _updating = true;
        try
        {
            VolumeSlider.Value = clamped;
            VolumeText.Text = _muted ? "静音" : $"{Math.Round(clamped)}%";
            MuteButton.Content = _muted || clamped < 1 ? "\uE74F" : "\uE767";
        }
        finally
        {
            _updating = false;
        }
    }

    public void SetPosition(double position, double duration)
    {
        if (_seeking)
        {
            return;
        }

        _updating = true;
        try
        {
            ProgressSlider.Maximum = Math.Max(1, duration);
            ProgressSlider.Value = Math.Clamp(position, 0, Math.Max(1, duration));
            PositionText.Text = TimeFormatter.Format(position);
            DurationText.Text = TimeFormatter.Format(duration);
        }
        finally
        {
            _updating = false;
        }
    }

    public void SetPlaylist(IReadOnlyList<string> items, int currentIndex)
    {
        PlaylistList.ItemsSource = items;
        if (currentIndex >= 0 && currentIndex < items.Count)
        {
            PlaylistList.SelectedIndex = currentIndex;
            PlaylistList.ScrollIntoView(items[currentIndex]);
        }
    }

    public void SetPlaylistExpanded(bool expanded)
    {
        PlaylistPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowControls()
    {
        if (_controlsShown)
        {
            return;
        }

        _controlsShown = true;
        Fade(ControlArea, 1, hitTest: true);
        Fade(InfoPanel, 0, hitTest: false);
    }

    public void HideControls()
    {
        if (!_controlsShown || IsPlaylistExpanded || VolumeBubble.Visibility == Visibility.Visible)
        {
            return;
        }

        _controlsShown = false;
        Fade(ControlArea, 0, hitTest: false);
        Fade(InfoPanel, 1, hitTest: true);
    }

    public void ToggleControls()
    {
        if (_controlsShown)
        {
            HideControls();
        }
        else
        {
            ShowControls();
        }
    }

    public void ShowVolumeBubble()
    {
        VolumeBubble.Visibility = Visibility.Visible;
        _volumeHideTimer.Stop();
        _volumeHideTimer.Start();
    }

    public void HideVolumeBubble()
    {
        _volumeHideTimer.Stop();
        VolumeBubble.Visibility = Visibility.Collapsed;
    }

    public void TogglePlaylist()
    {
        bool expanded = !IsPlaylistExpanded;
        PlaylistPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PlaylistExpandedChanged?.Invoke(expanded);
    }

    /// <summary>Returns to the info-first state after entering mini mode.</summary>
    public void ResetControls()
    {
        _controlsShown = false;
        ControlArea.Opacity = 0;
        ControlArea.IsHitTestVisible = false;
        InfoPanel.Opacity = 1;
        InfoPanel.IsHitTestVisible = true;
        HideVolumeBubble();
        _dragCandidate = false;
        _dragging = false;
    }

    private void Fade(FrameworkElement element, double opacity, bool hitTest)
    {
        element.IsHitTestVisible = hitTest;
        if (!_animationsEnabled)
        {
            element.Opacity = opacity;
            return;
        }

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = opacity,
            Duration = TimeSpan.FromMilliseconds(MiniPlayerLayout.ControlAnimationMilliseconds),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => ShowControls();

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();

        if (!_dragCandidate)
        {
            return;
        }

        Windows.Foundation.Point current = e.GetCurrentPoint(this).Position;
        if (!_dragging)
        {
            double dx = current.X - _dragStart.X;
            double dy = current.Y - _dragStart.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < 3)
            {
                return;
            }

            _dragging = true;
            CapturePointer(e.Pointer);
            DragStarted?.Invoke(this, EventArgs.Empty);
        }

        DragMoved?.Invoke(current);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragCandidate = true;
        _dragging = false;
        _dragStart = e.GetCurrentPoint(this).Position;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            DragEnded?.Invoke(this, EventArgs.Empty);
        }

        _dragCandidate = false;
        _dragging = false;
        try
        {
            ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // Pointer may already be gone.
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            DragEnded?.Invoke(this, EventArgs.Empty);
        }

        _dragCandidate = false;
        _dragging = false;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _volumeHideTimer.Stop();
        HideVolumeBubble();
        HideControls();
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        int delta = properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        double next = VolumeSlider.Value + Math.Sign(delta) * 5;
        _updating = true;
        try
        {
            VolumeSlider.Value = Math.Clamp(next, 0, 130);
        }
        finally
        {
            _updating = false;
        }

        VolumeRequested?.Invoke(VolumeSlider.Value);
        ShowVolumeBubble();
        e.Handled = true;
    }

    private void OnRootDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) =>
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviousClick(object sender, RoutedEventArgs e) =>
        PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void OnNextClick(object sender, RoutedEventArgs e) =>
        NextRequested?.Invoke(this, EventArgs.Empty);

    private void OnRestoreClick(object sender, RoutedEventArgs e) =>
        RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (VolumeBubble.Visibility != Visibility.Visible)
        {
            ShowVolumeBubble();
            return;
        }

        MuteToggleRequested?.Invoke(this, EventArgs.Empty);
        ShowVolumeBubble();
    }

    private void OnPlaylistClick(object sender, RoutedEventArgs e) => TogglePlaylist();

    private void OnProgressChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating || !_seeking)
        {
            return;
        }

        PositionText.Text = TimeFormatter.Format(e.NewValue);
    }

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        VolumeText.Text = $"{Math.Round(e.NewValue)}%";
        VolumeRequested?.Invoke(e.NewValue);
        ShowVolumeBubble();
    }

    private void OnPlaylistItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string item && PlaylistList.ItemsSource is IReadOnlyList<string> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], item, StringComparison.Ordinal))
                {
                    PlaylistItemRequested?.Invoke(i);
                    break;
                }
            }
        }
    }
}
