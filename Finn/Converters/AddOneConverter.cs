using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class AddOneConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is int i ? i + 1 : 0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int i) return i - 1;
            if (int.TryParse(value?.ToString(), out int parsed)) return parsed - 1;
            return 0;
        }
    }
}
