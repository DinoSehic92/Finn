using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a color tag name (e.g. "Red", "Blue") to a <see cref="SolidColorBrush"/>.
    /// Returns a transparent brush when the tag is empty or unrecognised.
    /// </summary>
    public class ColorNameToBrushConverter : IValueConverter
    {
        public static readonly ColorNameToBrushConverter Instance = new();

        private static readonly Dictionary<string, Color> ColorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Yellow"]  = Color.Parse("#E5A820"),
            ["Orange"]  = Color.Parse("#E06830"),
            ["Brown"]   = Color.Parse("#9C6B3C"),
            ["Green"]   = Color.Parse("#3DA35F"),
            ["Blue"]    = Color.Parse("#3B82D9"),
            ["Red"]     = Color.Parse("#D64045"),
            ["Magenta"] = Color.Parse("#9B5FC0"),
        };

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string name && ColorMap.TryGetValue(name, out var color))
                return new SolidColorBrush(color);

            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
