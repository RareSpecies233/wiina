using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Wiina.App.Interop;
using Wiina.Core.State;
using Wiina.App.Localization;
using Wiina.Core.Text;
// Alias instead of `using Windows.System;`: that namespace also exposes a
// DispatcherQueueTimer, which would collide with Microsoft.UI.Dispatching.
using VirtualKey = Windows.System.VirtualKey;

namespace Wiina.App.Controls;

/// <summary>
/// Mini / music mode overlay (Stage 6, spec §10). Implemented as a main-window
/// mode rather than a second window: the same mpv surface keeps rendering
/// album art underneath, while this control supplies cover fallback, title /
/// artist, transport, the 148 DIP volume bubble and the 300 DIP playlist panel.
/// </summary>
public sealed partial class MiniPlayerView : UserControl
{
    /// <summary>Gap between the volume bubble and the mute button (P1-5).</summary>
    private const double VolumeBubbleGap = 8;

    /// <summary>Keep the anchored bubble this far from the window edges.</summary>
    private const double BubbleEdgePadding = 8;

    private readonly DispatcherQueueTimer _volumeHideTimer;
    private readonly Dictionary<Control, (bool IsTabStop, bool AllowFocusOnInteraction)> _keyboardState = new();
    private bool _mouseOnlyKeyboard;
    private bool _controlsShown;
    private bool _updating;
    private bool _seeking;
    private bool _animationsEnabled = true;
    private bool _muted;
    private bool _dragCandidate;
    private bool _dragging;
    private bool _positioningBubble;
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

        SizeChanged += (_, _) =>
        {
            FallbackInitial.FontSize = Math.Clamp(ActualWidth * 0.18, 40, 96);
            // The window grows by 300 DIP when the playlist expands; the
            // bubble hugs the mute button, so its anchor moved too.
            PositionVolumeBubble();
        };

        // The first show measures the card while it is still unarranged, so
        // its real height arrives with this event; re-anchor once it is known.
        VolumeBubble.SizeChanged += (_, _) => PositionVolumeBubble();
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

    /// <summary>
    /// Mini mode is mouse-only chrome: every button, slider and playlist row is
    /// taken out of the keyboard focus track, so Tab cannot park focus on a
    /// control and Space/Enter/arrow keys keep their player meaning. The
    /// original values are restored when mini mode ends, because the same
    /// controls are reused the next time mini mode is entered.
    /// </summary>
    public void SetKeyboardMouseOnly(bool mouseOnly)
    {
        if (mouseOnly == _mouseOnlyKeyboard)
        {
            return;
        }

        _mouseOnlyKeyboard = mouseOnly;
        if (mouseOnly)
        {
            CaptureAndDisable(this);
            // Rows are created lazily by the ListView; keep them out of the tab
            // order as they appear.
            PlaylistList.ContainerContentChanging -= OnPlaylistContainerChanging;
            PlaylistList.ContainerContentChanging += OnPlaylistContainerChanging;
        }
        else
        {
            PlaylistList.ContainerContentChanging -= OnPlaylistContainerChanging;
            RestoreKeyboardState();
        }
    }

