using Avalonia.Media;
using Finn.Controls;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Finn.Model;

public class AnnotationLayer : INotifyPropertyChanged
{
    private string _name = "Layer";
    private bool _isVisible = true;
    private Color _color = Color.FromRgb(214, 64, 69);

    /// <summary>Strokes keyed by page number.</summary>
    [JsonProperty]
    internal Dictionary<int, List<InkStroke>> PageStrokes { get; } = [];
    [JsonIgnore]
    internal int StrokeCount { get; set; }

    /// <summary>Shapes keyed by page number.</summary>
    [JsonProperty]
    internal Dictionary<int, List<ShapeAnnotation>> PageShapes { get; } = [];
    [JsonIgnore]
    internal int ShapeCount { get; set; }

    /// <summary>Text annotations keyed by page number.</summary>
    [JsonProperty]
    internal Dictionary<int, List<TextAnnotation>> PageTexts { get; } = [];
    [JsonIgnore]
    internal int TextCount { get; set; }

    /// <summary>Measurement annotations keyed by page number.</summary>
    [JsonProperty]
    internal Dictionary<int, List<MeasurementAnnotation>> PageMeasurements { get; } = [];
    [JsonIgnore]
    internal int MeasurementCount { get; set; }

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

    public int TotalCount => StrokeCount + ShapeCount + TextCount + MeasurementCount;

    public string StatusText => IsVisible
        ? $"{TotalCount} annotations"
        : $"{TotalCount} annotations (hidden)";

    public void RefreshStatus() => OnPropertyChanged(nameof(StatusText));

    /// <summary>Recompute counts from the dictionaries (e.g. after deserialization).</summary>
    public void RecalculateCounts()
    {
        StrokeCount = PageStrokes.Values.Sum(l => l.Count);
        ShapeCount = PageShapes.Values.Sum(l => l.Count);
        TextCount = PageTexts.Values.Sum(l => l.Count);
        MeasurementCount = PageMeasurements.Values.Sum(l => l.Count);
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
