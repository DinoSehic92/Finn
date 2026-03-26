using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a bool to a star-sized GridLength when true, or 0px when false.
    /// Pass a numeric ConverterParameter to control the star proportion (default 1).
    /// </summary>
    public class GridDefConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool visible && visible)
            {
                double stars = 1;
                if (parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out double parsed))
                    stars = parsed;
                return new GridLength(stars, GridUnitType.Star);
            }
            return new GridLength(0, GridUnitType.Pixel);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
