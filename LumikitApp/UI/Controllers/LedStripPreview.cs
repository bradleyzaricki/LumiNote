using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LumikitApp.Controls;

public class LedStripPreview : Control
{
    private Color[] _colors = [];

    private readonly SolidColorBrush _backgroundBrush =
        new(Color.Parse("#101010"));

    private readonly Pen _stripPen =
        new(new SolidColorBrush(Color.Parse("#1E1E1E")), 14)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

    public void SetColors(Color[] colors)
    {
        _colors = colors;
        //Render()
        InvalidateVisual();
    }
    
    /// <summary>
    /// Compute
    /// </summary>
    /// <param name="points"></param>
    /// <param name="lengths"></param>
    /// <param name="distance"></param>
    /// <returns></returns>
    private static Point GetPointAlongPath(Point[] points, double[] lengths, double distance)
    {
        for (int i = 0; i < lengths.Length; i++)
        {
            if (distance > lengths[i])
            {
                distance -= lengths[i];
                continue;
            }

            double t = distance / lengths[i];

            return new Point(
                points[i].X + (points[i + 1].X - points[i].X) * t,
                points[i].Y + (points[i + 1].Y - points[i].Y) * t
            );
        }

        return points[^1];
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_colors.Length == 0)
            return;

        double width = Bounds.Width;
        double height = Bounds.Height;

        var path = new[]
        {
            new Point(0, height),   // bottom-left
            new Point(0, 0),        // top-left
            new Point(width, 0),    // top-right
            new Point(width, height) // bottom-right
        };


        double[] lengths = new double[path.Length - 1];
        double totalLength = 0;

        for (int i = 0; i < path.Length - 1; i++)
        {
            var dx = path[i + 1].X - path[i].X;
            var dy = path[i + 1].Y - path[i].Y;

            lengths[i] = Math.Sqrt(dx * dx + dy * dy);
            totalLength += lengths[i];
        }


        var geometry = new StreamGeometry();

        //Draw light strip
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(path[0], false);

            for (int i = 1; i < path.Length; i++)
                gc.LineTo(path[i]);
        }

        context.DrawGeometry(null, _stripPen, geometry);
        
        //Draw LEDs
        for (int i = 0; i < _colors.Length; i++)
        {
            double t = i / (double)(_colors.Length - 1);
            double distance = t * totalLength;

            Point p = GetPointAlongPath(path, lengths, distance);

            var color = _colors[i];
            

            // core
            context.DrawEllipse(
                new SolidColorBrush(color),
                null,
                p,
                4,
                4);
        }
    }}