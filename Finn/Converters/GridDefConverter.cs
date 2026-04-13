using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a bool (or multiple bools via MultiBinding) to a star-sized GridLength
    /// when all values are true, or 0px when any is false.
    /// Pass a numeric ConverterParameter to control the star proportion (default 1).
    /// </summary>
    public class GridDefConverter : IValueConverter, IMultiValueConverter
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

        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            bool allTrue = values.All(v => v is bool b && b);
            if (allTrue)
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
