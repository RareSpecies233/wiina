using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;

namespace Wiina.App.Controls;

/// <summary>
/// Custom OSC timeline: played/buffer fills, chapter and A-B marks, a thumb and
/// a transparent Slider that provides pointer, keyboard and accessibility
/// input. Everything above the Slider is hit-test transparent.
/// </summary>
public sealed partial class TimelineControl : UserControl
{
    private const double ThumbSize = 3;
    private const double SeekStepSeconds = 5;

    private bool _updating;
    private bool _dragging;
    private double _position;
    private double _duration;
    private double _bufferEnd;
    private double _abLoopA;
    private double _abLoopB;
    private IReadOnlyList<double> _chapters = Array.Empty<double>();

    public TimelineControl()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => RedrawMarks();
    }

    public event Action<double>? SeekRequested;
    public event Action? SeekStarted;
    public event Action? SeekCompleted;
    public event Action<double, double>? PreviewRequested;
    public event Action? PreviewHidden;

    public bool IsScrubbing => _dragging;

    public double Position
    {
        get => _position;
        set
        {
            double clamped = Math.Max(0, value);
            if (_dragging)
            {
                // Keep the thumb under the pointer while the player reports
                // intermediate seek positions.
                _position = clamped;
                return;
            }

            _position = clamped;
            SyncSlider();
            UpdateVisuals();
        }
    }

    public void SetDuration(double seconds)
    {
        _duration = Math.Max(0, seconds);
        _updating = true;
        try
        {
            Slider.Maximum = Math.Max(1, _duration);
            Slider.Value = Math.Clamp(_position, 0, Slider.Maximum);
        }
        finally
        {
            _updating = false;
        }

        UpdateVisuals();
    }

    public void SetBufferEnd(double seconds)
    {
        _bufferEnd = Math.Max(0, seconds);
        UpdateVisuals();
    }

    public void SetAbLoop(double a, double b)
    {
        _abLoopA = a;
        _abLoopB = b;
        RedrawMarks();
    }

    public void SetChapters(IReadOnlyList<double> chapters)
    {
        _chapters = chapters ?? Array.Empty<double>();
        RedrawMarks();
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (_duration <= 0)
        {
            base.OnKeyDown(e);
            return;
        }

        double? target = e.Key switch
        {
            VirtualKey.Left => _position - SeekStepSeconds,
            VirtualKey.Right => _position + SeekStepSeconds,
            VirtualKey.Home => 0,
            VirtualKey.End => _duration,
            _ => null,
        };

        if (target is null)
        {
            base.OnKeyDown(e);
            return;
        }

        _position = Math.Clamp(target.Value, 0, _duration);
        SyncSlider();
        UpdateVisuals();
        SeekRequested?.Invoke(_position);
        e.Handled = true;
    }

    private void SyncSlider()
    {
        _updating = true;
        try
        {
            Slider.Value = Math.Clamp(_position, 0, Slider.Maximum);
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateVisuals();
        if (_updating)
        {
            return;
        }

        _position = e.NewValue;
        SeekRequested?.Invoke(e.NewValue);
    }

    private void OnSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        if (_duration <= 0)
        {
            return;
        }

        _dragging = true;
        PreviewHidden?.Invoke();
        SeekStarted?.Invoke();
    }

    private void OnSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
    }

    private void OnSliderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        SeekCompleted?.Invoke();
    }

    private void OnSliderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_duration <= 0 || Slider.ActualWidth <= 0 || _dragging)
        {
            return;
        }

        Point point = e.GetCurrentPoint(Slider).Position;
        double ratio = Math.Clamp(point.X / Slider.ActualWidth, 0, 1);
        PreviewRequested?.Invoke(ratio * _duration, point.X);
    }

    private void OnSliderPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            PreviewHidden?.Invoke();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        double width = Slider.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        double ratio = _duration <= 0 ? 0 : Math.Clamp(_position / _duration, 0, 1);
        double bufferRatio = _duration <= 0 ? 0 : Math.Clamp(_bufferEnd / _duration, 0, 1);

        PlayedBar.Width = width * ratio;
        BufferBar.Width = width * bufferRatio;
        Thumb.Margin = new Thickness(
            Math.Clamp(width * ratio - ThumbSize / 2, 0, Math.Max(0, width - ThumbSize)),
            0,
            0,
            0);
        Thumb.Visibility = _duration > 0 ? Visibility.Visible : Visibility.Collapsed;

        RedrawMarks();
    }

    private void RedrawMarks()
    {
        Marks.Children.Clear();
        double width = Slider.ActualWidth;
        if (width <= 0 || _duration <= 0)
        {
            return;
        }

        Brush chapterBrush = ChapterBrushSource.Background;
        foreach (double chapter in _chapters)
        {
            if (chapter <= 0 || chapter >= _duration)
            {
                continue;
            }

            var mark = new Rectangle
            {
                Width = 1,
                Height = 9,
                Fill = chapterBrush,
                Opacity = 0.65,
            };
            Canvas.SetLeft(mark, width * (chapter / _duration));
            Canvas.SetTop(mark, 6.5);
            Marks.Children.Add(mark);
        }

        AddLoopMarker(_abLoopA, width);
        AddLoopMarker(_abLoopB, width);
    }

    private void AddLoopMarker(double seconds, double width)
    {
        if (seconds <= 0 || seconds >= _duration)
        {
            return;
        }

        var mark = new Rectangle
        {
            Width = 3,
            Height = 13,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Fill = AbBrushSource.Background,
        };
        Canvas.SetLeft(mark, Math.Clamp(width * (seconds / _duration) - 1.5, 0, width - 3));
        Canvas.SetTop(mark, 4.5);
        Marks.Children.Add(mark);
    }
}
