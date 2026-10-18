using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Wiina.App.Controls;
using Wiina.Mpv;
namespace Wiina.App.Interop;

/// <summary>
/// Live blurred backdrop for the chrome that floats over the video surface.
///
/// The XAML acrylic/backdrop brushes cannot see a SwapChainPanel (the pixels
/// live in a separate compositor layer), so this service pulls the real frame
/// straight out of libmpv, reduces it, blurs it with a Win2D
/// <see cref="GaussianBlurEffect"/> on a worker thread and paints the blurred
/// crop behind each chrome surface. Every failure path keeps the acrylic look:
/// the layers simply never become visible.
/// </summary>
internal sealed class ChromeBlur
{
    private const int TargetPixels = 320;
    private const int FrameIntervalMs = 125;
    private const float BlurAmount = 9f;
    private static readonly Lazy<ChromeBlur> LazyInstance =
        new(() => new ChromeBlur());
    public static ChromeBlur Current => LazyInstance.Value;
    private readonly object _gate = new();
    private readonly List<WeakReference<ChromeBlurLayer>> _layers = new();
    private readonly FieldInfo? _playerField =
        typeof(MainWindow).GetField(
            "_player",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private DispatcherQueue? _queue;
    private CanvasDevice? _device;
    private Thread? _thread;
    private volatile bool _running;
    private int _failureLogged;
    private WriteableBitmap? _bitmap;
    private byte[]? _pending;
    private int _pendingWidth;
    private int _pendingHeight;
    private int _queued;
    private bool _osdInjected;
    private long _osdScanAt;
    private ChromeBlur()
    {
    }
    /// <summary>Adds a blur layer over <paramref name="host"/>; UI thread.</summary>
    public static bool TryAttach(
        FrameworkElement host,
        double cornerRadius,
        out ChromeBlurLayer layer)
    {
        // Kill switch for support/AB comparison: keeps the acrylic fallback.
        if (Environment.GetEnvironmentVariable("WIINA_BLUR_DISABLE") == "1")
        {
            layer = null!;
            return false;
        }
        layer = new ChromeBlurLayer(cornerRadius);
        if (host is Border border)
        {
            border.Child = layer;
        }
        else if (host is Panel panel)
        {
            panel.Children.Add(layer);
        }
        else
        {
            return false;
        }
        return true;
    }
    internal void Register(ChromeBlurLayer layer)
    {
        lock (_gate)
        {
            foreach (WeakReference<ChromeBlurLayer> existing in _layers)
            {
                if (existing.TryGetTarget(out ChromeBlurLayer? target) &&
                    ReferenceEquals(target, layer))
                {
                    return;
                }
            }
            _layers.Add(new WeakReference<ChromeBlurLayer>(layer));
        }
        Start();
    }
    internal void Unregister(ChromeBlurLayer layer)
    {
        lock (_gate)
        {
            _layers.RemoveAll(reference =>
                !reference.TryGetTarget(out ChromeBlurLayer? target) ||
                ReferenceEquals(target, layer));
        }
    }
    private void Start()
    {
        if (_thread is not null)
        {
            return;
        }
        _queue ??= DispatcherQueue.GetForCurrentThread();
        if (_queue is null)
        {
            return;
        }
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "wiina-chrome-blur",
        };
        _thread.Start();
    }
    private void Loop()
    {
        try
        {
            // A dedicated shared device keeps Win2D work off the UI thread.
            _device = CanvasDevice.GetSharedDevice();
        }
        catch (Exception ex)
        {
            LogOnce("Win2D device unavailable: " + ex.Message);
            return;
        }
        while (_running)
        {
            long started = Environment.TickCount64;
            try
            {
                MpvPlayer? player = ResolvePlayer();
                if (player is { IsRunning: true })
                {
                    MpvScreenshot? shot = player.ScreenshotRaw();
                    if (shot is not null && shot.Pixels.Length > 0)
                    {
                        Blur(shot);
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce("frame capture failed: " + ex.Message);
            }
            int remaining = FrameIntervalMs - (int)(Environment.TickCount64 - started);
            if (remaining > 0)
            {
                Thread.Sleep(remaining);
            }
        }
    }
    private MpvPlayer? ResolvePlayer()
    {
        object? window = App.ActiveWindow;
        return window is null ? null : _playerField?.GetValue(window) as MpvPlayer;
    }
    private void Blur(MpvScreenshot shot)
    {
        int width = Math.Min(TargetPixels, shot.Width);
        int height = Math.Max(
            1,
            (int)Math.Round((double)shot.Height * width / shot.Width));
        byte[] small = Downscale(shot, width, height);
        CanvasDevice? device = _device;
        if (device is null)
        {
            return;
        }
        using CanvasBitmap source = CanvasBitmap.CreateFromBytes(
            device,
            small,
            width,
            height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
            96f);
        using var blur = new GaussianBlurEffect
        {
            Source = source,
            BlurAmount = BlurAmount,
            Optimization = EffectOptimization.Speed,
            BorderMode = EffectBorderMode.Hard,
        };
        using var target = new CanvasRenderTarget(device, width, height, 96f);
        using (CanvasDrawingSession session = target.CreateDrawingSession())
        {
            session.Clear(Color.FromArgb(255, 0, 0, 0));
            session.DrawImage(blur);
        }
        Publish(target.GetPixelBytes(), width, height);
    }
    /// <summary>
    /// Box-filters one raw mpv frame (bgr0/bgra, possibly bottom-up) down to a
    /// small opaque BGRA bitmap. Alpha is forced to 255 because mpv's "bgr0"
    /// format leaves the alpha channel zero.
    /// </summary>
    private static byte[] Downscale(MpvScreenshot shot, int width, int height)
    {
        byte[] source = shot.Pixels;
        int stride = Math.Abs(shot.Stride);
        bool bottomUp = shot.Stride < 0;
        int limit = source.Length - 4;
        var target = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy0 = y * shot.Height / height;
            int sy1 = Math.Max(sy0 + 1, (y + 1) * shot.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sx0 = x * shot.Width / width;
                int sx1 = Math.Max(sx0 + 1, (x + 1) * shot.Width / width);
                long blue = 0;
                long green = 0;
                long red = 0;
                int samples = 0;
                for (int sy = sy0; sy < sy1; sy++)
                {
                    int row = bottomUp ? shot.Height - 1 - sy : sy;
                    int rowOffset = row * stride;
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int index = rowOffset + (sx * 4);
                        if (index < 0 || index > limit)
                        {
                            continue;
                        }
                        blue += source[index];
                        green += source[index + 1];
                        red += source[index + 2];
                        samples++;
                    }
                }
                int offset = (y * width + x) * 4;
                if (samples == 0)
                {
                    target[offset] = 0;
                    target[offset + 1] = 0;
                    target[offset + 2] = 0;
                }
                else
                {
                    target[offset] = (byte)(blue / samples);
                    target[offset + 1] = (byte)(green / samples);
                    target[offset + 2] = (byte)(red / samples);
                }
                target[offset + 3] = 255;
            }
        }
        return target;
    }
    private void Publish(byte[] pixels, int width, int height)
    {
        lock (_gate)
        {
            _pending = pixels;
            _pendingWidth = width;
            _pendingHeight = height;
        }
        if (Interlocked.Exchange(ref _queued, 1) == 0)
        {
            _queue?.TryEnqueue(ApplyPending);
        }
    }
    private void ApplyPending()
    {
        byte[]? pixels;
        int width;
        int height;
        lock (_gate)
        {
            pixels = _pending;
            width = _pendingWidth;
            height = _pendingHeight;
            _pending = null;
        }
        if (pixels is null)
        {
            Volatile.Write(ref _queued, 0);
            return;
        }
        try
        {
            if (_bitmap is null ||
                _bitmap.PixelWidth != width ||
                _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height);
            }
            using (Stream stream = _bitmap.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            _bitmap.Invalidate();
            var alive = new List<ChromeBlurLayer>();
            lock (_gate)
            {
                for (int i = _layers.Count - 1; i >= 0; i--)
                {
                    if (_layers[i].TryGetTarget(out ChromeBlurLayer? layer))
                    {
                        alive.Add(layer);
                    }
                    else
                    {
                        _layers.RemoveAt(i);
                    }
                }
            }
            foreach (ChromeBlurLayer layer in alive)
            {
                layer.Apply(_bitmap);
            }
            if (alive.Count > 0)
            {
                TryInjectOsd(alive[0]);
            }
        }
        catch (Exception ex)
        {
            LogOnce("blur publish failed: " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref _queued, 0);
            bool again;
            lock (_gate)
            {
                again = _pending is not null;
            }
            if (again)
            {
                PublishCurrent();
            }
        }
    }
    /// <summary>
    /// The OSD panel belongs to the OSD control's own template, so it is
    /// upgraded in place: its background border receives a blurred frame layer
    /// plus a translucent tint. The existing background is only replaced when it
    /// is translucent, so the opaque high-contrast system color keeps its look.
    /// </summary>
    private void TryInjectOsd(ChromeBlurLayer anyLayer)
    {
        if (_osdInjected)
        {
            return;
        }
        long now = Environment.TickCount64;
        if (now - _osdScanAt < 1000)
        {
            return;
        }
        _osdScanAt = now;
        if (anyLayer.XamlRoot?.Content is not FrameworkElement root)
        {
            return;
        }
        OsdText? osd = FindOsd(root);
        if (osd is null ||
            VisualTreeHelper.GetChildrenCount(osd) == 0 ||
            VisualTreeHelper.GetChild(osd, 0) is not Border border ||
            border.Child is not UIElement content)
        {
            return;
        }
        if (border.Background is not SolidColorBrush acrylic || acrylic.Color.A >= 255)
        {
            return;
        }
        Color tintColor = osd.ActualTheme == ElementTheme.Light
            ? Color.FromArgb(0xB3, 0xF3, 0xF3, 0xF3)
            : Color.FromArgb(0xB3, 0x10, 0x11, 0x14);
        var grid = new Grid();
        var layer = new ChromeBlurLayer(border.CornerRadius.TopLeft);
        var tint = new Border
        {
            Background = new SolidColorBrush(tintColor),
            CornerRadius = border.CornerRadius,
            IsHitTestVisible = false,
        };
        grid.Children.Add(layer);
        grid.Children.Add(tint);
        // The OSD control re-applies its border padding whenever a message is
        // shown, so the blur/tint grid takes that padding over: the border is
        // kept at zero inset, the message keeps the same inset through its own
        // margin, and the blurred panel covers the whole rounded rectangle.
        Thickness baseMargin = content is FrameworkElement element0
            ? element0.Margin
            : new Thickness(0);
        Thickness inset = border.Padding;
        border.Padding = new Thickness(0);
        border.Background = null;
        border.Child = null;
        if (content is FrameworkElement element)
        {
            element.Margin = new Thickness(
                baseMargin.Left + inset.Left,
                baseMargin.Top + inset.Top,
                baseMargin.Right + inset.Right,
                baseMargin.Bottom + inset.Bottom);
        }
        grid.Children.Add(content);
        border.Child = grid;
        border.LayoutUpdated += (_, _) =>
        {
            Thickness pad = border.Padding;
            if (pad.Left == 0 && pad.Top == 0 && pad.Right == 0 && pad.Bottom == 0)
            {
                return;
            }
            border.Padding = new Thickness(0);
            if (content is FrameworkElement element)
            {
                element.Margin = new Thickness(
                    baseMargin.Left + pad.Left,
                    baseMargin.Top + pad.Top,
                    baseMargin.Right + pad.Right,
                    baseMargin.Bottom + pad.Bottom);
            }
        };
        _osdInjected = true;
    }

