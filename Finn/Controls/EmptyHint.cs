using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Finn.Controls;

/// <summary>
/// Lightweight empty-state overlay: icon + title + subtitle, centered in the
/// parent Panel.  Place it as a sibling of the DataGrid inside a Panel.
/// Set <see cref="IsActive"/> to show/hide.
/// </summary>
public class EmptyHint : StackPanel
{
    public static readonly StyledProperty<Symbol> IconProperty =
        AvaloniaProperty.Register<EmptyHint, Symbol>(nameof(Icon), Symbol.Info);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<EmptyHint, double>(nameof(IconSize), 22);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyHint, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<EmptyHint, string>(nameof(Subtitle), string.Empty);

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

    public EmptyHint()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Opacity = 0.4;
        IsVisible = false;
        // Push down slightly so it sits below DataGrid column headers;
        // horizontal padding keeps wrapped text away from edges.
        Margin = new Thickness(12, 20, 12, 0);

        _icon = new SymbolIcon { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        _title = new TextBlock
        {
            FontSize = 12,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _subtitle = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Opacity = 0.7,
            Margin = new Thickness(0, 2, 0, 0)
        };

        Children.Add(_icon);
        Children.Add(_title);
        Children.Add(_subtitle);
    }

    static EmptyHint()
    {
        IconProperty.Changed.AddClassHandler<EmptyHint>((hint, _) =>
            hint._icon.Symbol = hint.Icon);

        IconSizeProperty.Changed.AddClassHandler<EmptyHint>((hint, _) =>
            hint._icon.FontSize = hint.IconSize);

        TitleProperty.Changed.AddClassHandler<EmptyHint>((hint, _) =>
            hint._title.Text = hint.Title);

        SubtitleProperty.Changed.AddClassHandler<EmptyHint>((hint, _) =>
        {
            hint._subtitle.Text = hint.Subtitle;
            hint._subtitle.IsVisible = !string.IsNullOrEmpty(hint.Subtitle);
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
}
