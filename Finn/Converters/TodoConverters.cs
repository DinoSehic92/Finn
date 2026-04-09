using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Finn.Converters
{
    /// <summary>
    /// Returns <see cref="TextDecorations.Strikethrough"/> when the value is true, null otherwise.
    /// Used for completed to-do items.
    /// </summary>
    public class TodoStrikethroughConverter : IValueConverter
    {
        public static readonly TodoStrikethroughConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? TextDecorations.Strikethrough : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns 0.45 when the value is true, 1.0 otherwise.
    /// Used to dim completed to-do items.
    /// </summary>
    public class TodoOpacityConverter : IValueConverter
    {
        public static readonly TodoOpacityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? 0.45 : 1.0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a color hex string (e.g. "#D64045") to a <see cref="SolidColorBrush"/>.
    /// Returns <see cref="Brushes.Transparent"/> for null or empty strings.
    /// </summary>
    public class TodoColorConverter : IValueConverter
    {
        public static readonly TodoColorConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                try { return new SolidColorBrush(Color.Parse(s)); }
                catch { }
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts an IndentLevel (int) to a left-margin Thickness (24px per level).
    /// </summary>
    public class TodoIndentConverter : IValueConverter
    {
        public static readonly TodoIndentConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is int level && level > 0 ? new Thickness(24 * level, 0, 0, 0) : new Thickness(0);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
