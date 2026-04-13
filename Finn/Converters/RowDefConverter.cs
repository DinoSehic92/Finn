using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a bool (or multiple bools via MultiBinding) to a fixed 40px row
    /// when all values are true, or 0px when any is false.
    /// </summary>
    public class RowDefConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool visible && visible)
                return new GridLength(40, GridUnitType.Pixel);
            return new GridLength(0, GridUnitType.Pixel);
        }

        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            bool allTrue = values.All(v => v is bool b && b);
            if (allTrue)
                return new GridLength(40, GridUnitType.Pixel);
            return new GridLength(0, GridUnitType.Pixel);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
