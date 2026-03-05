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
            ["Yellow"]  = Color.Parse("#F5C542"),
            ["Orange"]  = Color.Parse("#E8875A"),
            ["Brown"]   = Color.Parse("#A68A6B"),
            ["Green"]   = Color.Parse("#5BAD7A"),
            ["Blue"]    = Color.Parse("#5B9BD5"),
            ["Red"]     = Color.Parse("#D9665F"),
            ["Magenta"] = Color.Parse("#9F7FBA"),
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
