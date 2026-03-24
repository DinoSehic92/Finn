using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Finn.Converters
{
    public class BitmapConverter : IValueConverter
    {
        // Cache bitmaps loaded from file paths so repeated tooltip hovers
        // don't re-read from disk and re-decode the image every time.
        private static readonly ConcurrentDictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return null;

            // If already a Bitmap, return as-is
            if (value is Bitmap b)
                return b;

            // If value is a string path to a file, load it (cached)
            if (value is string bitmapPath)
            {
                if (string.IsNullOrEmpty(bitmapPath))
                    return null;

                if (_cache.TryGetValue(bitmapPath, out var cached))
                    return cached;

                try
                {
                    if (!File.Exists(bitmapPath))
                        return null;

                    var bitmap = new Bitmap(bitmapPath);
                    _cache.TryAdd(bitmapPath, bitmap);
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }

            // If value is a byte[] (image bytes), create a Bitmap from stream
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
                    return null;
                }
            }

            return null;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }

        /// <summary>
        /// Evicts a single path from the cache (e.g. after thumbnail regeneration).
        /// </summary>
        public static void Evict(string path)
        {
            if (_cache.TryRemove(path, out var old))
                old.Dispose();
        }

        /// <summary>
        /// Clears the entire bitmap cache (e.g. when clearing all thumbnails).
        /// </summary>
        public static void ClearCache()
        {
            foreach (var kvp in _cache)
                kvp.Value.Dispose();
            _cache.Clear();
        }
    }
}
