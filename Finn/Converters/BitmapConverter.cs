using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;

namespace Finn.Converters
{
    public class BitmapConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string bitmapPath = (string)value;

            if (File.Exists(bitmapPath))
            {
                Avalonia.Media.Imaging.Bitmap bitmap = new Avalonia.Media.Imaging.Bitmap(bitmapPath);
                return bitmap;
            }
            else
            {
                return null;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