    private static OsdText? FindOsd(DependencyObject node)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, i);
            if (child is OsdText osd)
            {
                return osd;
            }
            if (FindOsd(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }

    private void PublishCurrent()
    {
        if (Interlocked.Exchange(ref _queued, 1) == 0)
        {
            _queue?.TryEnqueue(ApplyPending);
        }
    }
    private void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref _failureLogged, 1) == 0)
        {
            Trace.WriteLine("[chrome-blur] " + message);
        }
    }

    private static void Diag(string message)
    {
        if (Environment.GetEnvironmentVariable("WIINA_BLUR_LOG") != "1")
        {
            return;
        }
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wiina-blur.log"),
                "[osd] " + message + "\n");
        }
        catch
        {
        }
    }
}

/// <summary>
/// One chrome surface's blurred backdrop. It holds a single image that shows
/// the region of the blurred video frame currently sitting under the surface,
/// so the layer must be a direct child that covers the surface.
/// </summary>
public sealed class ChromeBlurLayer : Canvas
{
    private readonly Rectangle _fill;
    private readonly ImageBrush _brush;
    private readonly CompositeTransform _transform;
    private readonly double _cornerRadius;
    private WeakReference<MpvVideoView>? _video;
    private static bool _diagLogged;
    internal ChromeBlurLayer(double cornerRadius)
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        _cornerRadius = cornerRadius;
        // The brush samples just the region of the blurred frame behind this
        // surface, so nothing ever needs to paint outside the surface bounds.
        // The brush paints the whole blurred frame at natural size with a
        // scale/offset transform; the Rectangle clips it to this surface, so
        // nothing paints outside the chrome bounds.
        _brush = new ImageBrush
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        _transform = new CompositeTransform();
        _brush.Transform = _transform;
        _fill = new Rectangle
        {
            Fill = _brush,
            RadiusX = cornerRadius,
            RadiusY = cornerRadius,
            IsHitTestVisible = false,
        };
        Children.Add(_fill);
        SizeChanged += (_, _) => UpdateClip();
        LayoutUpdated += (_, _) => UpdateClip();
        Loaded += (_, _) =>
        {
            UpdateClip();
            ChromeBlur.Current.Register(this);
        };
        Unloaded += (_, _) => ChromeBlur.Current.Unregister(this);
    }
    /// <summary>
    /// The brush paints the blurred frame at its natural size, which is larger
    /// than a thin surface such as the titlebar, so clip the overflow away from
    /// the video. A Canvas does not constrain its children, hence the explicit
    /// fill size; the clip is refreshed whenever the surface get its real size.
    /// </summary>
    private void UpdateClip()
    {
        if (ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }
        _fill.Width = ActualWidth;
        _fill.Height = ActualHeight;
        Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, ActualWidth, ActualHeight),
        };
    }
    /// <summary>Raised once the first blurred frame is on screen.</summary>
    public event EventHandler? FirstFrame;
    private bool HasFrame { get; set; }
    private bool _firstFrameRaised;

    /// <summary>
    /// Delivers the first-frame notification to a subscriber that attached after
    /// the layer had already received a frame (the host's Loaded handler can run
    /// after the layer's Loaded, so a plain event subscription races).
    /// </summary>
    internal void RaiseFirstFrameIfReady()
    {
        if (HasFrame && !_firstFrameRaised)
        {
            _firstFrameRaised = true;
            FirstFrame?.Invoke(this, EventArgs.Empty);
        }
    }
    internal void Apply(WriteableBitmap bitmap)
    {
        FrameworkElement? root = XamlRoot?.Content as FrameworkElement;
        if (root is null)
        {
            return;
        }
        MpvVideoView? video = ResolveVideo(root);
        if (video is null || video.ActualWidth < 1 || video.ActualHeight < 1)
        {
            return;
        }
        Rect surface = Bounds(this, root);
        Rect source = Bounds(video, root);
        if (surface.Width < 1 || surface.Height < 1 || source.Width < 1)
        {
            return;
        }
        // The bitmap is a scaled copy of the whole video frame, so map the
        // surface's rectangle into bitmap pixel coordinates.
        double k = source.Width / bitmap.PixelWidth;
        if (!_diagLogged && Environment.GetEnvironmentVariable("WIINA_BLUR_LOG") == "1")
        {
            _diagLogged = true;
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wiina-blur.log"),
                    $"[layer] surface={surface.Width}x{surface.Height} at {surface.X},{surface.Y} " +
                    $"source={source.Width}x{source.Height} at {source.X},{source.Y} " +
                    $"bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight} k={k:0.###}\n");
            }
            catch
            {
            }
        }
        _brush.ImageSource = bitmap;
        _transform.ScaleX = k;
        _transform.ScaleY = k;
        _transform.TranslateX = source.X - surface.X;
        _transform.TranslateY = source.Y - surface.Y;
        UpdateClip();
        if (!HasFrame && Visibility == Visibility.Visible)
        {
            HasFrame = true;
        }
        RaiseFirstFrameIfReady();
    }
    private MpvVideoView? ResolveVideo(FrameworkElement root)
    {
        if (_video is not null && _video.TryGetTarget(out MpvVideoView? cached))
        {
            return cached;
        }
        MpvVideoView? found = FindVideo(root);
        if (found is not null)
        {
            _video = new WeakReference<MpvVideoView>(found);
        }
        return found;
    }
    private static MpvVideoView? FindVideo(DependencyObject node)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, i);
            if (child is MpvVideoView video)
            {
                return video;
            }
            if (FindVideo(child) is { } nested)
            {
                return nested;
            }
        }
        return null;
    }
    private static Rect Bounds(FrameworkElement element, FrameworkElement root)
    {
        try
        {
            GeneralTransform transform = element.TransformToVisual(root);
            return transform.TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (ArgumentException)
        {
            return Rect.Empty;
        }
    }}