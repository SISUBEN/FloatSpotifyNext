using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Control = System.Windows.Controls.Control;
using System.Windows.Media;
using FloatSpotify.Playback;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace FloatSpotify.Windows;

/// <summary>Native WPF text layout; highlight clips never change glyph positions.</summary>
public sealed class KaraokeText : Control
{
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(PlaybackFrame), typeof(KaraokeText),
        new FrameworkPropertyMetadata(null, OnFrameChanged));
    public PlaybackFrame? Frame { get => (PlaybackFrame?)GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    private FormattedText? _layout;
    private readonly List<(TimedWord Word, Geometry Geometry, Rect[] Rows)> _words = new();
    private long _sampledAt;
    private bool _subscribed;
    private double _layoutWidth;

    public KaraokeText()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => StopRendering();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }

    private static void OnFrameChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (KaraokeText)sender;
        var old = (PlaybackFrame?)args.OldValue;
        var frame = (PlaybackFrame?)args.NewValue;
        control._sampledAt = Stopwatch.GetTimestamp();
        if (old?.CurrentLine != frame?.CurrentLine || !ReferenceEquals(old?.ActiveLyric, frame?.ActiveLyric))
        {
            control._layout = null;
            control.InvalidateMeasure();
        }
        control.InvalidateVisual();
        control.UpdateSubscription();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty || e.Property == FontWeightProperty ||
            e.Property == FontStyleProperty || e.Property == FontStretchProperty || e.Property == ForegroundProperty ||
            e.Property == FlowDirectionProperty)
        {
            _layout = null;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _layout = null;
        InvalidateMeasure();
    }

    private void EnsureLayout(double width)
    {
        width = Math.Max(1, double.IsFinite(width) ? width : 1600);
        if (_layout is not null && Math.Abs(width - _layoutWidth) < 0.01) return;
        _layoutWidth = width;
        var text = Frame?.CurrentLine ?? string.Empty;
        _layout = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize,
            Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width, TextAlignment = TextAlignment.Center
        };
        _words.Clear();
        var timed = Frame?.ActiveLyric?.Words;
        if (timed is null || string.Concat(timed.Select(w => w.Text)) != text) return;
        var offset = 0;
        foreach (var word in timed)
        {
            if (word.Text.Length > 0)
            {
                var geometry = _layout.BuildHighlightGeometry(new Point(), offset, word.Text.Length);
                if (geometry is not null)
                {
                    var rows = geometry.GetFlattenedPathGeometry().Figures
                        .Select(f => new PathGeometry(new[] { f }).Bounds).OrderBy(r => r.Top).ToArray();
                    _words.Add((word, geometry, rows));
                }
            }
            offset += word.Text.Length;
        }
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        EnsureLayout(availableSize.Width);
        return new System.Windows.Size(_layoutWidth, _layout!.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureLayout(ActualWidth);
        if (_layout is null) return;
        var frame = Frame;
        if (_words.Count == 0 || frame is null)
        {
            dc.DrawText(_layout, new Point()); // Honest line-sync fallback: no invented word sweep.
            return;
        }
        var elapsed = frame.IsPlaying ? Math.Min(250, Stopwatch.GetElapsedTime(_sampledAt).TotalMilliseconds) : 0;
        var position = frame.Position + TimeSpan.FromMilliseconds(elapsed);
        dc.PushOpacity(0.32);
        dc.DrawText(_layout, new Point());
        dc.Pop();
        foreach (var (word, geometry, rows) in _words)
        {
            var progress = word.Progress(position);
            if (progress <= 0) continue;
            dc.PushClip(geometry);
            // Per-row rectangles keep wrapped words continuous and respect centered layout.
            var remaining = rows.Sum(r => r.Width) * progress;
            foreach (var row in rows)
            {
                var width = Math.Min(row.Width, remaining);
                remaining -= width;
                if (width <= 0) break;
                var left = FlowDirection == System.Windows.FlowDirection.RightToLeft ? row.Right - width : row.Left;
                dc.PushClip(new RectangleGeometry(new Rect(left, row.Top, width, row.Height)));
                dc.DrawText(_layout, new Point());
                dc.Pop();
            }
            dc.Pop();
        }
    }

    private void UpdateSubscription()
    {
        var animate = IsLoaded && IsVisible && Frame is { IsPlaying: true, ActiveLyric.Words.Count: > 0 };
        if (animate && !_subscribed)
        {
            CompositionTarget.Rendering += RenderFrame;
            _subscribed = true;
        }
        else if (!animate) StopRendering();
    }

    private void RenderFrame(object? sender, EventArgs e)
    {
        // Freeze on stale playback updates rather than running away during a disconnect.
        if (Stopwatch.GetElapsedTime(_sampledAt).TotalMilliseconds <= 300) InvalidateVisual();
    }

    private void StopRendering()
    {
        if (!_subscribed) return;
        CompositionTarget.Rendering -= RenderFrame;
        _subscribed = false;
    }
}
