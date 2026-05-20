using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace LumikitApp.Converters;

public class DarkenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ISolidColorBrush c)
        {
            return new SolidColorBrush(Color.FromRgb(
                (byte)(c.Color.R * 0.2),
                (byte)(c.Color.G * 0.2),
                (byte)(c.Color.B * 0.2)
            ));
        }

        return Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}