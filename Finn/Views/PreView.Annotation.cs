using Finn.ViewModels;
using Finn.Model;
using Finn.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MuPDFCore.MuPDFRenderer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Finn.Views;

public partial class PreView
{
    #region Inline Annotation

    // ── Hit-test thresholds: target screen-pixel sizes ──
    // These are converted to PDF units at the current zoom via HitRadius().
    private const double ArrowTipHitScreenPx = 10;
    private const double HandleHitScreenPx = 14;
    private const double HandleHitScreenPxLarge = 16;
    private const double ResizeHandleHitScreenPx = 18;

    /// <summary>Converts a screen-pixel hit radius to PDF units at the current zoom,
    /// so hit areas stay a constant screen size regardless of zoom level.</summary>
    private double HitRadius(double screenPixels)
        => MuPDFRenderer.ScreenToPdfDistance(screenPixels);

    private static double DistanceSq(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool IsNear(Point a, Point b, double radius)
        => DistanceSq(a, b) <= radius * radius;

    private static readonly Avalonia.Input.Cursor CursorSizeWE = new(Avalonia.Input.StandardCursorType.SizeWestEast);

    private bool _annotateMode;
    internal bool IsAnnotating => _annotateMode;
    private bool _inkDrawing;
    private Avalonia.Controls.ContextMenu? _savedContextMenu;
    private Button? _activeToolButton;
    private Button? _activeColorButton;
    private Button? _activeWidthButton;
    private Button? _activeDashButton;
    private Button? _undoBtn;
    private Button? _redoBtn;
    private double _normalStrokeWidth = 2;
    private Point? _textPlacementPdfPoint;
    private TextAnnotation? _editingTextAnnotation;
    private TextAnnotation? _draggingTextAnnotation;
    /// <summary>When non-null, we're dragging only the arrow tip (origin) of this annotation.</summary>
    private TextAnnotation? _draggingArrowOrigin;
    private Point _dragStartPdf;
    private bool _calibrationMode;
    private Point? _arrowTextOrigin;
    private object? _selectDragItem;
    /// <summary>When non-null, we're dragging a single vertex of this shape or measurement.</summary>
    private object? _draggingVertexItem;
    /// <summary>0 = Start/Points[0], 1 = End/Points[1].</summary>
    private int _draggingVertexIndex;
    /// <summary>When non-null, we're resizing the MaxWidth of this text annotation via the right-edge handle.</summary>
    private TextAnnotation? _resizingTextAnnotation;
    /// <summary>Persistent selection: stays non-null after drag so color/width/font changes apply.</summary>
    private object? _selectedAnnotation;
    /// <summary>Full set of selected annotations for multi-select (Shift+Click).</summary>
    private readonly HashSet<object> _selectedAnnotations = new();
    /// <summary>Clipboard for annotation copy/paste.</summary>
    private object? _annotationClipboard;
    /// <summary>Pre-drag position snapshot for undo support. Captured on drag-start, pushed on drag-end.</summary>
    private object? _preDragSnapshot;
    /// <summary>Pre-drag snapshots for all selected items during multi-select drag.</summary>
    private List<object>? _multiDragSnapshots;
    /// <summary>Timestamp of the last arrow-key nudge for undo coalescing.</summary>
    private DateTime _lastNudgeTime;
    /// <summary>Toolbar state saved before selection syncing, restored on deselect.</summary>
    private Color _preSelectColor;
    private double _preSelectWidth;
    private LineDashPattern _preSelectDash;
    private double _preSelectOpacity;
    private bool _hasPreSelectState;
    /// <summary>True while the user is dragging a rubber-band marquee rectangle in Select mode.</summary>
    private bool _rubberBandActive;
    private bool _rubberBandAdditive;
    private bool _rubberBandSubtractive;
    /// <summary>True when the rubber-band drag was initiated right-to-left (crossing/touching mode).</summary>
    private bool _rubberBandCrossing;
    /// <summary>PDF-space start point of the rubber-band rectangle.</summary>
    private Point _rubberBandStartPdf;
    /// <summary>The annotation currently being edited via the property panel (double-click).</summary>
    private object? _propertyPanelTarget;
    /// <summary>True while the format-painter style matching flow is waiting for a target annotation.</summary>
    private bool _matchStyleArmed;
    /// <summary>The annotation whose style should be applied to the next clicked target.</summary>
    private object? _matchStyleSource;
    /// <summary>
    /// The last non-Select tool the user explicitly activated (or that was active when a
    /// shape/polyline completed and auto-switched to Select).
    /// Enter key re-activates this tool — "repeat last command" like AutoCAD.
    /// </summary>
    private InlineAnnotationTool? _lastUsedTool;
    /// <summary>Whether the centered helper text below the toolbar is visible.</summary>
    private bool _showAnnotationStatusHints = true;
    /// <summary>Cached reference to the status hint border control.</summary>
    private Border? _annotationStatusBorder;
    /// <summary>Cached reference to the status hint text control.</summary>
    private TextBlock? _annotationStatusText;
    /// <summary>Cached reference to the status hints toggle button.</summary>
    private Button? _statusHintsToggleBtn;
    /// <summary>Arrow origin point staged during ArrowText two-click placement.</summary>
    private Point? _pendingArrowOrigin;
    /// <summary>True when the pending text input is for a sticky note.</summary>
    private bool _pendingStickyNote;
    /// <summary>Collects property snapshots while the property panel is open so related edits undo as a single step.</summary>
    private readonly Dictionary<object, object> _propertySessionSnapshots = new();

    // ── Group resize drag state ──
    /// <summary>True while the user is dragging a corner handle of the combined selection bounding box.</summary>
    private bool _groupResizing;
    /// <summary>The PDF-space anchor corner (opposite to the dragged corner).</summary>
    private Point _groupResizeAnchor;
    /// <summary>The original corner being dragged (used for signed distance computation).</summary>
    private Point _groupResizeDragCorner;
    /// <summary>Pre-resize scale-state for restoring sizes before reapplying (position + sizes).</summary>
    private List<AnnotatedPDFRenderer.GroupResizeSnapshot>? _groupResizeStates;

    /// <summary>Resets all drag/drawing state flags to idle. Called by tool-switch and deactivate.</summary>
    private void ResetDragState()
    {
        _inkDrawing = false;
        _draggingTextAnnotation = null;
        _draggingArrowOrigin = null;
        _draggingVertexItem = null;
        _resizingTextAnnotation = null;
        _selectDragItem = null;
        _rubberBandActive = false;
        _rubberBandCrossing = false;
        _groupResizing = false;
        _groupResizeStates = null;
        _preDragSnapshot = null;
        _multiDragSnapshots = null;
        MuPDFRenderer.ClearRubberBand();
    }

    private void SetAnnotationStatusText(string? text)
    {
        _annotationStatusBorder ??= this.FindControl<Border>("AnnotationStatusBorder");
        _annotationStatusText ??= this.FindControl<TextBlock>("AnnotationStatusText");
        if (_annotationStatusBorder == null || _annotationStatusText == null) return;

        bool visible = _annotateMode
            && _showAnnotationStatusHints
            && !string.IsNullOrWhiteSpace(text);

        _annotationStatusText.Text = text ?? string.Empty;
        _annotationStatusBorder.IsVisible = visible;
    }

    private void StagePropertyUndoSnapshot(object item)
    {
        if (_propertySessionSnapshots.ContainsKey(item)) return;
        var snap = MuPDFRenderer.CapturePropertySnapshot(item);
        if (snap != null)
            _propertySessionSnapshots[item] = snap;
    }

    private void FlushPropertySessionUndo()
    {
        if (_propertySessionSnapshots.Count == 0) return;
        MuPDFRenderer.PushGroupPropertyUndo(_propertySessionSnapshots.Values.ToList());
        _propertySessionSnapshots.Clear();
    }

    private void ClearPropertySessionUndo() => _propertySessionSnapshots.Clear();

    private string GetDefaultToolStatusText()
    {
        if (_selectedAnnotations.Count > 1)
            return "Selection: drag to move, drag corners to resize, Delete to remove, Esc to deselect.";

        if (_selectedAnnotation != null && MuPDFRenderer.ActiveTool == InlineAnnotationTool.Select)
            return "Select: drag to move, drag handles to edit, double-click to open properties.";

        return MuPDFRenderer.ActiveTool switch
        {
            InlineAnnotationTool.Select => "Select: click to select, Shift+click for multi-select, drag empty space to marquee select.",
            InlineAnnotationTool.Draw => "Draw: drag to sketch freehand, right-click or Esc to cancel.",
            InlineAnnotationTool.Highlight => "Highlight: drag to mark up content, right-click or Esc to cancel.",
            InlineAnnotationTool.Polyline => "Polyline: click to add points, click the first point or press Enter to close, Esc to cancel.",
            InlineAnnotationTool.Dot => "Dot: click to place a dot annotation.",
            InlineAnnotationTool.Rectangle => "Rectangle: click once to start, move the mouse, click again to finish. Hold Shift for a square.",
            InlineAnnotationTool.Ellipse => "Ellipse: click once to start, move the mouse, click again to finish. Hold Shift for a circle.",
            InlineAnnotationTool.Line => "Line: click once to start, move the mouse, click again to finish. Hold Shift to constrain angles.",
            InlineAnnotationTool.Arrow => "Arrow: click once to start, move the mouse, click again to finish. Hold Shift to constrain angles.",
            InlineAnnotationTool.RevisionCloud => "Revision Cloud: click once to start, move the mouse, click again to finish.",
            InlineAnnotationTool.Text => "Comment: click to place a text box, Enter to save, Shift+Enter for a newline.",
            InlineAnnotationTool.ArrowText => "Arrow Comment: click the arrow origin, click the text position, then enter your comment.",
            InlineAnnotationTool.StickyNote => "Sticky Note: click to place a note, then type your comment.",
            InlineAnnotationTool.MeasureDistance => _calibrationMode
                ? "Calibration: draw a reference line, then enter the real-world distance."
                : "Measure: click the start point, move the mouse, click again to finish.",
            InlineAnnotationTool.MeasureArea => "Area Measure: click to add points, click the first point or press Enter to close and calculate area.",
            InlineAnnotationTool.Eraser => "Eraser: click or drag across annotations to remove them.",
            _ => string.Empty,
        };
    }

    private void UpdateAnnotationStatusHint()
    {
        string text = _matchStyleArmed
            ? "Match Style: click a target annotation to apply the style. Right-click or Esc cancels."
            : _textPlacementPdfPoint.HasValue || _editingTextAnnotation != null
                ? "Text Editing: Enter saves, Shift+Enter inserts a new line, Esc cancels."
                : _arrowTextOrigin != null
                    ? "Arrow Comment: choose where the text box should go."
                    : MuPDFRenderer.HasActivePolyline
                        ? MuPDFRenderer.ActiveTool == InlineAnnotationTool.MeasureArea
                            ? $"Area Measure: keep clicking to add vertices (min. 3), then click the first point or press Enter to close ({MuPDFRenderer.ActivePolylinePointCount} so far)."
                            : "Polyline: keep clicking to add vertices, then click the first point or press Enter to close, right-click to finish open."
                        : MuPDFRenderer.HasActiveMeasurement
                            ? (_calibrationMode
                                ? "Calibration: finish the reference line to enter the known distance."
                                : "Measure: click the end point to finish the measurement.")
                            : MuPDFRenderer.HasActiveShape
                                ? "Shape: move the mouse to preview, click again to finish. Hold Shift to constrain."
                                : GetDefaultToolStatusText();

        SetAnnotationStatusText(text);
    }

    private void ShowHoverEditHint(object? hoverHit)
    {
        if (!_annotateMode || !_showAnnotationStatusHints || _matchStyleArmed) return;
        if (_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null || MuPDFRenderer.HasActivePolyline || MuPDFRenderer.HasActiveMeasurement || MuPDFRenderer.HasActiveShape)
            return;

        string? text = hoverHit switch
        {
            TextAnnotation => "Double-click to edit text. Single-click to select and drag.",
            ShapeAnnotation or InkStroke or MeasurementAnnotation => "Double-click to edit properties. Single-click to select and drag.",
            _ => null
        };

        if (text != null)
            SetAnnotationStatusText(text);
        else
            UpdateAnnotationStatusHint();
    }

    private void ArmMatchStyle(object source)
    {
        _matchStyleSource = source;
        _matchStyleArmed = true;
        MuPDFRenderer.Cursor = CursorHand;
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    private void CancelMatchStyle(bool restoreCursor = true)
    {
        _matchStyleArmed = false;
        _matchStyleSource = null;
        if (restoreCursor && _annotateMode)
            MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
        UpdateAnnotationStatusHint();
    }

    private void ApplyMatchedStyle(object source, object target)
    {
        Color? sourceColor = source switch
        {
            InkStroke s => s.Color,
            ShapeAnnotation s => s.Color,
            TextAnnotation t => t.Color,
            MeasurementAnnotation m => m.Color,
            _ => null
        };
        double? sourceOpacity = source switch
        {
            InkStroke s => s.Opacity,
            ShapeAnnotation s => s.Opacity,
            TextAnnotation t => t.Opacity,
            _ => null
        };
        double? sourceWidth = source switch
        {
            InkStroke s => s.Width,
            ShapeAnnotation s => s.StrokeWidth,
            _ => null
        };
        LineDashPattern? sourceDash = source switch
        {
            InkStroke s => s.DashPattern,
            ShapeAnnotation s => s.DashPattern,
            _ => null
        };
        bool? sourceFill = source switch
        {
            ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud } s => s.IsFilled,
            InkStroke { IsPolyline: true, IsClosed: true } s => s.IsFilled,
            _ => null
        };
        double? sourceCornerRadius = source switch
        {
            ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle } s => s.CornerRadius,
            InkStroke { IsPolyline: true, IsAreaMeasure: false } s => s.CornerRadius,
            _ => null
        };
        double? sourceFontSize = source switch
        {
            TextAnnotation t => t.FontSize,
            _ => null
        };
        bool? sourceHasFrame = source switch
        {
            TextAnnotation t => t.HasFrame,
            _ => null
        };

        var snapshot = MuPDFRenderer.CapturePropertySnapshot(target);
        if (snapshot != null)
            MuPDFRenderer.PushPropertyUndo(snapshot);

        switch (target)
        {
            case InkStroke ink:
                if (sourceColor.HasValue) ink.Color = sourceColor.Value;
                if (sourceOpacity.HasValue) ink.Opacity = sourceOpacity.Value;
                if (sourceWidth.HasValue) ink.Width = sourceWidth.Value;
                if (sourceDash.HasValue) ink.DashPattern = sourceDash.Value;
                if (sourceCornerRadius.HasValue && ink.IsPolyline && !ink.IsAreaMeasure)
                    ink.CornerRadius = sourceCornerRadius.Value;
                if (sourceFill.HasValue && ink.IsPolyline && ink.IsClosed)
                    ink.IsFilled = sourceFill.Value;
                ink.InvalidatePen();
                break;

            case ShapeAnnotation shape:
                if (sourceColor.HasValue) shape.Color = sourceColor.Value;
                if (sourceOpacity.HasValue) shape.Opacity = sourceOpacity.Value;
                if (sourceWidth.HasValue) shape.StrokeWidth = sourceWidth.Value;
                if (sourceDash.HasValue) shape.DashPattern = sourceDash.Value;
                if (sourceCornerRadius.HasValue && shape.ShapeType == InlineAnnotationTool.Rectangle)
                    shape.CornerRadius = sourceCornerRadius.Value;
                if (sourceFill.HasValue && shape.ShapeType is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud)
                    shape.IsFilled = sourceFill.Value;
                shape.InvalidatePen();
                break;

            case TextAnnotation text:
                if (sourceColor.HasValue) text.Color = sourceColor.Value;
                if (sourceOpacity.HasValue) text.Opacity = sourceOpacity.Value;
                if (sourceHasFrame.HasValue) text.HasFrame = sourceHasFrame.Value;
                if (sourceFontSize.HasValue)
                {
                    text.FontSize = sourceFontSize.Value;
                    MuPDFRenderer.TextFontSize = sourceFontSize.Value;
                    UpdateFontSizeLabel();
                }
                break;

            case MeasurementAnnotation measurement:
                if (sourceColor.HasValue) measurement.Color = sourceColor.Value;
                if (pwr != null) pwr.StatusMessage = "Style applied (color only for measurements)";
                break;
        }

        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
    }

