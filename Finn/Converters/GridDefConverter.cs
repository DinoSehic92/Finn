using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class GridDefConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if ((bool)value)
            {
                return new GridLength(1, GridUnitType.Star);
            }
            else
            {
                return new GridLength(0, GridUnitType.Pixel);
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
