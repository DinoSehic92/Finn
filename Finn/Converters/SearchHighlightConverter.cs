using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Finn.Converters
{
    /// <summary>
    /// Multi-value converter that splits a file name into Avalonia <see cref="Run"/> inlines,
    /// highlighting the substring that matches the active search term with the accent colour.
    ///
    /// Bindings (in order):
    ///   [0] string – the full text to display  (FileData.Namn)
    ///   [1] string – the search term           (FileData.SearchTerm)
    ///   [2] bool   – whether this row is a direct match (FileData.IsSearchMatch)
    ///
    /// When there is no search term, or the row is not a direct match,
    /// a single unstyled Run with the full text is returned.
    /// </summary>
    public class SearchHighlightConverter : IMultiValueConverter
    {
        public static readonly SearchHighlightConverter Instance = new();

        // Accent brush — matches the SystemAccentColor resource used elsewhere in the app.
        private static readonly IBrush AccentBrush =
            new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));   // Windows blue fallback

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 3
                || values[0] is not string text
                || values[1] is not string term
                || values[2] is not bool isMatch
                || string.IsNullOrEmpty(term)
                || !isMatch)
            {
                string plain = values.Count > 0 && values[0] is string s ? s : string.Empty;
                return new InlineCollection { new Run(plain) };
            }

            var inlines = new InlineCollection();
            int start = 0;

            while (start < text.Length)
            {
                int idx = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    inlines.Add(new Run(text[start..]));
                    break;
                }

                if (idx > start)
                    inlines.Add(new Run(text[start..idx]));

                inlines.Add(new Run(text.Substring(idx, term.Length))
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = AccentBrush,
                });

                start = idx + term.Length;
            }

            return inlines;
        }
    }
}

    /// <summary>
    /// Multi-value converter that splits a file name into Avalonia <see cref="Run"/> inlines,
    /// bolding the substring that matches the active search term.
    /// 
    /// Bindings (in order):
    ///   [0] string  – the full text to display (e.g. FileData.Namn)
    ///   [1] string  – the search term            (e.g. FileData.SearchTerm)
    ///   [2] bool    – whether this row is a direct match (FileData.IsSearchMatch)
    /// 
    /// When there is no search term, or the row is not a direct match,
    /// the converter returns a single unstyled Run containing the full text.
    /// </summary>
    public class SearchHighlightConverter : IMultiValueConverter
    {
        public static readonly SearchHighlightConverter Instance = new();

        private static readonly FontWeight NormalWeight = FontWeight.Normal;
        private static readonly FontWeight BoldWeight   = FontWeight.Bold;

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 3
                || values[0] is not string text
                || values[1] is not string term
                || values[2] is not bool isMatch
                || string.IsNullOrEmpty(term)
                || !isMatch)
            {
                // No highlighting — return plain text or empty
                string plain = values.Count > 0 && values[0] is string s ? s : string.Empty;
                return new InlineCollection { new Run(plain) };
            }

            var inlines = new InlineCollection();
            int start = 0;

            while (start < text.Length)
            {
                int idx = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    inlines.Add(new Run(text[start..]));
                    break;
                }

                if (idx > start)
                    inlines.Add(new Run(text[start..idx]));

                inlines.Add(new Run(text.Substring(idx, term.Length))
                {
                    FontWeight = BoldWeight,
                    TextDecorations = TextDecorations.Underline
                });

                start = idx + term.Length;
            }

            return inlines;
        }
    }

