using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.App.Interop;
using Wiina.App.Localization;

namespace Wiina.App.Controls;

public sealed class SidebarTabRequestedEventArgs : EventArgs
{
    public SidebarTabRequestedEventArgs(SidebarType type) => Type = type;

    public SidebarType Type { get; }
}

public sealed class SidebarWidthRequestedEventArgs : EventArgs
{
    public SidebarWidthRequestedEventArgs(double width) => Width = width;

    public double Width { get; }
}

/// <summary>
/// One sidebar slot: tab strip, close button, content host and the 4 DIP
/// inner-edge width grip. The container itself is animation-agnostic; the
/// shell tells it how to enter/leave so system animation settings are honored.
/// </summary>
public sealed partial class SidebarHost : UserControl
{
    private readonly DispatcherQueueTimer _animTimer;
    private SidebarType? _currentType;
    private bool _dragging;
    private double _lastPointerX;
    private bool _hiding;
    private Action? _hideCompleted;
    private double _animFromOffset;
    private double _animToOffset;
    private double _animFromOpacity;
    private double _animToOpacity;
    private long _animStart;

    public SidebarHost()
    {
        InitializeComponent();
        _animTimer = DispatcherQueue.CreateTimer();
        _animTimer.Interval = TimeSpan.FromMilliseconds(15);
        _animTimer.IsRepeating = true;
        _animTimer.Tick += (_, _) => OnAnimationTick();
        SetSide(SidebarSide.Trailing);

        // Resize from the inner 6 DIP, handled on the root so a 4 DIP child
        // cannot lose hit-testing to the panel background.
        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnGripPointerPressed),
            handledEventsToo: true);
        RootGrid.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnGripPointerMoved),
            handledEventsToo: true);
        RootGrid.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnGripPointerReleased),
            handledEventsToo: true);
        RootGrid.PointerCaptureLost += OnGripPointerCaptureLost;
    }

    /// <summary>Optional diagnostic sink wired by the shell to its trace file.</summary>
    public static Action<string>? Log;

    public event EventHandler? CloseClicked;

    public event EventHandler<SidebarTabRequestedEventArgs>? TabRequested;

    /// <summary>Raised when the quick-settings page locator changes page.</summary>
    public event EventHandler<QuickSettingsPage>? PageRequested;

    /// <summary>Raised continuously while the grip is dragged (unclamped DIP).</summary>
    public event EventHandler<SidebarWidthRequestedEventArgs>? WidthRequested;

    /// <summary>Raised once on pointer release so the shell can persist.</summary>
    public event EventHandler? WidthChangeCompleted;

    public SidebarSide Side { get; private set; } = SidebarSide.Trailing;

    public SidebarType? CurrentType => _currentType;

    public double SlideOffset => SlideTransform.TranslateX;

    public QuickSettingsView QuickSettings => SettingsContent;

    public PlaylistView Playlist => PlaylistContent;

    public ChaptersView Chapters => ChaptersContent;

    public PluginsView Plugins => PluginsContent;

    public void SetSide(SidebarSide side)
    {
        Side = side;
        bool trailing = side == SidebarSide.Trailing;

        PanelBorder.BorderThickness = trailing
            ? new Thickness(1, 0, 0, 0)
            : new Thickness(0, 0, 1, 0);
        ResizeGrip.HorizontalAlignment = trailing
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CloseButton.HorizontalAlignment = trailing
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        CloseButton.Content = "\uE711";
        ToolTipService.SetToolTip(CloseButton, Loc.T("sidebar.close"));
    }

    public void SetWidth(double width) => Width = Math.Max(0, width);

    public void SetType(SidebarType? type)
    {
        _currentType = type;
        SettingsContent.Visibility = type == SidebarType.Settings
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistContent.Visibility = type == SidebarType.Playlist
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChaptersContent.Visibility = type == SidebarType.Chapters
            ? Visibility.Visible
            : Visibility.Collapsed;
        PluginsContent.Visibility = type == SidebarType.Plugins
            ? Visibility.Visible
            : Visibility.Collapsed;

        // There is only one navigator: all seven destinations are always
        // visible. Playlist/chapters/plugins are pages of that same row, not a
        // second four-item category layer.
        CategoryTabs.Visibility = Visibility.Collapsed;
        PageTabs.Visibility = Visibility.Visible;
        SettingsTab.IsChecked = false;
        PlaylistTab.IsChecked = false;
        ChaptersTab.IsChecked = false;
        PluginsTab.IsChecked = false;

        LayoutTab.IsChecked = false;
        VideoTab.IsChecked = false;
        AudioTab.IsChecked = false;
        SubtitlesTab.IsChecked = false;
        PlaylistPageTab.IsChecked = false;
        ChaptersPageTab.IsChecked = false;
        PluginsPageTab.IsChecked = false;

        UpdatePageChecks(SettingsContent.CurrentPage, type ?? SidebarType.Settings);
    }

    public void SetPage(QuickSettingsPage page)
    {
        SettingsContent.ShowPage(page);

        UpdatePageChecks(page, _currentType ?? SidebarType.Settings);
    }

    private void UpdatePageChecks(QuickSettingsPage page, SidebarType type)
    {
        bool settings = type == SidebarType.Settings;
        LayoutTab.IsChecked = settings && page == QuickSettingsPage.Layout;
        VideoTab.IsChecked = settings && page == QuickSettingsPage.Video;
        AudioTab.IsChecked = settings && page == QuickSettingsPage.Audio;
        SubtitlesTab.IsChecked = settings && page == QuickSettingsPage.Subtitles;

        PlaylistPageTab.IsChecked = type == SidebarType.Playlist;
        ChaptersPageTab.IsChecked = type == SidebarType.Chapters;
        PluginsPageTab.IsChecked = type == SidebarType.Plugins;

        UpdatePageLabels();
        RefreshPageUnderlines();
    }

    /// <summary>
    /// WinUI's ToggleButton visual states occasionally keep the checked
    /// underline after IsChecked flips to false (and therefore leave two
    /// underlines visible). Drive the rail opacity explicitly so exactly one
    /// item can ever show a rail.
    /// </summary>
    private void RefreshPageUnderlines()
    {
        ToggleButton[] tabs =
        {
            LayoutTab, VideoTab, AudioTab, SubtitlesTab,
            PlaylistPageTab, ChaptersPageTab, PluginsPageTab,
        };

        foreach (ToggleButton tab in tabs)
        {
            tab.ApplyTemplate();
            SetUnderlineOpacity(tab, tab.IsChecked == true ? 1 : 0);
        }
    }

    private static void SetUnderlineOpacity(DependencyObject root, double opacity)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is Border border &&
                string.Equals(border.Name, "NavUnderline", StringComparison.Ordinal))
            {
                border.Opacity = opacity;
                return;
            }

            SetUnderlineOpacity(child, opacity);
        }
    }

    /// <summary>
    /// Quick-settings locator labels only the current target; the rest stay
    /// icon-only so all seven entries fit without shrinking their names.
    /// </summary>
    private void UpdatePageLabels()
    {
        bool settings = _currentType == SidebarType.Settings;
        QuickSettingsPage page = SettingsContent.CurrentPage;

        LayoutLabel.Visibility = settings && page == QuickSettingsPage.Layout
            ? Visibility.Visible
            : Visibility.Collapsed;
        VideoLabel.Visibility = settings && page == QuickSettingsPage.Video
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioLabel.Visibility = settings && page == QuickSettingsPage.Audio
            ? Visibility.Visible
            : Visibility.Collapsed;
        SubtitlesLabel.Visibility = settings && page == QuickSettingsPage.Subtitles
            ? Visibility.Visible
            : Visibility.Collapsed;

        PagePlaylistLabel.Visibility = _currentType == SidebarType.Playlist
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageChaptersLabel.Visibility = _currentType == SidebarType.Chapters
            ? Visibility.Visible
            : Visibility.Collapsed;
        PagePluginsLabel.Visibility = _currentType == SidebarType.Plugins
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void ShowInstant()
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        SlideTransform.TranslateX = 0;
        Opacity = 1;
        Visibility = Visibility.Visible;
    }

    public void PlayShow(SidebarAnimationMode mode)
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        Visibility = Visibility.Visible;

        if (mode == SidebarAnimationMode.None)
        {
            SlideTransform.TranslateX = 0;
            Opacity = 1;
            return;
        }

        _animFromOffset = mode == SidebarAnimationMode.Fade
            ? 0
            : Side == SidebarSide.Leading ? -Width : Width;
        _animToOffset = 0;
        _animFromOpacity = mode == SidebarAnimationMode.Fade ? 0 : 1;
        _animToOpacity = 1;
        SlideTransform.TranslateX = _animFromOffset;
        Opacity = _animFromOpacity;
        StartAnimation();
    }

    public void PlayHide(SidebarAnimationMode mode, Action? completed)
    {
        _animTimer.Stop();

        if (mode == SidebarAnimationMode.None)
        {
            CollapseInstant();
            completed?.Invoke();
            return;
        }

        _hiding = true;
        _hideCompleted = completed;
        _animFromOffset = mode == SidebarAnimationMode.Fade
            ? 0
            : SlideTransform.TranslateX;
        _animToOffset = Side == SidebarSide.Leading ? -Width : Width;
        _animFromOpacity = Opacity;
        _animToOpacity = 0;
        StartAnimation();
    }

    public void CollapseInstant()
    {
        _animTimer.Stop();
        _hiding = false;
        _hideCompleted = null;
        Visibility = Visibility.Collapsed;
        SlideTransform.TranslateX = 0;
        Opacity = 1;
        LayoutTab.IsChecked = false;
        VideoTab.IsChecked = false;
        AudioTab.IsChecked = false;
        SubtitlesTab.IsChecked = false;
        PlaylistPageTab.IsChecked = false;
        ChaptersPageTab.IsChecked = false;
        PluginsPageTab.IsChecked = false;
    }

    private void StartAnimation()
    {
        _animStart = Stopwatch.GetTimestamp();
        _animTimer.Start();
    }

    private void OnAnimationTick()
    {
        double elapsed = (Stopwatch.GetTimestamp() - _animStart) /
            (double)Stopwatch.Frequency;
        double progress = Math.Clamp(elapsed / SidebarState.AnimationSeconds, 0, 1);
        double eased = EaseInOut(progress);

        SlideTransform.TranslateX =
            _animFromOffset + ((_animToOffset - _animFromOffset) * eased);
        Opacity = _animFromOpacity + ((_animToOpacity - _animFromOpacity) * eased);

        if (progress < 1)
        {
            return;
        }

        _animTimer.Stop();
        if (_hiding)
        {
            Action? completed = _hideCompleted;
            CollapseInstant();
            completed?.Invoke();
        }
        else
        {
            SlideTransform.TranslateX = 0;
            Opacity = 1;
        }
    }

    private static double EaseInOut(double t) =>
        t < 0.5
            ? 4 * t * t * t
            : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    private void OnTypeTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } &&
            Enum.TryParse(tag, out SidebarType type))
        {
            TabRequested?.Invoke(this, new SidebarTabRequestedEventArgs(type));
        }
    }

    private void OnPageTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse(tag, out QuickSettingsPage page))
        {
            // The four quick-settings pages are hosted by the settings slot.
            // While playlist/chapters/plugins is on screen, swapping only the
            // hidden page leaves the rail on the other surface, so switch the
            // slot first and let the shell re-apply the type before the page
            // below is shown. Toggle state is rebuilt by SetType/SetPage.
            if (_currentType != SidebarType.Settings)
            {
                TabRequested?.Invoke(this, new SidebarTabRequestedEventArgs(SidebarType.Settings));
            }

            SetPage(page);
            PageRequested?.Invoke(this, page);
            return;
        }

        if (Enum.TryParse(tag, out SidebarType type))
        {
            TabRequested?.Invoke(this, new SidebarTabRequestedEventArgs(type));
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseClicked?.Invoke(this, EventArgs.Empty);

    private void OnGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(RootGrid);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double x = point.Position.X;
        bool nearInnerEdge = Side == SidebarSide.Trailing
            ? x <= 6
            : x >= RootGrid.ActualWidth - 6;
        if (!nearInnerEdge)
        {
            return;
        }

        Log?.Invoke(
            $"resize press side={Side} x={x:0.0} actual={RootGrid.ActualWidth:0}");

        _dragging = true;
        _lastPointerX = WindowInterop.TryGetCursorPosition(out int cursorX, out _)
            ? cursorX
            : 0;
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        if (!WindowInterop.TryGetCursorPosition(out int x, out _))
        {
            return;
        }

        double delta = x - _lastPointerX;
        _lastPointerX = x;
        double scale = XamlRoot?.RasterizationScale ?? 1.0;
        if (scale > 0.01)
        {
            delta /= scale;
        }

        double proposed = Width + (Side == SidebarSide.Trailing ? -delta : delta);
        Log?.Invoke($"move delta={delta:0.0} proposed={proposed:0}");
        WidthRequested?.Invoke(this, new SidebarWidthRequestedEventArgs(proposed));
        e.Handled = true;
    }

    private void OnGripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        try
        {
            RootGrid.ReleasePointerCapture(e.Pointer);
        }
        catch
        {
            // Pointer may already be gone.
        }

        Log?.Invoke($"release width={Width:0}");
        WidthChangeCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnGripPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        WidthChangeCompleted?.Invoke(this, EventArgs.Empty);
    }
}
