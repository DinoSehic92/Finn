using System.Collections.Generic;

namespace Finn.Model
{
    public enum AnnotationTool
    {
        None,
        Draw,
        Highlight
    }

    /// <summary>
    /// A single freehand or highlight annotation on a diff page.
    /// Coordinates are stored in content-space (relative to ContentArea)
    /// so they scale correctly with zoom/pan.
    /// </summary>
    public class DiffAnnotation
    {
        public AnnotationTool Tool { get; set; }
        public List<(double X, double Y)> Points { get; set; } = [];
        public string Color { get; set; } = "#D64045";
        public double StrokeWidth { get; set; } = 3;
        public int PageIndex { get; set; }
    }
}
