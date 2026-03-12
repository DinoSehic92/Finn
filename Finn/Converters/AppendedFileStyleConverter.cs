using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Finn.Converters
{
    /// <summary>
    /// Converts a boolean (IsAppendedFile / IsExpanded / HasChildren) to visual
    /// properties so appended files appear italic, slightly faded, and indented,
    /// and parent files show an expand/collapse chevron.
    /// Use the static instances in AXAML via x:Static.
    /// </summary>
    public class AppendedFileStyleConverter : IValueConverter
    {
        public static readonly AppendedFileStyleConverter FontStyle = new(Mode.FontStyle);
        public static readonly AppendedFileStyleConverter Opacity = new(Mode.Opacity);
        public static readonly AppendedFileStyleConverter Indent = new(Mode.Indent);
        public static readonly AppendedFileStyleConverter Chevron = new(Mode.Chevron);

        private enum Mode { FontStyle, Opacity, Indent, Chevron }
        private readonly Mode _mode;

        private AppendedFileStyleConverter(Mode mode) => _mode = mode;

        private static readonly RotateTransform ChevronExpanded = new(90);
        private static readonly RotateTransform ChevronCollapsed = new(0);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool flag = value is true;
            return _mode switch
            {
                Mode.FontStyle => flag ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                Mode.Opacity => flag ? 0.8 : 1.0,
                Mode.Indent => flag ? 20.0 : 0.0,
                Mode.Chevron => flag ? ChevronExpanded : ChevronCollapsed,
                _ => value
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