    /// <summary>
    /// Common epilogue for all drag-release paths: pushes undo, clears drag state, restores cursor.
    /// </summary>
    private void FinishDrag(bool isPropertyUndo = false)
    {
        if (_preDragSnapshot != null)
        {
            if (isPropertyUndo) MuPDFRenderer.PushPropertyUndo(_preDragSnapshot);
            else MuPDFRenderer.PushMoveUndo(_preDragSnapshot);
            _preDragSnapshot = null;
        }
        if (_multiDragSnapshots != null)
        {
            MuPDFRenderer.PushGroupMoveUndo(_multiDragSnapshots);
            _multiDragSnapshots = null;
        }
        if (_groupResizing && _groupResizeStates is { Count: > 0 })
        {
            MuPDFRenderer.PushGroupResizeUndo(_groupResizeStates);
        }
        _groupResizeStates = null;
        _draggingVertexItem = null;
        _draggingArrowOrigin = null;
        _draggingTextAnnotation = null;
        _resizingTextAnnotation = null;
        _selectDragItem = null;
        _groupResizing = false;
        MuPDFRenderer.ClearSnapGuides();
        MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
        MuPDFRenderer.NotifyAnnotationChanged();
    }

    /// <summary>Tests whether the click is near a corner of the combined selection bounding box
    /// and begins a proportional resize drag if so.</summary>
    private bool TryBeginGroupResize(Point pdfPoint)
    {
        var combined = AnnotatedPDFRenderer.GetCombinedBounds(_selectedAnnotations);
        if (combined.Width <= 0 && combined.Height <= 0) return false;

        double hitR = HitRadius(ResizeHandleHitScreenPx);
        var corners = new (Point corner, Point anchor)[]
        {
            (combined.TopLeft,     combined.BottomRight),
            (combined.TopRight,    combined.BottomLeft),
            (combined.BottomLeft,  combined.TopRight),
            (combined.BottomRight, combined.TopLeft)
        };

        foreach (var (corner, anchor) in corners)
        {
            if (IsNear(pdfPoint, corner, hitR))
            {
                _groupResizing = true;
                _groupResizeAnchor = anchor;
                _groupResizeDragCorner = corner;
                _dragStartPdf = pdfPoint;
                _groupResizeStates = [];
                foreach (var sel in _selectedAnnotations)
                    _groupResizeStates.Add(AnnotatedPDFRenderer.CaptureGroupResizeSnapshot(sel));
                _inkDrawing = true;
                if (corner == combined.TopLeft || corner == combined.BottomRight)
                    MuPDFRenderer.Cursor = CursorSizeNWSE;
                else
                    MuPDFRenderer.Cursor = CursorSizeNESW;
                return true;
            }
        }
        return false;
    }

