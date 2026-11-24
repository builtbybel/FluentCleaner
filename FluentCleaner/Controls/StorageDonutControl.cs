using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace FluentCleaner.Controls;

/// <summary>
/// Pure native WinUI 3 multi-segment vector donut chart for storage breakdown.
/// Zero third-party dependencies; DPI-independent; Light & Dark theme aware.
/// </summary>
public sealed class StorageDonutControl : Grid
{
    private readonly Canvas _canvas = new();
    private const double RingSize = 96;
    private const double StrokeWidth = 9;

    public static readonly DependencyProperty WindowsBytesProperty =
        DependencyProperty.Register(nameof(WindowsBytes), typeof(long), typeof(StorageDonutControl),
            new PropertyMetadata(0L, OnSegmentsChanged));

    public static readonly DependencyProperty BrowsersBytesProperty =
        DependencyProperty.Register(nameof(BrowsersBytes), typeof(long), typeof(StorageDonutControl),
            new PropertyMetadata(0L, OnSegmentsChanged));

    public static readonly DependencyProperty DevAiBytesProperty =
        DependencyProperty.Register(nameof(DevAiBytes), typeof(long), typeof(StorageDonutControl),
            new PropertyMetadata(0L, OnSegmentsChanged));

    public static readonly DependencyProperty AppsBytesProperty =
        DependencyProperty.Register(nameof(AppsBytes), typeof(long), typeof(StorageDonutControl),
            new PropertyMetadata(0L, OnSegmentsChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(StorageDonutControl),
            new PropertyMetadata(0.0, OnSegmentsChanged));

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(StorageDonutControl),
            new PropertyMetadata(false, OnSegmentsChanged));

    public long   WindowsBytes  { get => (long)GetValue(WindowsBytesProperty);  set => SetValue(WindowsBytesProperty, value); }
    public long   BrowsersBytes { get => (long)GetValue(BrowsersBytesProperty); set => SetValue(BrowsersBytesProperty, value); }
    public long   DevAiBytes    { get => (long)GetValue(DevAiBytesProperty);    set => SetValue(DevAiBytesProperty, value); }
    public long   AppsBytes     { get => (long)GetValue(AppsBytesProperty);     set => SetValue(AppsBytesProperty, value); }
    public double Progress      { get => (double)GetValue(ProgressProperty);    set => SetValue(ProgressProperty, value); }
    public bool   IsBusy        { get => (bool)GetValue(IsBusyProperty);        set => SetValue(IsBusyProperty, value); }

    public StorageDonutControl()
    {
        Width  = RingSize;
        Height = RingSize;
        _canvas.Width  = RingSize;
        _canvas.Height = RingSize;
        Children.Add(_canvas);
        Loaded += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
    }

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StorageDonutControl ctrl)
            ctrl.Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();

        double center = RingSize / 2.0;
        double radius = (RingSize - StrokeWidth) / 2.0;

        // Subtle track ring
        var trackBrush = Application.Current.Resources.TryGetValue("ControlStrokeColorDefaultBrush", out var res) && res is Brush b
            ? b
            : new SolidColorBrush(Color.FromArgb(45, 128, 128, 128));

        var track = new Ellipse
        {
            Width           = radius * 2,
            Height          = radius * 2,
            Stroke          = trackBrush,
            StrokeThickness = StrokeWidth
        };
        Canvas.SetLeft(track, StrokeWidth / 2.0);
        Canvas.SetTop(track, StrokeWidth / 2.0);
        _canvas.Children.Add(track);

        if (IsBusy)
        {
            double pct = Math.Clamp(Progress, 0, 100);
            if (pct > 0.5)
                AddArc(center, radius, 0, pct * 3.599, Color.FromArgb(255, 0, 120, 212));
            return;
        }

        long total = WindowsBytes + BrowsersBytes + DevAiBytes + AppsBytes;
        if (total <= 0) return;

        var slices = new (long Bytes, Color Color)[]
        {
            (WindowsBytes,  Color.FromArgb(255, 0,   120, 212)), // Fluent Blue
            (BrowsersBytes, Color.FromArgb(255, 56,  189, 248)), // Cyan
            (DevAiBytes,    Color.FromArgb(255, 16,  185, 129)), // Emerald
            (AppsBytes,     Color.FromArgb(255, 139, 92,  246)), // Violet
        };

        int nonZero = slices.Count(s => s.Bytes > 0);
        double gapDeg = nonZero > 1 ? 4.0 : 0.0;
        double currentAngle = 0.0;

        foreach (var (bytes, color) in slices)
        {
            if (bytes <= 0) continue;
            double sweep = (bytes / (double)total) * 360.0;
            double drawSweep = Math.Max(3.0, sweep - gapDeg);
            if (nonZero == 1) drawSweep = 359.5;

            AddArc(center, radius, currentAngle + gapDeg / 2.0, drawSweep, color);
            currentAngle += sweep;
        }
    }

    private void AddArc(double center, double radius, double startDeg, double sweepDeg, Color color)
    {
        double clampedSweep = Math.Min(sweepDeg, 359.5);
        double startRad = (startDeg - 90.0) * Math.PI / 180.0;
        double endRad   = (startDeg + clampedSweep - 90.0) * Math.PI / 180.0;

        var startPt = new Point(center + radius * Math.Cos(startRad), center + radius * Math.Sin(startRad));
        var endPt   = new Point(center + radius * Math.Cos(endRad),   center + radius * Math.Sin(endRad));

        var figure = new PathFigure { StartPoint = startPt, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point          = endPt,
            Size           = new Size(radius, radius),
            IsLargeArc     = clampedSweep > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geom = new PathGeometry();
        geom.Figures.Add(figure);

        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data            = geom,
            Stroke          = new SolidColorBrush(color),
            StrokeThickness = StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        };
        _canvas.Children.Add(path);
    }
}
