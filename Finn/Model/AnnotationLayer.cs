using Avalonia.Media;
using Finn.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Finn.Model;

public class AnnotationLayer : INotifyPropertyChanged
{
    private string _name = "Layer";
    private bool _isVisible = true;
    private Color _color = Color.FromRgb(214, 64, 69);

    /// <summary>Strokes keyed by page number.</summary>
    internal Dictionary<int, List<InkStroke>> PageStrokes { get; } = [];
    internal int StrokeCount { get; set; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public Color Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    public string StatusText => IsVisible
        ? $"{StrokeCount} strokes"
        : $"{StrokeCount} strokes (hidden)";

    public void RefreshStatus() => OnPropertyChanged(nameof(StatusText));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
