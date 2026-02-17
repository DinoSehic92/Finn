using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a byte[] containing image/png (or other bitmap) data to an Avalonia Bitmap.
    /// Useful for showing serialized icon bytes (e.g. OtherData.IconBytes) in XAML bindings.
    /// </summary>
    public class ByteArrayToBitmapConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is byte[] bytes && bytes.Length > 0)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    ms.Position = 0;
                    return new Bitmap(ms);
                }
                catch
                {
                    return null!;
                }
            }

            return null!;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