    private void OnPlaylistContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (_mouseOnlyKeyboard && args.ItemContainer is Control container)
        {
            CaptureAndDisable(container);
        }
    }

    private void CaptureAndDisable(DependencyObject root)
    {
        if (root is Control control)
        {
            if (!_keyboardState.ContainsKey(control))
            {
                _keyboardState[control] = (control.IsTabStop, control.AllowFocusOnInteraction);
            }

            control.IsTabStop = false;
            control.AllowFocusOnInteraction = false;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            CaptureAndDisable(VisualTreeHelper.GetChild(root, i));
        }
    }

    private void RestoreKeyboardState()
    {
        foreach (KeyValuePair<Control, (bool IsTabStop, bool AllowFocusOnInteraction)> entry in _keyboardState)
        {
            entry.Key.IsTabStop = entry.Value.IsTabStop;
            entry.Key.AllowFocusOnInteraction = entry.Value.AllowFocusOnInteraction;
        }

        _keyboardState.Clear();
    }

    public void SetAnimationsEnabled(bool enabled) => _animationsEnabled = enabled;

    /// <summary>
    /// One-line snapshot of the surfaces that decide the mini-mode background
    /// colour, written to the trace when mini mode is entered so a light-theme
    /// regression can be attributed to a specific layer.
    /// </summary>
    public string DescribeThemeSurfaces()
    {
        static string ColorOf(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color.ToString(),
            LinearGradientBrush gradient => $"gradient({gradient.GradientStops.Count})",
            null => "null",
            _ => brush.GetType().Name,
        };

        return $"theme={ActualTheme} root={ColorOf(Root.Background)} " +
            $"cover={ColorOf(FallbackCoverSurface.Background)} coverVisible={FallbackCover.Visibility} " +
            $"info={ColorOf(InfoPanel.Background)} controls={ColorOf(ControlArea.Background)} " +
            $"playlist={ColorOf(PlaylistPanel.Background)}";
    }

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
            FallbackInitial.Text = ResolveFallbackInitial(resolvedTitle, artist, album);
        }
    }

    /// <summary>
    /// Cover fallback letter. File names often start with a bracket/punctuation
    /// tag ("[MV] Artist - Title", "(01) …"), which used to become the visible
    /// initial; take the first letter or digit instead, then the album/artist,
    /// then the music note.
    /// </summary>
    private static string ResolveFallbackInitial(string? title, string? artist, string? album)
    {
        foreach (string? candidate in new[] { title, album, artist })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            foreach (char character in candidate)
            {
                if (char.IsLetterOrDigit(character))
                {
                    return char.ToUpperInvariant(character).ToString();
                }
            }
        }

        return "♪";
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
            VolumeText.Text = muted ? Loc.T("mini.muted") : $"{Math.Round(VolumeSlider.Value)}%";
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
            VolumeText.Text = _muted ? Loc.T("mini.muted") : $"{Math.Round(clamped)}%";
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
        PositionVolumeBubble();
        _volumeHideTimer.Stop();
        _volumeHideTimer.Start();
    }

    public void HideVolumeBubble()
    {
        _volumeHideTimer.Stop();
        VolumeBubble.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Anchors the 148 DIP bubble 8 DIP above MuteButton and clamps it inside
    /// the mini window. The card lives on <see cref="BubbleLayer"/> (a Canvas
    /// above the whole surface), so both axes are explicit and follow the
    /// window while the playlist changes its height.
    /// </summary>
    private void PositionVolumeBubble()
    {
        if (_positioningBubble ||
            VolumeBubble.Visibility != Visibility.Visible ||
            Root.ActualWidth <= 0 ||
            Root.ActualHeight <= 0)
        {
            return;
        }

        _positioningBubble = true;
        try
        {
            // Measure with the fixed width so the top edge is known on the
            // first show, before the Canvas has arranged the card.
            VolumeBubble.Measure(new Windows.Foundation.Size(
                MiniPlayerLayout.VolumeBubbleWidth,
                double.PositiveInfinity));
            double width = MiniPlayerLayout.VolumeBubbleWidth;
            double height = VolumeBubble.ActualHeight > 0
                ? VolumeBubble.ActualHeight
                : VolumeBubble.DesiredSize.Height;

            Windows.Foundation.Point anchor;
            try
            {
                anchor = MuteButton
                    .TransformToVisual(Root)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
            }
            catch
            {
                // Not in the visual tree yet; the next SizeChanged repositions.
                return;
            }

            double left = anchor.X + ((MuteButton.ActualWidth - width) / 2);
            double top = anchor.Y - height - VolumeBubbleGap;
            left = Math.Clamp(
                left,
                BubbleEdgePadding,
                Math.Max(BubbleEdgePadding, Root.ActualWidth - width - BubbleEdgePadding));
            top = Math.Clamp(
                top,
                BubbleEdgePadding,
                Math.Max(BubbleEdgePadding, Root.ActualHeight - height - BubbleEdgePadding));

            Canvas.SetLeft(VolumeBubble, left);
            Canvas.SetTop(VolumeBubble, top);
        }
        finally
        {
            _positioningBubble = false;
        }
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

        // P0-4: a pointer move without the left button held must never move the
        // window. The compact mini window used to keep an old _dragCandidate
        // after a child (slider/button) swallowed the Released event, so simply
        // hovering dragged the window around.
        if (!WindowInterop.IsKeyDown(VirtualKey.LeftButton))
        {
            EndDrag();
            return;
        }

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
            EndDrag();
            return;
        }

        // Only a press on a non-interactive surface starts a window drag. A
        // press on a transport button, a slider, a playlist row, the volume
        // bubble or the playlist panel belongs to that control; otherwise
        // dragging the progress/volume thumb moved the window instead.
        if (IsInteractiveSurface(e.OriginalSource))
        {
            EndDrag();
            return;
        }

        _dragCandidate = true;
        _dragging = false;
        _dragStart = e.GetCurrentPoint(this).Position;
    }

    /// <summary>
    /// Walks the OriginalSource ancestry: anything that owns a pointer gesture
    /// (buttons, range controls, list rows, text entry, scroll bars, the two
    /// floating panels) must not turn into a window drag.
    /// </summary>
    private bool IsInteractiveSurface(object? source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null && !ReferenceEquals(current, Root))
        {
            if (current is ButtonBase or RangeBase or ListViewItem or TextBox or ScrollBar)
            {
                return true;
            }

            if (ReferenceEquals(current, VolumeBubble) ||
                ReferenceEquals(current, PlaylistPanel))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>
    /// Clears the drag gesture, raising <see cref="DragEnded"/> so the window
    /// position is persisted.
    /// </summary>
    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            _dragCandidate = false;
            DragEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dragCandidate = false;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
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
        // Moving the window can drop XAML pointer capture mid-gesture. While
        // the left button is still down the drag must survive (the pointer is
        // also captured by the window that follows it); only a real release
        // ends the gesture.
        if (_dragging && WindowInterop.IsKeyDown(VirtualKey.LeftButton))
        {
            return;
        }

        EndDrag();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // A release delivered to a child control leaves the root press state
        // behind; drop it when the button is no longer down.
        if (!WindowInterop.IsKeyDown(VirtualKey.LeftButton))
        {
            EndDrag();
        }

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

    /// <summary>
    /// The transport volume button only opens the anchored bubble (P1-5: it
    /// used to need a second click to actually mute). Muting lives on the
    /// bubble's own mute button.
    /// </summary>
    private void OnMuteClick(object sender, RoutedEventArgs e) => ShowVolumeBubble();

    private void OnBubbleMuteClick(object sender, RoutedEventArgs e)
    {
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
