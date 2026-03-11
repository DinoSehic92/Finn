using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a boolean (IsAppendedFile) to either a <see cref="FontStyle"/> or
    /// an opacity value so appended files appear italic and slightly faded.
    /// Use the two static instances in AXAML via x:Static.
    /// </summary>
    public class AppendedFileStyleConverter : IValueConverter
    {
        public static readonly AppendedFileStyleConverter FontStyle = new(Mode.FontStyle);
        public static readonly AppendedFileStyleConverter Opacity = new(Mode.Opacity);

        private enum Mode { FontStyle, Opacity }
        private readonly Mode _mode;

        private AppendedFileStyleConverter(Mode mode) => _mode = mode;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isAppended = value is true;
            return _mode switch
            {
                Mode.FontStyle => isAppended ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                Mode.Opacity => isAppended ? 0.55 : 1.0,
                _ => value
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
