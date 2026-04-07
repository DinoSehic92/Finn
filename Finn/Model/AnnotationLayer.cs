using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Finn.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Finn.Model;

public class AnnotationLayer : ObservableObject
{
    private string _name = "Layer";
    private bool _isVisible = true;
    private bool _isLocked;
    private Color _color = Color.FromRgb(214, 64, 69);

    /// <summary>Strokes keyed by page number.</summary>
    [JsonInclude]
    internal Dictionary<int, List<InkStroke>> PageStrokes { get; set; } = [];
    [JsonIgnore]
    internal int StrokeCount { get; set; }

    /// <summary>Shapes keyed by page number.</summary>
    [JsonInclude]
    internal Dictionary<int, List<ShapeAnnotation>> PageShapes { get; set; } = [];
    [JsonIgnore]
    internal int ShapeCount { get; set; }

    /// <summary>Text annotations keyed by page number.</summary>
    [JsonInclude]
    internal Dictionary<int, List<TextAnnotation>> PageTexts { get; set; } = [];
    [JsonIgnore]
    internal int TextCount { get; set; }

    /// <summary>Measurement annotations keyed by page number.</summary>
    [JsonInclude]
    internal Dictionary<int, List<MeasurementAnnotation>> PageMeasurements { get; set; } = [];
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

    public bool IsLocked
    {
        get => _isLocked;
        set { _isLocked = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public Color Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
    }

    public int TotalCount => StrokeCount + ShapeCount + TextCount + MeasurementCount;

    public string StatusText
    {
        get
        {
            var suffix = "";
            if (!IsVisible) suffix += " (hidden)";
            if (IsLocked) suffix += " (locked)";
            return $"{TotalCount} annotations{suffix}";
        }
    }

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
}
