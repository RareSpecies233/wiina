using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Wiina.App.Controls;
using Wiina.App.Services;
using Wiina.Core.Models;
using Wiina.App.Localization;
using Wiina.Core.Osd;
using Wiina.Core.Settings;
using Wiina.Core.State;
using Wiina.Core.Text;

namespace Wiina.App;

/// <summary>
/// Stage 5: OSD placement/messages, cache status, fullscreen clock/battery and
/// the timeline thumbnail peek. Kept out of the shell file so the Stage 1–4
/// layout code stays readable.
/// </summary>
public sealed partial class MainWindow
{
    private readonly OsdQueue _osdQueue = new();
    private ThumbnailService? _thumbnailService;
    private CancellationTokenSource? _thumbnailCts;
    private long _thumbnailRequest;
    private TimelineControl? _previewTimeline;
    private double _previewRatio;
    private double _previewPointerX;
    private bool _previewAboveTimeline;
    // Content size of TimePreview without its positioning margin. DesiredSize
    // includes Margin in WinUI, so reusing it after moving the card made the
    // width grow with the previous position and the preview drift.
    private double _previewWidth;
    private double _previewHeight;
    private bool _cacheBuffering;
    private double _cachePercent = 100;
    private double _cacheSpeed;
    private bool _pausedForCache;
    private bool _seekingProperty;
    private bool _openingStream;
    private string? _streamTarget;
    private DispatcherQueueTimer? _fullscreenInfoTimer;
    private DateTimeOffset _lastBatterySample;
    private int? _batteryPercent;
    private bool _batteryCharging;
    private bool _onAcPower;

    private bool IsFullscreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    private void InitializeStage5()
    {
        Osd.SetEnabled(_settings.OsdEnabled);
        Osd.SetScale(_settings.OsdSizePercent);
        Osd.SetDuration(OsdSettings.Duration(_settings.OsdDurationSeconds));
        Osd.SetAnimationsEnabled(AnimationsEnabled);

        // The statistics card shares RootGrid's element-level theme; mirroring
        // it like OsdText keeps its ThemeResource brushes from keeping the
        // app-theme values used at construction (issue 6 dark-mode text).
        StatsPanel.RequestedTheme = RootGrid.RequestedTheme;
        RootGrid.ActualThemeChanged += (_, _) => StatsPanel.RequestedTheme = RootGrid.ActualTheme;

        FullscreenStatus.Configure(
            _settings.FullscreenClockEnabled,
            _settings.FullscreenBatteryEnabled,
            _settings.FullscreenTitleEnabled);

        _fullscreenInfoTimer = DispatcherQueue.CreateTimer();
        _fullscreenInfoTimer.Interval = TimeSpan.FromSeconds(1);
        _fullscreenInfoTimer.IsRepeating = true;
        _fullscreenInfoTimer.Tick += (_, _) => UpdateFullscreenStatus();

        UpdateOsdPlacement();
        UpdateFullscreenStatus();
    }

    private void ApplyStage5Settings()
    {
        Osd.SetEnabled(_settings.OsdEnabled);
        Osd.SetScale(_settings.OsdSizePercent);
        Osd.SetDuration(OsdSettings.Duration(_settings.OsdDurationSeconds));
        Osd.SetAnimationsEnabled(AnimationsEnabled);
        FullscreenStatus.Configure(
            _settings.FullscreenClockEnabled,
            _settings.FullscreenBatteryEnabled,
            _settings.FullscreenTitleEnabled);
        UpdateOsdPlacement();
        UpdateFullscreenStatus();
        ApplyStatsSettings();
    }

    private void ShowOsd(OsdMessage message)
    {
        OsdQueueResult result = _osdQueue.Enqueue(message);
        Osd.Show(message);
        UpdateOsdPlacement();
        Trace($"OSD [{message.Kind}] coalesced={result.Coalesced}");
    }

    private string ShortStreamName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var uri = new Uri(path);
            string name = uri.AbsolutePath;
            if (name.Length > 42)
            {
                name = name[^42..];
            }

