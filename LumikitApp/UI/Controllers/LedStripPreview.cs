using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LumikitApp.Controls;

public class LedStripPreview : Control
{
    private Color[] _colors = [];

    public void SetColors(Color[] colors)
    {
        _colors = colors;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_colors.Length == 0)
            return;

        int columns = 100;

        double ledSize = Bounds.Width / columns;

        for (int i = 0; i < _colors.Length; i++)
        {
            int row = i / columns;
            int col = i % columns;

            var rect = new Rect(
                col * ledSize,
                row * ledSize,
                ledSize - 2,
                ledSize - 2);

            context.FillRectangle(
                new SolidColorBrush(_colors[i]),
                rect);
        }
    }
}