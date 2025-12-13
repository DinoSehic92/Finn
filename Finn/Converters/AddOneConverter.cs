using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class AddOneConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (int)value + 1;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                return Int32.Parse(value.ToString()) - 1;
            }
            catch
            {
                return 0;
            }
        }
    }
}
