using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace FluentCleaner.Services;

/// <summary>
/// High-performance vector donut chart renderer that paints directly onto a native WinUI 3 Canvas.
/// Zero custom UI subclassing; eliminates WinRT ABI IFrameworkElementOverrides MeasureOverride exceptions.
/// </summary>
public static class StorageDonutRenderer
{
    public const double RingSize = 124.0;
    public const double StrokeWidth = 10.0;

    public static void Render(
        Canvas canvas,
        long windowsBytes,
        long browsersBytes,
        long devAiBytes,
        long appsBytes,
        double progress,
        bool isBusy)
    {
        if (canvas == null) return;

        canvas.Children.Clear();
        canvas.Width = RingSize;
        canvas.Height = RingSize;

        double center = RingSize / 2.0;
        double radius = (RingSize - StrokeWidth) / 2.0;

        // Subtle background track ring
        var trackBrush = Application.Current.Resources.TryGetValue("ControlStrokeColorDefaultBrush", out var res) && res is Brush b
            ? b
            : new SolidColorBrush(Color.FromArgb(45, 128, 128, 128));

        var track = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = trackBrush,
            StrokeThickness = StrokeWidth
        };
        Canvas.SetLeft(track, StrokeWidth / 2.0);
        Canvas.SetTop(track, StrokeWidth / 2.0);
        canvas.Children.Add(track);

        if (isBusy)
        {
            double pct = Math.Clamp(progress, 0, 100);
            if (pct > 0.5)
            {
                AddArc(canvas, center, radius, 0, pct * 3.599, Color.FromArgb(255, 2, 132, 199));
            }
            return;
        }

        long total = windowsBytes + browsersBytes + devAiBytes + appsBytes;

        // If not scanned yet, render the vibrant 4-category color preview ring matching design
        if (total <= 0)
        {
            var previewSlices = new (double Share, Color Color)[]
            {
                (0.25, Color.FromArgb(255, 0,   120, 212)), // Windows Azure (#0078D4)
                (0.35, Color.FromArgb(255, 56,  189, 248)), // Browsers Sky Blue (#38BDF8)
                (0.20, Color.FromArgb(255, 16,  185, 129)), // Dev & AI Emerald (#10B981)
                (0.20, Color.FromArgb(255, 139, 92,  246)), // Apps & Tools Purple (#8B5CF6)
            };

            double gapDegPreview = 5.0;
            double currentAnglePreview = 0.0;
            foreach (var (share, color) in previewSlices)
            {
                double sweep = share * 360.0;
                double drawSweep = Math.Max(3.0, sweep - gapDegPreview);
                AddArc(canvas, center, radius, currentAnglePreview + gapDegPreview / 2.0, drawSweep, color);
                currentAnglePreview += sweep;
            }
            return;
        }

        var slices = new (long Bytes, Color Color)[]
        {
            (windowsBytes,  Color.FromArgb(255, 0,   120, 212)), // Fluent Blue
            (browsersBytes, Color.FromArgb(255, 56,  189, 248)), // Cyan
            (devAiBytes,    Color.FromArgb(255, 16,  185, 129)), // Emerald
            (appsBytes,     Color.FromArgb(255, 139, 92,  246)), // Violet
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

            AddArc(canvas, center, radius, currentAngle + gapDeg / 2.0, drawSweep, color);
            currentAngle += sweep;
        }
    }

    private static void AddArc(Canvas canvas, double center, double radius, double startDeg, double sweepDeg, Color color)
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
            Data               = geom,
            Stroke             = new SolidColorBrush(color),
            StrokeThickness    = StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        };
        canvas.Children.Add(path);
    }
}
