using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a uniform CornerRadius to a partial one for segmented controls.
    /// ConverterParameter: "left" | "right" | "top-left" | "top-right" | "bottom-left" | "bottom-right"
    /// </summary>
    public class SegmentCornerRadiusConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not CornerRadius cr || parameter is not string side)
                return new CornerRadius(0);

            double r = cr.TopLeft;
            return side switch
            {
                "left"         => new CornerRadius(r, 0, 0, r),
                "right"        => new CornerRadius(0, r, r, 0),
                "top-left"     => new CornerRadius(r, 0, 0, 0),
                "top-right"    => new CornerRadius(0, r, 0, 0),
                "bottom-left"  => new CornerRadius(0, 0, 0, r),
                "bottom-right" => new CornerRadius(0, 0, r, 0),
                _              => cr
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