            return string.IsNullOrWhiteSpace(name) || name == "/"
                ? uri.Host
                : uri.Host + name;
        }
        catch
        {
            return path.Length > 48 ? path[..48] : path;
        }
    }

    // ------------------------------------------------------------ OSD geometry

    private void UpdateOsdPlacement()
    {
        (double top, double bottom, double right, double left) = OverlayInsets();

        OsdPosition position = _settings.OsdPosition;
        bool topPosition = OsdPositions.IsTop(position);
        bool leftPosition = OsdPositions.IsLeft(position);
        bool centered = OsdPositions.IsCenter(position);
        HorizontalAlignment horizontal = centered
            ? HorizontalAlignment.Center
            : leftPosition
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        VerticalAlignment vertical = topPosition
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;

        Osd.HorizontalAlignment = horizontal;
        Osd.VerticalAlignment = vertical;
        Osd.Margin = new Thickness(
            centered ? 8 : leftPosition ? left : 0,
            topPosition ? top : 0,
            centered ? 8 : leftPosition ? 0 : right,
            topPosition ? 0 : bottom);
        Osd.SetAlignment(horizontal, vertical);

        UpdateStatsPlacement(top, bottom, right, left);
    }

    /// <summary>
    /// Insets shared by the overlay cards (OSD and statistics): bottom-anchored
    /// cards stay clear of the OSC and top-anchored cards clear of the title bar
    /// or top OSC; fullscreen drops the chrome allowances.
    /// </summary>
    private (double Top, double Bottom, double Right, double Left) OverlayInsets()
    {
        double bottom = 8;
        double top = 8;
        double right = 8;
        double left = 8;
        bool fullscreen = IsFullscreen;
        bool chromeVisible = _chromeShown || _settings.Layout == WindowLayoutMode.Docked;

        if (fullscreen || !chromeVisible)
        {
            bottom = 8;
            top = 8;
        }
        else if (_settings.OscPosition == OscPosition.Bottom)
        {
            bottom = 72;
        }
        else if (_settings.OscPosition == OscPosition.Floating &&
                 _settings.Layout == WindowLayoutMode.EdgeToEdge)
        {
            bottom = 76;
        }
        else if (_settings.Layout == WindowLayoutMode.Docked &&
                 _settings.OscPosition != OscPosition.Top)
        {
            bottom = 72;
        }

        if (!fullscreen && chromeVisible && _settings.OscPosition == OscPosition.Top)
        {
            top = 72;
        }
        else if (!fullscreen && _titleBarVisible)
        {
            top = 48;
        }

        if (fullscreen && (FullscreenStatus.IsShowing ||
                           (_settings.FullscreenClockEnabled ||
                            _settings.FullscreenBatteryEnabled ||
                            _settings.FullscreenTitleEnabled)))
        {
            bottom = Math.Max(bottom, 48);
        }

        return (top, bottom, right, left);
    }

    /// <summary>
    /// Places the statistics card on the same six OSD positions and with the
    /// same insets, so it respects the OSD position setting and never covers
    /// the OSC or title bar (issue 6).
    /// </summary>
    private void UpdateStatsPlacement(double top, double bottom, double right, double left)
    {
        OsdPosition position = _settings.OsdPosition;
        bool topPosition = OsdPositions.IsTop(position);
        bool leftPosition = OsdPositions.IsLeft(position);
        bool centered = OsdPositions.IsCenter(position);

        StatsPanel.HorizontalAlignment = centered
            ? HorizontalAlignment.Center
            : leftPosition
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
        StatsPanel.VerticalAlignment = topPosition
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        StatsPanel.Margin = new Thickness(
            centered ? 8 : leftPosition ? left : 0,
            topPosition ? top : 0,
            centered ? 8 : leftPosition ? 0 : right,
            topPosition ? 0 : bottom);
    }

    private bool OsdCoversTimeline()
    {
        if (!Osd.IsShowing || _previewTimeline is null)
        {
            return false;
        }

        try
        {
            Point osdOrigin = Osd.TransformToVisual(OverlayLayer).TransformPoint(new Point(0, 0));
            Point timelineOrigin = _previewTimeline.TransformToVisual(OverlayLayer)
                .TransformPoint(new Point(0, 0));
            Rect osd = new(
                osdOrigin.X,
                osdOrigin.Y,
                Osd.ActualWidth,
                Osd.ActualHeight);
            Rect timeline = new(
                timelineOrigin.X,
                timelineOrigin.Y,
                _previewTimeline.ActualWidth,
                _previewTimeline.ActualHeight);
            return Intersects(osd, timeline);
        }
        catch
        {
            return false;
        }
    }

    private static bool Intersects(Rect a, Rect b) =>
        a.X < b.X + b.Width &&
        a.X + a.Width > b.X &&
        a.Y < b.Y + b.Height &&
        a.Y + a.Height > b.Y;

    // ------------------------------------------------------- fullscreen status

    private void UpdateFullscreenStatus()
    {
        bool enabled = _settings.FullscreenClockEnabled ||
            _settings.FullscreenBatteryEnabled ||
            _settings.FullscreenTitleEnabled;
        bool visible = IsFullscreen && _chromeShown && enabled;
        FullscreenStatus.SetVisible(visible);

        if (!visible)
        {
            _fullscreenInfoTimer?.Stop();
            FullscreenStatus.SetVisible(false);
            UpdateOsdPlacement();
            return;
        }

        if (_fullscreenInfoTimer is { IsRunning: false })
        {
            _fullscreenInfoTimer.Start();
        }

        if (DateTimeOffset.UtcNow - _lastBatterySample > TimeSpan.FromSeconds(10))
        {
            SampleBattery();
            _lastBatterySample = DateTimeOffset.UtcNow;
        }

        FullscreenStatus.Update(
            DateTimeOffset.Now,
            _batteryPercent,
            _batteryCharging,
            _onAcPower,
            _settings.FullscreenTitleEnabled ? _currentTitle : null);
        UpdateOsdPlacement();
    }

    private void SampleBattery()
    {
        try
        {
            if (GetSystemPowerStatus(out SystemPowerStatus status))
            {
                _onAcPower = status.ACLineStatus == 1;
                _batteryCharging = (status.BatteryFlag & 8) != 0;
                _batteryPercent = status.BatteryLifePercent == 255
                    ? null
                    : status.BatteryLifePercent;
            }
        }
        catch
        {
            _batteryPercent = null;
        }
    }

    // ---------------------------------------------------------- cache / stream

    private void SetPausedForCache(bool value)
    {
        _pausedForCache = value;
        UpdateBufferStatus();
    }

    private void SetSeekingProperty(bool value)
    {
        _seekingProperty = value;
        UpdateBufferStatus();
    }

    private void SetCacheBuffering(double percent)
    {
        _cachePercent = double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 100;
        _cacheBuffering = _cachePercent > 0 && _cachePercent < 99.5;
        UpdateBufferStatus();
    }

    private void SetCacheSpeed(double bytesPerSecond)
    {
        _cacheSpeed = double.IsFinite(bytesPerSecond) ? Math.Max(0, bytesPerSecond) : 0;
    }

    private void BeginStreamOpen(string path)
    {
        _openingStream = IsUrlPath(path);
        _streamTarget = _openingStream ? ShortStreamName(path) : null;
        UpdateBufferStatus();
    }

    private void EndStreamOpen()
    {
        _openingStream = false;
        _streamTarget = null;
        _cacheBuffering = false;
        _pausedForCache = false;
        _seekingProperty = false;
        UpdateBufferStatus();
    }

    private void UpdateBufferStatus()
    {
        if (_openingStream)
        {
            BufferStatus.Show(Loc.T("osd.opening"), _streamTarget, indeterminate: true);
            return;
        }

        if (_pausedForCache || _cacheBuffering)
        {
            string detail = _cachePercent > 0
                ? $"{Math.Round(_cachePercent)}%" + FormatCacheSpeed()
                : Loc.T("osd.bufferReading");
            BufferStatus.Show(Loc.T("osd.buffering"), detail, indeterminate: false);
            return;
        }

        if (_seekingProperty && !string.IsNullOrWhiteSpace(_currentPath) &&
            IsUrlPath(_currentPath))
        {
            BufferStatus.Show(Loc.T("osd.seeking"), _streamTarget, indeterminate: true);
            return;
        }

        BufferStatus.Hide();
    }

    private string FormatCacheSpeed()
    {
        if (_cacheSpeed < 1024)
        {
            return string.Empty;
        }

        double kilo = _cacheSpeed / 1024;
        return kilo < 1024
            ? $" · {kilo:0} KB/s"
            : $" · {kilo / 1024:0.0} MB/s";
    }

    // -------------------------------------------------------- time preview UX

    private async void RequestThumbnailAsync(double seconds)
    {
        if (!_settings.ThumbnailPreviewEnabled ||
            string.IsNullOrWhiteSpace(_currentPath) ||
            !File.Exists(_currentPath) ||
            _videoWidth <= 0)
        {
            ThumbnailPeek.Hide();
            return;
        }

        long request = ++_thumbnailRequest;
        _thumbnailCts?.Cancel();
        var cts = new CancellationTokenSource();
        _thumbnailCts = cts;

        try
        {
            ThumbnailFrame? frame = await GetThumbnailService().GetAsync(
                _currentPath,
                seconds,
                90,
                cts.Token);
            if (cts.IsCancellationRequested ||
                request != _thumbnailRequest ||
                !ThumbnailPeek.IsShowing ||
                TimePreview.Visibility != Visibility.Visible)
            {
                return;
            }

            if (frame is null)
            {
                ThumbnailPeek.Hide();
                return;
            }

            var bitmap = new WriteableBitmap(frame.Width, frame.Height);
            using (Stream stream = bitmap.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(frame.Bgra);
            }

            bitmap.Invalidate();
            ThumbnailPeek.Show(
                bitmap,
                TimeFormatter.Format(seconds),
                frame.Width,
                frame.Height);
            PositionThumbnail();
        }
        catch (OperationCanceledException)
        {
            // A newer hover position owns the overlay.
        }
        catch (Exception ex)
        {
            Trace("thumbnail request failed: " + ex.Message);
            ThumbnailPeek.Hide();
        }
        finally
        {
            if (request == _thumbnailRequest)
            {
            }
        }
    }

    private ThumbnailService GetThumbnailService() =>
        _thumbnailService ??= new ThumbnailService(
            Math.Clamp(_settings.ThumbnailCacheCount, 1, 240),
            Math.Clamp(_settings.ThumbnailBucketSeconds, 1, 30),
            Trace);

    private void PositionThumbnail()
    {
        if (!ThumbnailPeek.IsShowing || _previewTimeline is null)
        {
            return;
        }

        try
        {
            double width = ThumbnailPeek.ActualWidth > 0
                ? ThumbnailPeek.ActualWidth
                : ThumbnailPeek.Width;
            double height = ThumbnailPeek.ActualHeight > 0
                ? ThumbnailPeek.ActualHeight
                : ThumbnailPeek.Height;
            double previewLeft = TimePreview.Margin.Left;
            double previewTop = TimePreview.Margin.Top;
            double previewWidth = _previewWidth > 0
                ? _previewWidth
                : Math.Max(0, TimePreview.ActualWidth);
            double previewHeight = _previewHeight > 0
                ? _previewHeight
                : Math.Max(0, TimePreview.ActualHeight);
            double centerX = previewLeft + previewWidth / 2;
            double left = Math.Clamp(
                centerX - width / 2,
                8,
                Math.Max(8, OverlayLayer.ActualWidth - width - 8));

            // Keep the thumbnail glued to the preview card, not to the raw
            // pointer, so edge clamping cannot visually separate the two.
            double top = _previewAboveTimeline
                ? previewTop - height - 5
                : previewTop + previewHeight + 5;
            if (top < 8)
            {
                top = previewTop + previewHeight + 5;
            }

            top = Math.Clamp(top, 8, Math.Max(8, OverlayLayer.ActualHeight - height - 8));
            ThumbnailPeek.Margin = new Thickness(left, top, 0, 0);
        }
        catch
        {
            // Layout can race a collapse; the next hover repositions it.
        }
    }

    private void DisposeStage5()
    {
        _fullscreenInfoTimer?.Stop();
        _fullscreenInfoTimer = null;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _thumbnailService?.Dispose();
        _thumbnailService = null;
        ThumbnailPeek.Hide();
    }

    // ------------------------------------------------------------- interop

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
