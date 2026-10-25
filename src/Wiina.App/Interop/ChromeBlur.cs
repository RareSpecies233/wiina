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
using Wiina.Core.Playback;
using Wiina.Core.Settings;
using Wiina.Mpv;
namespace Wiina.App.Interop;

/// <summary>
/// Live blurred backdrop for the chrome that floats over the video surface.
///
/// The XAML acrylic/backdrop brushes cannot see a SwapChainPanel (the pixels
/// live in a separate compositor layer), so this service pulls the real frame
/// straight out of libmpv, reduces it, blurs it with a Win2D
/// <see cref="GaussianBlurEffect"/> on a worker thread and paints the blurred
/// crop behind each chrome surface. Every failure path keeps the solid chrome:
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

    /// <summary>
    /// Raised on the UI thread after the mode changed so the chrome surfaces
    /// can attach a layer (leaving Solid) or fall back to the solid brush.
    /// </summary>
    public static event EventHandler? ModeChanged;

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
    private int _geometryDumped;
    private int[]? _extendMap;
    private string? _extendSignature;
    private WriteableBitmap? _bitmap;
    private byte[]? _pending;
    private int _pendingWidth;
    private int _pendingHeight;
    private VideoGeometry _pendingGeometry;
    private int _queued;
    private volatile bool _active = true;
    private volatile bool _fixedCaptured;
    private volatile ChromeBlurMode _mode = ChromeBlurMode.Dynamic;
    private volatile bool _suspended;
    private volatile ChromeBlurMode _pendingMode = ChromeBlurMode.Dynamic;
    private int _captures;

    /// <summary>Current capture strategy.</summary>
    public ChromeBlurMode Mode => _mode;

    /// <summary>Chrome visibility drives captures in Dynamic and Fixed modes.</summary>
    public static void SetActive(bool active)
    {
        ChromeBlur blur = Current;
        if (active)
        {
            blur._fixedCaptured = false;
        }
        blur._active = active;
    }

    /// <summary>
    /// Stops the capture worker and waits briefly for it to exit. Called from
    /// the window close path before the player is disposed so the worker can
    /// never touch a disposed mpv handle (a known hang/crash source).
    /// </summary>
    public static void Shutdown()
    {
        ChromeBlur blur = Current;
        blur._active = false;
        blur._running = false;

        Thread? thread = blur._thread;
        if (thread is { IsAlive: true } && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(1));
        }
        blur._thread = null;

        lock (blur._gate)
        {
            blur._layers.Clear();
        }
    }

    /// <summary>
    /// Switch capture strategy. Solid never captures (the worker does not even
    /// create the Win2D device) and the chrome surfaces show their solid brush;
    /// the other modes capture a live frame.
    /// </summary>
    public static void SetMode(ChromeBlurMode mode)
    {
        ChromeBlur blur = Current;
        if (blur._suspended)
        {
            // A settings/theme refresh must not wake the capture loop while a
            // small window owns the player. Remember the choice; Resume
            // applies it.
            blur._pendingMode = mode;
            return;
        }

        blur.ApplyMode(mode);
    }

    /// <summary>
    /// True while small-window mode keeps the capture worker parked.
    /// <see cref="SetMode"/> calls made meanwhile are remembered, not applied.
    /// </summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// Parks the capture worker for as long as a small window (mini / PiP)
    /// owns the video. Independent of the persisted
    /// <see cref="ChromeBlurMode"/>: the current mode is applied when
    /// <see cref="Resume"/> runs, so a setting change made while parked is not
    /// lost. The chrome surfaces render their opaque solid brush meanwhile.
    /// </summary>
    public static void Suspend()
    {
        ChromeBlur blur = Current;
        if (blur._suspended)
        {
            return;
        }

        blur._suspended = true;
        blur._pendingMode = blur._mode;
        blur.ApplyMode(ChromeBlurMode.Solid);
    }

    /// <summary>
    /// Leaves the parked state and re-applies the mode requested while
    /// suspended (or the one that was active before).
    /// </summary>
    public static void Resume()
    {
        ChromeBlur blur = Current;
        if (!blur._suspended)
        {
            return;
        }

        blur._suspended = false;
        blur.ApplyMode(blur._pendingMode);
    }

    private void ApplyMode(ChromeBlurMode mode)
    {
        _mode = mode;
        _fixedCaptured = false;

        // Solid stops all capture work (the worker parks on the Solid check)
        // but leaves _active alone: if the chrome is visible right now, going
        // back to a blur mode must resume capturing without a show/hide cycle.
        if (mode != ChromeBlurMode.Solid && SnapshotLayers().Count > 0)
        {
            // Layers attached while Solid never armed the worker (Register
            // returns early there), so leaving Solid has to start it here.
            Start();
        }

        ModeChanged?.Invoke(this, EventArgs.Empty);
        BlurLog($"mode -> {mode} (active={_active}, layers={SnapshotLayers().Count}, suspended={_suspended})");
    }

    private List<ChromeBlurLayer> SnapshotLayers()
    {
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
        return alive;
    }
    private ChromeBlur()
    {
    }
    /// <summary>Adds a blur layer over <paramref name="host"/>; UI thread.</summary>
    public static bool TryAttach(
        FrameworkElement host,
        double cornerRadius,
        out ChromeBlurLayer layer)
    {
        // Kill switch for support/AB comparison: keeps the solid fallback.
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
        // Solid deliberately never starts the worker, so no Win2D device or
        // capture exists until a blur mode is selected.
        if (_mode == ChromeBlurMode.Solid)
        {
            return;
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
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
            {
                _running = true;
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
    }
    private void Loop()
    {
        while (_running)
        {
            long started = Environment.TickCount64;
            if (_mode == ChromeBlurMode.Solid)
            {
                Thread.Sleep(FrameIntervalMs);
                continue;
            }

            if (!_active && _mode != ChromeBlurMode.Always)
            {
                Thread.Sleep(FrameIntervalMs);
                continue;
            }

            if (_mode is ChromeBlurMode.Fixed && _fixedCaptured)
            {
                Thread.Sleep(FrameIntervalMs);
                continue;
            }

            try
            {
                MpvPlayer? player = ResolvePlayer();
                if (player is { IsRunning: true })
                {
                    MpvScreenshot? shot = player.ScreenshotRaw();
                    if (shot is not null && shot.Pixels.Length > 0)
                    {
                        Blur(shot, ReadVideoGeometry(player));
                        // Cheap diagnostic for "is the blur still capturing?"
                        // reports: only written when WIINA_BLUR_LOG=1.
                        BlurLog("capture #" + Interlocked.Increment(ref _captures));
                        if (_mode == ChromeBlurMode.Fixed)
                        {
                            _fixedCaptured = true;
                        }
                    }
                    else
                    {
                        BlurLog(shot is null
                            ? "capture skipped: no frame"
                            : "capture skipped: empty frame");
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce("frame capture failed: " + ex.Message);
                BlurLog("capture failed: " + ex.Message);
            }
            int remaining = FrameIntervalMs - (int)(Environment.TickCount64 - started);
            if (remaining > 0)
            {
                Thread.Sleep(remaining);
            }
        }
    }

    /// <summary>
    /// Created on the worker thread only when a blurred frame is actually
    /// needed; Solid mode never touches Win2D.
    /// </summary>
    private CanvasDevice? EnsureDevice()
    {
        if (_device is not null)
        {
            return _device;
        }
        try
        {
            // A dedicated shared device keeps Win2D work off the UI thread.
            _device = CanvasDevice.GetSharedDevice();
        }
        catch (Exception ex)
        {
            LogOnce("Win2D device unavailable: " + ex.Message);
        }
        return _device;
    }

    private MpvPlayer? ResolvePlayer()
    {
        object? window = App.ActiveWindow;
        return window is null ? null : _playerField?.GetValue(window) as MpvPlayer;
    }

    /// <summary>
    /// Reads mpv's real video rectangle: <c>osd-dimensions</c> reports the
    /// rendered frame size plus the letterbox/pillarbox margins, in the pixels
    /// of mpv's window. That window is the composition swapchain sized from the
    /// widget in physical pixels, so these values are physical pixels too and
    /// must be divided by the XAML rasterization scale before use.
    /// Missing or inconsistent properties surface as an invalid geometry and
    /// the layer falls back to an aspect-fit rectangle.
    /// </summary>
    private VideoGeometry ReadVideoGeometry(MpvPlayer player)
    {
        try
        {
            // mpv exposes osd-dimensions as a node: w/h are the whole window
            // (the composition surface) and ml/mt/mr/mb are the letterbox
            // bars inside it, so the painted frame is w-ml-mr by h-mt-mb.
            double width = player.GetPropertyDouble("osd-dimensions/w");
            double height = player.GetPropertyDouble("osd-dimensions/h");
            if (Environment.GetEnvironmentVariable("WIINA_BLUR_LOG") == "1")
            {
                DumpGeometryProperties(player);
            }
            if (width <= 0 || height <= 0)
            {
                return default;
            }
            double left = ReadMargin(player, "osd-dimensions/ml", "osd-dimensions/margin-l");
            double top = ReadMargin(player, "osd-dimensions/mt", "osd-dimensions/margin-t");
            double right = ReadMargin(player, "osd-dimensions/mr", "osd-dimensions/margin-r");
            double bottom = ReadMargin(player, "osd-dimensions/mb", "osd-dimensions/margin-b");
            if (left < 0 || top < 0 || right < 0 || bottom < 0)
            {
                // A negative margin means the video is cropped/panned beyond the
                // window; that rectangle is not expressible here, so fall back
                // to the aspect fit instead of painting outside the widget.
                return default;
            }
            double videoWidth = width - left - right;
            double videoHeight = height - top - bottom;
            if (videoWidth <= 0 || videoHeight <= 0)
            {
                return default;
            }
            return new VideoGeometry(videoWidth, videoHeight, left, top, right, bottom);
        }
        catch (Exception ex)
        {
            LogOnce("video geometry unavailable: " + ex.Message);
            return default;
        }
    }

    private static double ReadMargin(MpvPlayer player, string primary, string fallback)
    {
        double value = player.GetPropertyDouble(primary);
        return value != 0 ? value : player.GetPropertyDouble(fallback);
    }

    /// <summary>
    /// Temporary diagnostic: dumps every property that can describe the video
    /// rectangle so the letterbox mapping can be validated against reality.
    /// </summary>
    private void DumpGeometryProperties(MpvPlayer player)
    {
        if (Interlocked.Exchange(ref _geometryDumped, 1) != 0)
        {
            return;
        }

        static string S(MpvPlayer p, string name)
        {
            string? value = p.GetPropertyString(name);
            return value is null ? "null" : value.Replace("\n", " ");
        }

        BlurLog(
            "props osd=" + S(player, "osd-dimensions") +
            " dwidth=" + S(player, "dwidth") + " dheight=" + S(player, "dheight") +
            " out-params=" + S(player, "video-out-params") +
            " params=" + S(player, "video-params") +
            " panscan=" + S(player, "panscan") + " zoom=" + S(player, "video-zoom") +
            " align=" + S(player, "video-align-x") + "," + S(player, "video-align-y") +
            " scale=" + S(player, "current-window-scale") +
            " hidpi=" + S(player, "display-hidpi-scale"));
    }

    /// <summary>
    /// mpv's rendered video rectangle in the physical pixels of the composition
    /// swapchain; <see cref="IsValid"/> is false when mpv could not report it.
    /// </summary>
    internal readonly record struct VideoGeometry(
        double Width,
        double Height,
        double MarginLeft,
        double MarginTop,
        double MarginRight,
        double MarginBottom,
        bool Extended = false)
    {
        public bool IsValid => Width > 0 && Height > 0;
    }

    private void Blur(MpvScreenshot shot, VideoGeometry geometry)
    {
        int width = Math.Min(TargetPixels, shot.Width);
        int height = Math.Max(
            1,
            (int)Math.Round((double)shot.Height * width / shot.Width));
        byte[] small = Downscale(shot, width, height);

        // With mpv's real video rectangle the frame is edge-extended to the whole
        // widget (letterbox/pillarbox included) so the chrome never shows an
        // unpainted seam at the video boundary. Without it the frame is published
        // as-is and the layer still maps it onto the fitted video rectangle.
        bool extended = geometry.IsValid;
        if (extended)
        {
            double windowWidth = geometry.Width + geometry.MarginLeft + geometry.MarginRight;
            double windowHeight = geometry.Height + geometry.MarginTop + geometry.MarginBottom;
            (int destWidth, int destHeight) = VideoDisplayRect.ExtendedSize(width, windowWidth, windowHeight);
            var display = new DisplayRect(
                geometry.MarginLeft,
                geometry.MarginTop,
                geometry.Width,
                geometry.Height);
            small = ExtendEdges(
                small,
                width,
                height,
                destWidth,
                destHeight,
                display,
                windowWidth,
                windowHeight);
            width = destWidth;
            height = destHeight;
        }

        CanvasDevice? device = EnsureDevice();
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
        Publish(target.GetPixelBytes(), width, height, geometry with { Extended = extended });
    }

    /// <summary>
    /// Copies the frame into a bitmap that covers the whole widget, replicating
    /// the border pixels outwards across the letterbox/pillarbox bars. The centre
    /// keeps the exact proportional mapping of <paramref name="display"/>, and the
    /// replicated border starts on the same edge pixels, so the boundary is
    /// continuous instead of a straight seam.
    /// </summary>
    private byte[] ExtendEdges(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int destWidth,
        int destHeight,
        DisplayRect display,
        double widgetWidth,
        double widgetHeight)
    {
        int[] map = GetExtendMap(
            sourceWidth,
            sourceHeight,
            destWidth,
            destHeight,
            display,
            widgetWidth,
            widgetHeight);
        var dest = new byte[destWidth * destHeight * 4];
        for (int i = 0; i < map.Length; i++)
        {
            int src = map[i];
            int dst = i * 4;
            dest[dst] = source[src];
            dest[dst + 1] = source[src + 1];
            dest[dst + 2] = source[src + 2];
            dest[dst + 3] = 255;
        }
        return dest;
    }

    /// <summary>
    /// Source byte offset for every pixel of the extended bitmap. Cached because
    /// the geometry only changes when the window, the video or the surface changes,
    /// not on every frame.
    /// </summary>
    private int[] GetExtendMap(
        int sourceWidth,
        int sourceHeight,
        int destWidth,
        int destHeight,
        DisplayRect display,
        double widgetWidth,
        double widgetHeight)
    {
        string signature =
            $"{sourceWidth}x{sourceHeight}>{destWidth}x{destHeight}" +
            $"|{display.X:0.##},{display.Y:0.##},{display.Width:0.##},{display.Height:0.##}" +
            $"|{widgetWidth:0.##}x{widgetHeight:0.##}";
        if (_extendMap is not null && _extendSignature == signature)
        {
            return _extendMap;
        }

        var map = new int[destWidth * destHeight];
        for (int y = 0; y < destHeight; y++)
        {
            for (int x = 0; x < destWidth; x++)
            {
                map[(y * destWidth) + x] = VideoDisplayRect.TryMapExtended(
                    x,
                    y,
                    destWidth,
                    destHeight,
                    display,
                    widgetWidth,
                    widgetHeight,
                    sourceWidth,
                    sourceHeight,
                    out int sourceX,
                    out int sourceY)
                    ? ((sourceY * sourceWidth) + sourceX) * 4
                    : 0;
            }
        }
        _extendMap = map;
        _extendSignature = signature;
        return map;
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
    private void Publish(byte[] pixels, int width, int height, VideoGeometry geometry)
    {
        lock (_gate)
        {
            _pending = pixels;
            _pendingWidth = width;
            _pendingHeight = height;
            _pendingGeometry = geometry;
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
        VideoGeometry geometry;
        lock (_gate)
        {
            pixels = _pending;
            width = _pendingWidth;
            height = _pendingHeight;
            geometry = _pendingGeometry;
            _pending = null;
        }
        if (pixels is null || _mode == ChromeBlurMode.Solid)
        {
            // Solid shows the opaque brush; drop any in-flight frame.
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
            foreach (ChromeBlurLayer layer in SnapshotLayers())
            {
                layer.Apply(_bitmap, geometry);
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

    /// <summary>Diagnostic log, only written when WIINA_BLUR_LOG=1.</summary>
    private static void BlurLog(string message)
    {
        if (Environment.GetEnvironmentVariable("WIINA_BLUR_LOG") != "1")
        {
            return;
        }
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wiina-blur.log"),
                message + "\n");
        }
        catch
        {
        }
    }

    /// <summary>
    /// The current theme's opaque chrome brush is never handed to the chrome
    /// surfaces by this service: every surface keeps its XAML
    /// <c>{ThemeResource ChromeSolidBrush}</c> binding and paints the blurred
    /// frame in a child layer above it. That keeps Solid opaque and lets the
    /// brush follow theme, accent and HighContrast changes on its own.
    /// </summary>
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
    private WeakReference<MpvVideoView>? _video;
    private string? _diagSignature;
    internal ChromeBlurLayer(double cornerRadius)
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
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
    /// fill size; the clip is refreshed whenever the surface gets its real size.
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

    /// <summary>True once a blurred frame has actually been painted.</summary>
    public bool HasFrame { get; private set; }
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

    internal void Apply(WriteableBitmap bitmap, ChromeBlur.VideoGeometry geometry)
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

        // The widget embraces the letterbox/pillarbox bars. When mpv reported the
        // frame's real rectangle the worker already edge-extended the bitmap over
        // the whole widget, so it maps uniformly; otherwise map the frame onto the
        // fitted video rectangle only (bars keep the tint underneath).
        double scale = XamlRoot?.RasterizationScale ?? 1.0;
        DisplayRect display = default;
        bool fromMpv = false;
        bool extended = geometry.Extended;
        if (extended)
        {
            display = new DisplayRect(0, 0, source.Width, source.Height);
            fromMpv = true;
        }
        else if (geometry.IsValid)
        {
            fromMpv = VideoDisplayRect.TryFromOsd(
                source.Width,
                source.Height,
                scale,
                geometry.Width,
                geometry.Height,
                geometry.MarginLeft,
                geometry.MarginTop,
                geometry.MarginRight,
                geometry.MarginBottom,
                out display);
        }
        if (!fromMpv)
        {
            display = VideoDisplayRect.Fit(
                source.Width,
                source.Height,
                (double)bitmap.PixelWidth / bitmap.PixelHeight);
        }
        display = VideoDisplayRect.Clamp(display, source.Width, source.Height);
        if (display.IsEmpty)
        {
            return;
        }

        double videoX = source.X + display.X;
        double videoY = source.Y + display.Y;
        double scaleX = display.Width / bitmap.PixelWidth;
        double scaleY = display.Height / bitmap.PixelHeight;
        // Log the geometry whenever it changes (not per frame): this is the
        // switch support needs to confirm the video rect on a user's machine.
        string signature = $"{surface.Width:0.#}x{surface.Height:0.#}@{surface.X:0.#},{surface.Y:0.#}|" +
            $"{source.Width:0.#}x{source.Height:0.#}|{display.X:0.#},{display.Y:0.#},{display.Width:0.#},{display.Height:0.#}|" +
            $"{fromMpv}|{extended}|{scale:0.##}|{geometry.Width:0.#}x{geometry.Height:0.#}";
        if (Environment.GetEnvironmentVariable("WIINA_BLUR_LOG") == "1" && signature != _diagSignature)
        {
            _diagSignature = signature;
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wiina-blur.log"),
                    $"[layer] surface={surface.Width:0.#}x{surface.Height:0.#} at {surface.X:0.#},{surface.Y:0.#} " +
                    $"source={source.Width:0.#}x{source.Height:0.#} at {source.X:0.#},{source.Y:0.#} " +
                    $"dpi={scale:0.##} osd={(geometry.IsValid ? $"{geometry.Width:0.#}x{geometry.Height:0.#} " +
                        $"m={geometry.MarginLeft:0.#},{geometry.MarginTop:0.#},{geometry.MarginRight:0.#},{geometry.MarginBottom:0.#}" : "n/a")} " +
                    $"display={display.Width:0.#}x{display.Height:0.#} at {display.X:0.#},{display.Y:0.#} " +
                    $"fromMpv={fromMpv} extended={extended} bitmap={bitmap.PixelWidth}x{bitmap.PixelHeight} " +
                    $"k={scaleX:0.###},{scaleY:0.###}\n");
            }
            catch
            {
            }
        }
        _fill.Fill = _brush;
        _brush.ImageSource = bitmap;
        _transform.ScaleX = scaleX;
        _transform.ScaleY = scaleY;
        _transform.TranslateX = videoX - surface.X;
        _transform.TranslateY = videoY - surface.Y;
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
    }
}