    /// <summary>Resets text input overlay state and restores focus.</summary>
    private void CloseTextInput()
    {
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = null;
        _pendingStickyNote = false;
        _arrowTextOrigin = null;
        MuPDFRenderer.ClearArrowTextPreview();
        // Re-show the actions row that was hidden for new text placement
        PropertyActionsRow.IsVisible = true;
        ClosePropertyPanel();
        MuPDFRenderer.Focus();
    }

    /// <summary>Selects all annotations on the current page (shared by Ctrl+A and context menu).</summary>
    private void SelectAllOnPage()
    {
        var all = MuPDFRenderer.GetAllAnnotationsOnPage();
        if (all.Count == 0) return;
        SavePreSelectState();
        _selectedAnnotations.Clear();
        MuPDFRenderer.ClearSelectHighlight();
        foreach (var item in all)
        {
            _selectedAnnotations.Add(item);
            MuPDFRenderer.AddSelectHighlight(item);
        }
        _selectedAnnotation = all[0];
        SyncToolbarToSelection();
        MuPDFRenderer.InvalidateVisual();
    }

    /// <summary>
    /// Attempts to begin a vertex drag on the given annotation item.
    /// Returns true if a vertex was near the click point and dragging has started.
    /// </summary>
    private bool TryBeginVertexDrag(object item, Point pdfPoint, double radius)
    {
        double radiusSq = radius * radius;
        switch (item)
        {
            case ShapeAnnotation shape when shape.ShapeType != InlineAnnotationTool.Dot:
            {
                double distS = DistanceSq(pdfPoint, shape.Start);
                double distE = DistanceSq(pdfPoint, shape.End);
                if (distS <= radiusSq || distE <= radiusSq)
                {
                    _draggingVertexItem = shape;
                    _draggingVertexIndex = distS <= distE ? 0 : 1;
                    _dragStartPdf = pdfPoint;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(shape);
                    _inkDrawing = true;
                    return true;
                }
                break;
            }
            case MeasurementAnnotation meas when meas.Points.Count >= 2:
            {
                double dist0 = DistanceSq(pdfPoint, meas.Points[0]);
                double dist1 = DistanceSq(pdfPoint, meas.Points[1]);
                if (dist0 <= radiusSq || dist1 <= radiusSq)
                {
                    _draggingVertexItem = meas;
                    _draggingVertexIndex = dist0 <= dist1 ? 0 : 1;
                    _dragStartPdf = pdfPoint;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(meas);
                    _inkDrawing = true;
                    return true;
                }
                break;
            }
            case InkStroke { IsPolyline: true } poly when poly.Points.Count >= 2:
            {
                int bestIdx = -1;
                double bestDist = double.MaxValue;
                for (int i = 0; i < poly.Points.Count; i++)
                {
                    double d = DistanceSq(pdfPoint, poly.Points[i]);
                    if (d <= radiusSq && d < bestDist) { bestDist = d; bestIdx = i; }
                }
                if (bestIdx >= 0)
                {
                    _draggingVertexItem = poly;
                    _draggingVertexIndex = bestIdx;
                    _dragStartPdf = pdfPoint;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(poly);
                    _inkDrawing = true;
                    return true;
                }
                break;
            }
        }
        return false;
    }

