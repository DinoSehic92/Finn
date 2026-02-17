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
            if (value == null)
                return null;

            // If already a Bitmap, return as-is
            if (value is Avalonia.Media.Imaging.Bitmap b)
                return b;

            // If value is a string path to a file, load it
            if (value is string bitmapPath)
            {
                if (File.Exists(bitmapPath))
                    return new Avalonia.Media.Imaging.Bitmap(bitmapPath);
                return null;
            }

            // If value is a byte[] (image bytes), create a Bitmap from stream
            if (value is byte[] bytes && bytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    ms.Position = 0;
                    return new Avalonia.Media.Imaging.Bitmap(ms);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
