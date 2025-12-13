using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class ShortenStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string input = (string)value;

            if (input != null)
            {
                return input[0].ToString();
            }
            return "N";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return "none";
        }
    }
}
