using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class RowDefConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if ((bool)value)
            {
                return new GridLength(35, GridUnitType.Pixel);
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
