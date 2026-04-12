using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Finn.Controls;

/// <summary>
/// Reusable dialog header: accent circle with icon + title + optional subtitle.
/// Place as the first content element inside dialog windows.
/// </summary>
public class DialogHeader : StackPanel
{
    public static readonly StyledProperty<Symbol> IconProperty =
        AvaloniaProperty.Register<DialogHeader, Symbol>(nameof(Icon), Symbol.Info);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<DialogHeader, double>(nameof(IconSize), 32);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DialogHeader, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<DialogHeader, string>(nameof(Subtitle), string.Empty);

    public Symbol Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    private readonly SymbolIcon _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;

    public DialogHeader()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Center;
        Spacing = 6;
        Margin = new Thickness(0, 10, 0, 16);

        var circle = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Gray, // fallback
        };
        // Apply accent color as background; updated dynamically when theme changes.
        circle.GetResourceObservable("SystemAccentColor").Subscribe(new AccentColorObserver(circle));

        _icon = new SymbolIcon
        {
            FontSize = 32,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        circle.Child = _icon;

        _title = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        _subtitle = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 400
        };

        Children.Add(circle);
        Children.Add(_title);
        Children.Add(_subtitle);
    }

    static DialogHeader()
    {
        IconProperty.Changed.AddClassHandler<DialogHeader>((h, _) =>
            h._icon.Symbol = h.Icon);

        IconSizeProperty.Changed.AddClassHandler<DialogHeader>((h, _) =>
            h._icon.FontSize = h.IconSize);

        TitleProperty.Changed.AddClassHandler<DialogHeader>((h, _) =>
            h._title.Text = h.Title);

        SubtitleProperty.Changed.AddClassHandler<DialogHeader>((h, _) =>
        {
            h._subtitle.Text = h.Subtitle;
            h._subtitle.IsVisible = !string.IsNullOrEmpty(h.Subtitle);
        });
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _icon.Symbol = Icon;
        _icon.FontSize = IconSize;
        _title.Text = Title;
        _subtitle.Text = Subtitle;
        _subtitle.IsVisible = !string.IsNullOrEmpty(Subtitle);
    }

    /// <summary>
    /// Lightweight observer that converts a Color resource value to a SolidColorBrush
    /// and assigns it to a Border's Background property.
    /// </summary>
    private sealed class AccentColorObserver(Border target) : IObserver<object?>
    {
        public void OnNext(object? value)
        {
            if (value is Color c)
                target.Background = new SolidColorBrush(c);
            else if (value is IBrush b)
                target.Background = b;
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