    private void DeselectAnnotation()
    {
        _selectedAnnotation = null;
        _selectedAnnotations.Clear();
        MuPDFRenderer.ClearSelectHighlight();
        RestorePreSelectState();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    /// <summary>Saves the current toolbar state so it can be restored after deselecting.</summary>
    private void SavePreSelectState()
    {
        if (_hasPreSelectState) return;
        _preSelectColor = MuPDFRenderer.StrokeColor;
        _preSelectWidth = MuPDFRenderer.IsHighlighterMode ? _normalStrokeWidth : MuPDFRenderer.StrokeWidth;
        _preSelectDash = MuPDFRenderer.StrokeDashPattern;
        _preSelectOpacity = MuPDFRenderer.StrokeOpacity;
        _hasPreSelectState = true;
    }

    /// <summary>Restores the toolbar state that was active before the last selection.</summary>
    private void RestorePreSelectState()
    {
        if (!_hasPreSelectState) return;
        _hasPreSelectState = false;
        MuPDFRenderer.StrokeColor = _preSelectColor;
        if (!MuPDFRenderer.IsHighlighterMode)
        {
            MuPDFRenderer.StrokeWidth = _preSelectWidth;
            _normalStrokeWidth = _preSelectWidth;
        }
        MuPDFRenderer.StrokeDashPattern = _preSelectDash;
        MuPDFRenderer.StrokeOpacity = _preSelectOpacity;
        SetActiveColorButton(MatchColorTag(_preSelectColor) is { } tag ? FindToolbarButtonByTag(tag) : null);
        SetActiveWidthButton(FindToolbarButtonByTag(((int)_preSelectWidth).ToString()));
        SetActiveDashButton(FindToolbarButtonByTag(_preSelectDash.ToString()));
        if (OpacitySlider != null) OpacitySlider.Value = _preSelectOpacity;
        UpdateColorIndicator(_preSelectColor);
        UpdateBrushSizeIndicator(_preSelectWidth);
        UpdateDashIndicator(_preSelectDash);
    }

    private void SelectAnnotation(object item)
    {
        SavePreSelectState();
        _selectedAnnotation = item;
        _selectedAnnotations.Clear();
        _selectedAnnotations.Add(item);

        MuPDFRenderer.ClearSelectHighlight();
        foreach (var sel in _selectedAnnotations)
            MuPDFRenderer.AddSelectHighlight(sel);

        SyncToolbarToSelection();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    /// <summary>Toggle an item in/out of multi-selection (Shift+Click).</summary>
    private void ToggleAnnotationSelection(object item)
    {
        if (_selectedAnnotations.Contains(item))
        {
            _selectedAnnotations.Remove(item);
            MuPDFRenderer.RemoveSelectHighlight(item);
            _selectedAnnotation = _selectedAnnotations.Count > 0 ? _selectedAnnotations.First() : null;
        }
        else
        {
            _selectedAnnotations.Add(item);
            MuPDFRenderer.AddSelectHighlight(item);
            _selectedAnnotation = item;
        }
        if (_selectedAnnotation != null)
            SyncToolbarToSelection();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    /// <summary>Paste a copy of the clipboard annotation(s) with a small offset.</summary>
    private void PasteAnnotation()
    {
        if (_annotationClipboard == null) return;
        if (_annotationClipboard is List<object> multiClipboard)
        {
            MuPDFRenderer.BeginGroupAdd();
            foreach (var item in multiClipboard)
                PasteSingleAnnotation(item);
            MuPDFRenderer.EndGroupAdd();
        }
        else
        {
            PasteSingleAnnotation(_annotationClipboard);
        }
    }

    private void PasteSingleAnnotation(object source)
    {
        const double offset = 15;
        switch (source)
        {
            case TextAnnotation src:
            {
                var copy = new TextAnnotation
                {
                    Position = new Point(src.Position.X + offset, src.Position.Y + offset),
                    Text = src.Text, FontSize = src.FontSize, Color = src.Color,
                    Opacity = src.Opacity, FontFamily = src.FontFamily,
                    IsStickyNote = src.IsStickyNote,
                    IsLabel = src.IsLabel,
                    HasFrame = src.HasFrame,
                    MaxWidth = src.MaxWidth,
                    ArrowOrigin = src.ArrowOrigin.HasValue
                        ? new Point(src.ArrowOrigin.Value.X + offset, src.ArrowOrigin.Value.Y + offset)
                        : null
                };
                MuPDFRenderer.PlaceTextAnnotation(copy);
                break;
            }
            case ShapeAnnotation src:
            {
                var copy = new ShapeAnnotation
                {
                    ShapeType = src.ShapeType,
                    Start = new Point(src.Start.X + offset, src.Start.Y + offset),
                    End = new Point(src.End.X + offset, src.End.Y + offset),
                    Color = src.Color, StrokeWidth = src.StrokeWidth,
                    Opacity = src.Opacity, IsFilled = src.IsFilled,
                    DashPattern = src.DashPattern,
                    CornerRadius = src.CornerRadius
                };
                MuPDFRenderer.PlaceShape(copy);
                break;
            }
            case InkStroke src:
            {
                var copy = new InkStroke
                {
                    Color = src.Color, Width = src.Width,
                    Opacity = src.Opacity, IsHighlighter = src.IsHighlighter,
                    IsPolyline = src.IsPolyline,
                    IsClosed = src.IsClosed,
                    IsFilled = src.IsFilled,
                    IsAreaMeasure = src.IsAreaMeasure,
                    AreaScale = src.AreaScale,
                    DashPattern = src.DashPattern,
                    CornerRadius = src.CornerRadius
                };
                foreach (var p in src.Points)
                    copy.Points.Add(new Point(p.X + offset, p.Y + offset));
                MuPDFRenderer.PlaceStroke(copy);
                break;
            }
            case MeasurementAnnotation src:
            {
                var copy = new MeasurementAnnotation
                {
                    Color = src.Color, Scale = src.Scale
                };
                foreach (var p in src.Points)
                    copy.Points.Add(new Point(p.X + offset, p.Y + offset));
                MuPDFRenderer.PlaceMeasurement(copy);
                break;
            }
        }
    }

    private void OnToggleAnnotate(object sender, RoutedEventArgs e)
    {
        if (AnnotateToggle.IsChecked == true)
            ActivateAnnotateMode();
        else
            DeactivateAnnotateMode();
    }

    private void ActivateAnnotateMode()
    {
        if (_annotateMode) return;
        // If screenshot mode is active, cancel it cleanly before entering annotation mode
        if (_screenshotMode) DeactivateScreenshotMode();
        // Block activation while in dual-file mode (ambiguous annotation target)
        if (pwr.DualFileMode && !pwr.WhiteboardMode) return;

        // Close diff renderer modes at the view level FIRST so that renderer
        // opacity, column positions and display-area sync are fully restored
        // before we collapse the ViewModel modes below.
        CloseDiffViews();

        // Annotation only works in single-page view. Collapse any remaining dual modes.
        if (pwr.TwopageMode)
            pwr.TwopageMode = false;
        if (pwr.DualFileMode)
            pwr.DualFileMode = false;

        _annotateMode = true;
        pwr.AnnotationActive = true;
        AnnotateToggle.IsChecked = true;
        AnnotateToolbar.IsVisible = true;
        MuPDFRenderer.Cursor = CursorArrow;
        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.Pan;
        MuPDFRenderer.ActiveTool = InlineAnnotationTool.Draw;

        // Clear any residual selection state from a previous session
        _selectedAnnotations.Clear();
        _selectedAnnotation = null;
        _hasPreSelectState = false;
        _calibrationMode = false;
        _rubberBandActive = false;

        MuPDFRenderer.EnsureDefaultLayer();
        // Remove before adding to guard against double-registration if ActivateAnnotateMode
        // is called while already active (e.g. fast toggle or mode-switch edge cases).
        MuPDFRenderer.RemoveHandler(PointerPressedEvent, OnInkPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerMovedEvent, OnInkPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnInkPointerReleased);
        this.RemoveHandler(KeyDownEvent, OnAnnotateKeyDown);
        this.RemoveHandler(KeyUpEvent, OnAnnotateKeyUp);
        MuPDFRenderer.AnnotationChanged -= OnAnnotationChanged;

        MuPDFRenderer.AddHandler(PointerPressedEvent, OnInkPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerMovedEvent, OnInkPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerReleasedEvent, OnInkPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.AddHandler(KeyDownEvent, OnAnnotateKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.AddHandler(KeyUpEvent, OnAnnotateKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Suppress the default context menu while annotating
        _savedContextMenu = MuPDFRenderer.ContextMenu as Avalonia.Controls.ContextMenu;
        MuPDFRenderer.ContextMenu = null;

        // Subscribe to annotation changes for badge + undo/redo button state
        MuPDFRenderer.AnnotationChanged += OnAnnotationChanged;

        // Cache undo/redo buttons to avoid FindControl tree walks on every change
        _undoBtn ??= this.FindControl<Button>("UndoBtn");
        _redoBtn ??= this.FindControl<Button>("RedoBtn");

        // Highlight default tool/color/width buttons to match initial state
        HighlightInitialButtons();
        UpdateUndoRedoButtons();
        UpdateActiveLayerLabel();
        SyncStatusHintsToggleButton();
        UpdateAnnotationStatusHint();
    }

    private void DeactivateAnnotateMode()
    {
        if (!_annotateMode) return;

        if (pwr.AnnotationPageListMode)
        {
            pwr.ClearSearch();
            pwr.SearchMode = false;
        }

        _annotateMode = false;
        pwr.AnnotationActive = false;
        AnnotateToggle.IsChecked = false;
        AnnotateToolbar.IsVisible = false;
        CalibrationCanvas.IsVisible = false;
        ColorInputCanvas.IsVisible = false;
        PropertyPanelCanvas.IsVisible = false;
        PropertyPanelCanvas.Background = Avalonia.Media.Brushes.Transparent;
        ResetDragState();
        CancelMatchStyle(restoreCursor: false);
        _selectedAnnotations.Clear();
        _selectedAnnotation = null;
        _hasPreSelectState = false;
        _rubberBandActive = false;
        _rubberBandCrossing = false;
        _calibrationMode = false;
        _arrowTextOrigin = null;
        _pendingStickyNote = false;
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        MuPDFRenderer.CancelStroke();
        MuPDFRenderer.CancelPolyline();
        MuPDFRenderer.ClearSelectHighlight();
        MuPDFRenderer.ClearSnapGuides();
        MuPDFRenderer.UpdateCursorPreview(null);
        MuPDFRenderer.ClearTextPlacementPreview();
        MuPDFRenderer.SnapToGrid = false;
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.Cursor = Avalonia.Input.Cursor.Default;
        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.PanHighlight;
        MuPDFRenderer.ActiveTool = InlineAnnotationTool.Select;

        MuPDFRenderer.RemoveHandler(PointerPressedEvent, OnInkPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerMovedEvent, OnInkPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnInkPointerReleased);
        this.RemoveHandler(KeyDownEvent, OnAnnotateKeyDown);
        this.RemoveHandler(KeyUpEvent, OnAnnotateKeyUp);
        _spaceHeld = false;

        MuPDFRenderer.AnnotationChanged -= OnAnnotationChanged;

        // Restore the context menu
        if (_savedContextMenu != null)
        {
            MuPDFRenderer.ContextMenu = _savedContextMenu;
            _savedContextMenu = null;
        }
        SetActiveToolButton(null);
        SetActiveColorButton(null);
        SetActiveWidthButton(null);
        SetActiveDashButton(null);
        SetAnnotationStatusText(null);
    }

    private void OnAnnotateKeyDown(object? sender, KeyEventArgs e)
    {
        // Safety: handler should only be registered during annotation mode.
        // Guard against mismatched register/remove leaving this active.
        if (!_annotateMode) return;

        // While the text input is open, only intercept Escape so the user
        // can still type Shift+digits (!, @, …), capital letters, brackets, etc.
        if (_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null)
        {
            if (e.Key == Key.Escape)
            {
                OnTextInputCancel(this, e);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Oem2 && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            AnnotateShortcutsCanvas.IsVisible = !AnnotateShortcutsCanvas.IsVisible;
            e.Handled = true;
            return;
        }

        // Delete/Backspace: always check first, outside the else-if chain
        if (e.Key is Key.Delete or Key.Back && _selectedAnnotations.Count > 0)
        {
            foreach (var item in _selectedAnnotations.ToList())
                MuPDFRenderer.DeleteAnnotation(item);
            _selectedAnnotation = null;
            _selectedAnnotations.Clear();
            e.Handled = true;
            return;
        }

        // Enter: close active polyline (only when no overlay dialog is open)
        if (e.Key == Key.Enter && MuPDFRenderer.HasActivePolyline && !CalibrationCanvas.IsVisible && !ColorInputCanvas.IsVisible)
        {
            // Area measurement needs at least 3 points to form a valid polygon.
            // If the user presses Enter with fewer, cancel silently instead of leaving a line.
            if (MuPDFRenderer.IsActivePolylineAreaMeasure && MuPDFRenderer.ActivePolylinePointCount < 3)
                MuPDFRenderer.CancelPolyline();
            else
                MuPDFRenderer.EndPolyline(close: true);
            ApplyToolSwitch(InlineAnnotationTool.Select);
            e.Handled = true;
            return;
        }

        // Enter (no active polyline): repeat last tool — re-activate the tool used before
        // the automatic switch to Select, like AutoCAD's "Enter = repeat last command".
        if (e.Key == Key.Enter
            && MuPDFRenderer.ActiveTool == InlineAnnotationTool.Select
            && _lastUsedTool.HasValue
            && !CalibrationCanvas.IsVisible
            && !ColorInputCanvas.IsVisible)
        {
            ApplyToolSwitch(_lastUsedTool.Value);
            e.Handled = true;
            return;
        }

        // Space: hold to temporarily pan (Figma-style)
        if (e.Key == Key.Space && !_spaceHeld)
        {
            _spaceHeld = true;
            MuPDFRenderer.Cursor = CursorHand;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            MuPDFRenderer.Undo();
            MuPDFRenderer.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            MuPDFRenderer.Redo();
            MuPDFRenderer.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+C: copy all selected annotations, else copy page image
            if (_selectedAnnotations.Count > 1)
            { _annotationClipboard = _selectedAnnotations.ToList(); e.Handled = true; }
            else if (_selectedAnnotation != null)
            { _annotationClipboard = _selectedAnnotation; e.Handled = true; }
            else
            { OnAnnotateCopy(this, e); e.Handled = true; }
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_annotationClipboard != null && !MuPDFRenderer.IsActiveLayerLocked)
            {
                PasteAnnotation();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+D: duplicate selected annotations in place
            if (_selectedAnnotations.Count > 1 && !MuPDFRenderer.IsActiveLayerLocked)
            {
                _annotationClipboard = _selectedAnnotations.ToList();
                PasteAnnotation();
                e.Handled = true;
            }
            else if (_selectedAnnotation != null && !MuPDFRenderer.IsActiveLayerLocked)
            {
                _annotationClipboard = _selectedAnnotation;
                PasteAnnotation();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SelectAllOnPage();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_screenshotMode)
            {
                DeactivateScreenshotMode();
                e.Handled = true;
                return;
            }
            if (AnnotateShortcutsCanvas.IsVisible)
            {
                AnnotateShortcutsCanvas.IsVisible = false;
            }
            else if (_matchStyleArmed)
            {
                CancelMatchStyle();
            }
            else if (PropertyPanelCanvas.IsVisible)
            {
                ClosePropertyPanel();
            }
            else if (CalibrationCanvas.IsVisible)
            {
                OnCalibrationCancel(this, e);
            }
            else if (_arrowTextOrigin != null)
            {
                _arrowTextOrigin = null;
                MuPDFRenderer.ClearArrowTextPreview();
            }
            else if (MuPDFRenderer.HasActivePolyline)
            {
                MuPDFRenderer.CancelPolyline();
            }
            else if (MuPDFRenderer.HasActiveShape)
            {
                MuPDFRenderer.CancelStroke();
            }
            else if (MuPDFRenderer.HasActiveMeasurement)
            {
                MuPDFRenderer.CancelStroke();
            }
            else if (_rubberBandActive)
            {
                _rubberBandActive = false;
                _rubberBandCrossing = false;
                _inkDrawing = false;
                MuPDFRenderer.ClearRubberBand();
            }
            else if (_selectedAnnotations.Count > 0)
            {
                // First Escape: deselect, stay in annotation mode
                DeselectAnnotation();
            }
            else
            {
                DeactivateAnnotateMode();
            }
            e.Handled = true;
        }
        // Arrow keys: nudge the selected annotation (with undo coalescing)
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down
            && _selectedAnnotations.Count > 0 && !MuPDFRenderer.IsActiveLayerLocked)
        {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            var now = DateTime.UtcNow;
            bool coalesce = (now - _lastNudgeTime).TotalMilliseconds < 400;
            if (!coalesce)
            {
                var snaps = new List<object>(_selectedAnnotations.Count);
                foreach (var item in _selectedAnnotations)
                {
                    var snap = MuPDFRenderer.CapturePreDragSnapshot(item);
                    if (snap != null) snaps.Add(snap);
                }
                MuPDFRenderer.PushGroupMoveUndo(snaps);
            }
            _lastNudgeTime = now;
            foreach (var item in _selectedAnnotations)
                MoveAnnotation(item, dx, dy);
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
            e.Handled = true;
        }
        // Keyboard tool shortcuts (1-9, 0)
        else if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var toolKey = e.Key switch
            {
                Key.D1 => (InlineAnnotationTool?)InlineAnnotationTool.Draw,
                Key.D2 => InlineAnnotationTool.Highlight,
                Key.D3 => InlineAnnotationTool.Rectangle,
                Key.D4 => InlineAnnotationTool.Ellipse,
                Key.D5 => InlineAnnotationTool.Line,
                Key.D6 => InlineAnnotationTool.Arrow,
                Key.D7 => InlineAnnotationTool.Text,
                Key.D8 => InlineAnnotationTool.ArrowText,
                Key.D9 => InlineAnnotationTool.MeasureDistance,
                Key.D0 => InlineAnnotationTool.Eraser,
                Key.V => InlineAnnotationTool.Select,
                Key.P => InlineAnnotationTool.Polyline,
                Key.A => InlineAnnotationTool.MeasureArea,
                Key.D => InlineAnnotationTool.Dot,
                _ => null
            };
            if (toolKey.HasValue)
            {
                SwitchTool(toolKey.Value);
                e.Handled = true;
            }
            else if (e.Key == Key.OemOpenBrackets)
            {
                // [ = decrease stroke width (skip in highlight mode)
                if (!MuPDFRenderer.IsHighlighterMode)
                {
                    MuPDFRenderer.StrokeWidth = Math.Max(1, MuPDFRenderer.StrokeWidth - 1);
                    SyncWidthState();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.OemCloseBrackets)
            {
                // ] = increase stroke width (skip in highlight mode)
                if (!MuPDFRenderer.IsHighlighterMode)
                {
                    MuPDFRenderer.StrokeWidth = Math.Min(20, MuPDFRenderer.StrokeWidth + 1);
                    SyncWidthState();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.G)
            {
                // G = toggle snap-to-grid
                MuPDFRenderer.SnapToGrid = !MuPDFRenderer.SnapToGrid;
                SyncGridToggleButton(MuPDFRenderer.SnapToGrid);
                MuPDFRenderer.InvalidateVisual();
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus)
            {
                // - = decrease font size
                MuPDFRenderer.TextFontSize = Math.Max(6, MuPDFRenderer.TextFontSize - 2);
                UpdateFontSizeLabel();
                e.Handled = true;
            }
            else if (e.Key == Key.OemPlus)
            {
                // + = increase font size
                MuPDFRenderer.TextFontSize = Math.Min(72, MuPDFRenderer.TextFontSize + 2);
                UpdateFontSizeLabel();
                e.Handled = true;
            }
        }
    }

    private bool _spaceHeld;

    // ── Color palette (single source of truth) ──────────────────────────────
    private static readonly Dictionary<string, Color> ColorPalette = new()
    {
        ["Red"]    = Color.FromRgb(214, 64, 69),
        ["Blue"]   = Color.FromRgb(59, 130, 217),
        ["Green"]  = Color.FromRgb(61, 163, 95),
        ["Black"]  = Color.FromRgb(34, 34, 34),
        ["Orange"] = Color.FromRgb(232, 125, 47),
        ["Purple"] = Color.FromRgb(139, 92, 246),
        ["Yellow"] = Color.FromRgb(245, 195, 50),
        ["Teal"]   = Color.FromRgb(38, 166, 154),
        ["Pink"]   = Color.FromRgb(236, 64, 122),
        ["Brown"]  = Color.FromRgb(141, 110, 99),
    };
    private static readonly Dictionary<Color, string> ColorNames =
        ColorPalette.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Avalonia.Input.Cursor CursorCross = new(Avalonia.Input.StandardCursorType.Cross);
    private static readonly Avalonia.Input.Cursor CursorNo = new(Avalonia.Input.StandardCursorType.No);
    private static readonly Avalonia.Input.Cursor CursorIbeam = new(Avalonia.Input.StandardCursorType.Ibeam);
    private static readonly Avalonia.Input.Cursor CursorSizeAll = new(Avalonia.Input.StandardCursorType.SizeAll);
    private static readonly Avalonia.Input.Cursor CursorArrow = new(Avalonia.Input.StandardCursorType.Arrow);
    private static readonly Avalonia.Input.Cursor CursorHand = new(Avalonia.Input.StandardCursorType.Hand);
    private static readonly Avalonia.Input.Cursor CursorSizeNWSE = new(Avalonia.Input.StandardCursorType.BottomRightCorner);
    private static readonly Avalonia.Input.Cursor CursorSizeNESW = new(Avalonia.Input.StandardCursorType.BottomLeftCorner);

    private void OnAnnotateShortcutsBackgroundClick(object? sender, PointerPressedEventArgs e)
    {
        if (!AnnotateShortcutsCanvas.IsVisible) return;
        AnnotateShortcutsCanvas.IsVisible = false;
        e.Handled = true;
    }

    private static Avalonia.Input.Cursor GetToolCursor(InlineAnnotationTool tool) => tool switch
    {
        InlineAnnotationTool.Eraser => CursorNo,
        InlineAnnotationTool.Text => CursorIbeam,
        InlineAnnotationTool.StickyNote => CursorIbeam,
        InlineAnnotationTool.Select => CursorArrow,
        InlineAnnotationTool.Polyline => CursorCross,
        InlineAnnotationTool.Screenshot => CursorCross,
        _ => CursorCross
    };

    /// <summary>
    /// Returns the appropriate cursor for hovering in Select mode, based on
    /// whether the cursor is over a vertex handle, resize corner, text resize
    /// handle, annotation body (move), or empty space.
    /// </summary>
    private Avalonia.Input.Cursor GetHoverCursor(Point pdfPoint, object? hoverHit)
    {
        // 1. Check group resize corners (combined bounding box) first
        if (_selectedAnnotations.Count >= 2)
        {
            var combined = AnnotatedPDFRenderer.GetCombinedBounds(_selectedAnnotations);
            if (combined is { Width: > 0 } or { Height: > 0 })
            {
                double hitR = HitRadius(ResizeHandleHitScreenPx);
                if (IsNear(pdfPoint, combined.TopLeft, hitR) || IsNear(pdfPoint, combined.BottomRight, hitR))
                    return CursorSizeNWSE;
                if (IsNear(pdfPoint, combined.TopRight, hitR) || IsNear(pdfPoint, combined.BottomLeft, hitR))
                    return CursorSizeNESW;
            }
        }

        // 2. Check vertex handles of the current selection
        if (_selectedAnnotation != null)
        {
            double vr = HitRadius(HandleHitScreenPxLarge);
            switch (_selectedAnnotation)
            {
                case ShapeAnnotation shape:
                    if (IsNear(pdfPoint, shape.Start, vr) || IsNear(pdfPoint, shape.End, vr))
                        return CursorCross;
                    break;
                case MeasurementAnnotation meas when meas.Points.Count >= 2:
                    if (IsNear(pdfPoint, meas.Points[0], vr) || IsNear(pdfPoint, meas.Points[1], vr))
                        return CursorCross;
                    break;
                case InkStroke { IsPolyline: true } poly:
                    foreach (var p in poly.Points)
                        if (IsNear(pdfPoint, p, vr))
                            return CursorCross;
                    break;
            }

            // Text right-edge resize handle
            if (_selectedAnnotation is TextAnnotation { IsStickyNote: false, MaxWidth: > 0 } selText)
            {
                var rtb = AnnotatedPDFRenderer.GetTextBounds(selText);
                var handlePoint = new Point(rtb.Right, (rtb.Top + rtb.Bottom) / 2);
                if (IsNear(pdfPoint, handlePoint, HitRadius(ResizeHandleHitScreenPx)))
                    return CursorSizeWE;
            }
        }

        // 3. Arrow origin handle
        if (hoverHit is TextAnnotation { ArrowOrigin: { } ao }
            && IsNear(pdfPoint, ao, HitRadius(ArrowTipHitScreenPx)))
            return CursorCross;

        // 4. Annotation body — move
        if (hoverHit != null)
            return CursorSizeAll;

        // 5. Empty space — default arrow
        return CursorArrow;
    }

    /// <summary>
    /// Core tool-switching logic shared by button clicks and keyboard shortcuts.
    /// </summary>
    private void ApplyToolSwitch(InlineAnnotationTool tool)
    {
        var previousTool = MuPDFRenderer.ActiveTool;

        // Track the last non-Select tool so Enter can re-activate it.
        if (tool != InlineAnnotationTool.Select)
            _lastUsedTool = tool;

        // Reset drawing/drag state to prevent stale flags from blocking new tools
        ResetDragState();

        if (previousTool is InlineAnnotationTool.MeasureDistance && tool != previousTool)
        {
            MuPDFRenderer.CancelStroke();
        }
        if (_arrowTextOrigin != null && tool != InlineAnnotationTool.ArrowText)
        {
            _arrowTextOrigin = null;
            MuPDFRenderer.ClearArrowTextPreview();
        }
        if (MuPDFRenderer.HasActivePolyline && tool != InlineAnnotationTool.Polyline)
        {
            MuPDFRenderer.CancelPolyline();
        }
        if (MuPDFRenderer.HasActiveShape)
        {
            MuPDFRenderer.CancelStroke();
        }
        if (MuPDFRenderer.HasActiveMeasurement)
        {
            MuPDFRenderer.CancelStroke();
        }
        if (tool != InlineAnnotationTool.Select && _selectedAnnotations.Count > 0)
            DeselectAnnotation();
        if (tool != InlineAnnotationTool.Select && PropertyPanelCanvas.IsVisible)
            ClosePropertyPanel();
        if (_matchStyleArmed)
            CancelMatchStyle(restoreCursor: false);

        MuPDFRenderer.ActiveTool = tool;
        MuPDFRenderer.ClearSnapGuides();
        MuPDFRenderer.ClearEraserHover();
        MuPDFRenderer.ClearStickyNoteHover();
        MuPDFRenderer.UpdateCursorPreview(null);
        MuPDFRenderer.ClearTextPlacementPreview();

        if (tool == InlineAnnotationTool.Highlight)
        {
            _normalStrokeWidth = MuPDFRenderer.StrokeWidth;
            MuPDFRenderer.StrokeWidth = 12;
            MuPDFRenderer.StrokeOpacity = 0.35;
            MuPDFRenderer.IsHighlighterMode = true;
        }
        else
        {
            if (previousTool == InlineAnnotationTool.Highlight || MuPDFRenderer.IsHighlighterMode)
            {
                MuPDFRenderer.StrokeOpacity = 1.0;
                MuPDFRenderer.StrokeWidth = _normalStrokeWidth;
            }
            MuPDFRenderer.IsHighlighterMode = false;
        }

        MuPDFRenderer.Cursor = GetToolCursor(tool);
        SetActiveToolButton(FindToolbarButtonByTag(tool.ToString()));
        UpdateCornerRadiusVisibility();
        UpdateFontSizeVisibility();
        UpdateAnnotationStatusHint();
    }

    /// <summary>Switch tool programmatically (from keyboard shortcut).</summary>
    private void SwitchTool(InlineAnnotationTool tool) => ApplyToolSwitch(tool);

    // ── Pointer input handlers are in PreView.AnnotationInput.cs ──

    // ── Toolbar & UI control handlers are in PreView.AnnotationControls.cs ──

    #endregion
}
