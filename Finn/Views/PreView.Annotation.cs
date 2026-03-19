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

    // ── Hit-test thresholds (squared radii for distance checks) ──
    private const double ArrowTipHitRadius = 8;
    private const double HandleHitRadius = 10;
    private const double HandleHitRadiusLarge = 12;
    private const double ResizeHandleHitRadius = 14;

    private static double DistanceSq(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static bool IsNear(Point a, Point b, double radius)
        => DistanceSq(a, b) <= radius * radius;

    private static readonly Avalonia.Input.Cursor CursorSizeWE = new(Avalonia.Input.StandardCursorType.SizeWestEast);

    private bool _annotateMode;
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
    /// <summary>PDF-space start point of the rubber-band rectangle.</summary>
    private Point _rubberBandStartPdf;

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
        _preDragSnapshot = null;
        _multiDragSnapshots = null;
        MuPDFRenderer.ClearRubberBand();
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
            foreach (var snap in _multiDragSnapshots) MuPDFRenderer.PushMoveUndo(snap);
            _multiDragSnapshots = null;
        }
        _draggingVertexItem = null;
        _draggingArrowOrigin = null;
        _draggingTextAnnotation = null;
        _resizingTextAnnotation = null;
        _selectDragItem = null;
        MuPDFRenderer.ClearSnapGuides();
        MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
        MuPDFRenderer.NotifyAnnotationChanged();
    }

    /// <summary>Resets text input overlay state and restores focus.</summary>
    private void CloseTextInput()
    {
        TextInputCanvas.IsVisible = false;
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = null;
        _pendingStickyNote = false;
        _arrowTextOrigin = null;
        MuPDFRenderer.ClearArrowTextPreview();
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
            case ShapeAnnotation shape:
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
    }

    private void SelectAnnotation(object item)
    {
        SavePreSelectState();
        _selectedAnnotation = item;
        _selectedAnnotations.Clear();
        _selectedAnnotations.Add(item);
        MuPDFRenderer.SetSelectHighlight(item);
        SyncToolbarToSelection();
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
        MuPDFRenderer.Focus();
    }

    /// <summary>Paste a copy of the clipboard annotation with a small offset.</summary>
    private void PasteAnnotation()
    {
        if (_annotationClipboard == null) return;
        const double offset = 15;
        switch (_annotationClipboard)
        {
            case TextAnnotation src:
            {
                var copy = new TextAnnotation
                {
                    Position = new Point(src.Position.X + offset, src.Position.Y + offset),
                    Text = src.Text, FontSize = src.FontSize, Color = src.Color,
                    Opacity = src.Opacity, FontFamily = src.FontFamily,
                    IsStickyNote = src.IsStickyNote,
                    MaxWidth = src.MaxWidth,
                    ArrowOrigin = src.ArrowOrigin.HasValue
                        ? new Point(src.ArrowOrigin.Value.X + offset, src.ArrowOrigin.Value.Y + offset)
                        : null
                };
                if (copy.IsStickyNote)
                    MuPDFRenderer.PlaceStickyNote(copy.Position, copy.Text);
                else if (copy.ArrowOrigin.HasValue)
                    MuPDFRenderer.PlaceArrowText(copy.ArrowOrigin.Value, copy.Position, copy.Text);
                else
                    MuPDFRenderer.PlaceText(copy.Position, copy.Text);
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
                    DashPattern = src.DashPattern
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
                    DashPattern = src.DashPattern
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
        MuPDFRenderer.ActiveTool = InlineAnnotationTool.Select;

        MuPDFRenderer.EnsureDefaultLayer();
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
    }

    private void DeactivateAnnotateMode()
    {
        if (!_annotateMode) return;
        _annotateMode = false;
        pwr.AnnotationActive = false;
        AnnotateToggle.IsChecked = false;
        AnnotateToolbar.IsVisible = false;
        TextInputCanvas.IsVisible = false;
        CalibrationCanvas.IsVisible = false;
        ColorInputCanvas.IsVisible = false;
        ResetDragState();
        _selectedAnnotation = null;
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
    }

    private void OnAnnotateKeyDown(object? sender, KeyEventArgs e)
    {
        // Safety: handler should only be registered during annotation mode.
        // Guard against mismatched register/remove leaving this active.
        if (!_annotateMode) return;

        // While the text input is open, only intercept Escape so the user
        // can still type Shift+digits (!, @, …), capital letters, brackets, etc.
        if (TextInputCanvas.IsVisible)
        {
            if (e.Key == Key.Escape)
            {
                OnTextInputCancel(this, e);
                e.Handled = true;
            }
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
            // Ctrl+C: copy selected annotation, else copy page image
            if (_selectedAnnotation != null)
            { _annotationClipboard = _selectedAnnotation; e.Handled = true; }
            else
            { OnAnnotateCopy(this, e); e.Handled = true; }
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_annotationClipboard != null)
            {
                PasteAnnotation();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+D: duplicate selected annotation in place
            if (_selectedAnnotation != null)
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
        else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnAnnotateSave(this, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (CalibrationCanvas.IsVisible)
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
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && _selectedAnnotations.Count > 0)
        {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            var now = DateTime.UtcNow;
            bool coalesce = (now - _lastNudgeTime).TotalMilliseconds < 400;
            if (!coalesce)
            {
                foreach (var item in _selectedAnnotations)
                {
                    var snap = MuPDFRenderer.CapturePreDragSnapshot(item);
                    if (snap != null) MuPDFRenderer.PushMoveUndo(snap);
                }
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

    private static Avalonia.Input.Cursor GetToolCursor(InlineAnnotationTool tool) => tool switch
    {
        InlineAnnotationTool.Eraser => CursorNo,
        InlineAnnotationTool.Text => CursorIbeam,
        InlineAnnotationTool.StickyNote => CursorIbeam,
        InlineAnnotationTool.Select => CursorArrow,
        InlineAnnotationTool.Polyline => CursorCross,
        _ => CursorCross
    };

    /// <summary>
    /// Core tool-switching logic shared by button clicks and keyboard shortcuts.
    /// </summary>
    private void ApplyToolSwitch(InlineAnnotationTool tool)
    {
        var previousTool = MuPDFRenderer.ActiveTool;

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

        MuPDFRenderer.ActiveTool = tool;
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
        UpdateActiveToolLabel(tool);
    }

    /// <summary>Switch tool programmatically (from keyboard shortcut).</summary>
    private void SwitchTool(InlineAnnotationTool tool) => ApplyToolSwitch(tool);

    private void OnInkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_annotateMode) return;
        var point = e.GetCurrentPoint(MuPDFRenderer);

        // Space+left-click: pan (Figma-style)
        if (_spaceHeld && point.Properties.IsLeftButtonPressed)
        {
            BeginPan(MuPDFRenderer, e);
            return;
        }

        // Middle-mouse: pan even in annotation mode
        if (point.Properties.IsMiddleButtonPressed)
        {
            BeginPan(MuPDFRenderer, e);
            MuPDFRenderer.Cursor = CursorHand;
            return;
        }

        // Right-click: finish/cancel active operations
        if (point.Properties.IsRightButtonPressed)
        {
            if (MuPDFRenderer.HasActivePolyline)
            {
                MuPDFRenderer.EndPolyline();
                e.Handled = true;
                return;
            }
            if (MuPDFRenderer.HasActiveShape)
            {
                MuPDFRenderer.CancelStroke();
                e.Handled = true;
                return;
            }
            if (MuPDFRenderer.HasActiveMeasurement)
            {
                MuPDFRenderer.CancelStroke();
                e.Handled = true;
                return;
            }
            if (TextInputCanvas.IsVisible)
            {
                OnTextInputCommit(this, e);
                e.Handled = true;
                return;
            }
            if (_arrowTextOrigin != null)
            {
                _arrowTextOrigin = null;
                MuPDFRenderer.ClearArrowTextPreview();
                _inkDrawing = false;
                e.Handled = true;
                return;
            }
            if (_inkDrawing)
            {
                MuPDFRenderer.CancelStroke();
                _inkDrawing = false;
                e.Handled = true;
                return;
            }
            // Right-click with nothing active: show context menu or switch to Select
            var rightPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
            object? rightHit = rightPdf.HasValue ? MuPDFRenderer.FindTopmostAt(rightPdf.Value) : null;
            if (rightHit != null)
            {
                // Right-clicked an annotation: select it and show context menu
                SelectAnnotation(rightHit);
                ShowAnnotationContextMenu(e.GetPosition(MuPDFRenderer));
                e.Handled = true;
                return;
            }
            if (_selectedAnnotation != null)
            {
                DeselectAnnotation();
            }
            if (MuPDFRenderer.ActiveTool != InlineAnnotationTool.Select)
            {
                ApplyToolSwitch(InlineAnnotationTool.Select);
            }
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        var pdfPoint = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        if (!pdfPoint.HasValue) return;

        e.Pointer.Capture(MuPDFRenderer);
        e.Handled = true;

        var tool = MuPDFRenderer.ActiveTool;

        // ── Two-click shapes: second click commits ──
        if (MuPDFRenderer.HasActiveShape)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            MuPDFRenderer.UpdateShape(pdfPoint.Value, shift);
            MuPDFRenderer.EndShape();
            e.Pointer.Capture(null);
            return;
        }

        // ── Two-click measurement: second click commits ──
        if (MuPDFRenderer.HasActiveMeasurement)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            MuPDFRenderer.UpdateMeasurementPreview(pdfPoint.Value, shift);
            MuPDFRenderer.EndMeasurement();
            e.Pointer.Capture(null);
            // Guided calibration: after drawing the reference measurement, show calibration input
            if (_calibrationMode)
            {
                _calibrationMode = false;
                CalibrationPromptText.Text = "Enter the real-world distance for the line you just drew:";
                CalibrationValueBox.Text = "";
                CenterCalibrationDialog();
                CalibrationCanvas.IsVisible = true;
                CalibrationValueBox.Focus();
            }
            return;
        }

        // ── Hover-grab: Select and text-editing tools can grab/edit existing annotations ──
        // Drawing tools (shapes, measure, polyline, freehand) always draw — vertex
        // snapping handles alignment. Switch to Select (V) to move things.
        if (tool is InlineAnnotationTool.Select
            or InlineAnnotationTool.Text
            or InlineAnnotationTool.ArrowText
            or InlineAnnotationTool.StickyNote)
        {
            // Check for existing annotations under cursor
            object? hitItem = null;
            if (tool is InlineAnnotationTool.StickyNote)
                hitItem = MuPDFRenderer.FindStickyNoteAt(pdfPoint.Value);
            else if (tool is InlineAnnotationTool.Text or InlineAnnotationTool.ArrowText)
                hitItem = MuPDFRenderer.FindTextAt(pdfPoint.Value);
            else
                hitItem = MuPDFRenderer.FindTopmostAt(pdfPoint.Value);

            if (hitItem is TextAnnotation hitText)
            {
                // Shift+Click: toggle multi-selection
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ToggleAnnotationSelection(hitText);
                    e.Pointer.Capture(null);
                    return;
                }
                if (e.ClickCount >= 2)
                {
                    ShowTextEdit(hitText, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                    return;
                }
                // Check if the click is specifically on the arrow origin (tip)
                if (hitText.ArrowOrigin.HasValue && IsNear(pdfPoint.Value, hitText.ArrowOrigin.Value, ArrowTipHitRadius))
                {
                    _draggingArrowOrigin = hitText;
                    _dragStartPdf = pdfPoint.Value;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(hitText);
                    _inkDrawing = true;
                    SelectAnnotation(hitText);
                    return;
                }
                // Right-edge resize handle: check before generic drag
                if (!hitText.IsStickyNote && hitText.MaxWidth > 0)
                {
                    var rtb = AnnotatedPDFRenderer.GetTextBounds(hitText);
                    var handlePoint = new Point(rtb.Right, (rtb.Top + rtb.Bottom) / 2);
                    if (IsNear(pdfPoint.Value, handlePoint, HandleHitRadiusLarge))
                    {
                        _resizingTextAnnotation = hitText;
                        _dragStartPdf = pdfPoint.Value;
                        _preDragSnapshot = MuPDFRenderer.CapturePropertySnapshot(hitText);
                        _inkDrawing = true;
                        MuPDFRenderer.Cursor = CursorSizeWE;
                        SelectAnnotation(hitText);
                        return;
                    }
                }
                // Single click: select + start drag
                _draggingTextAnnotation = hitText;
                _dragStartPdf = pdfPoint.Value;
                _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(hitText);
                _inkDrawing = true;
                MuPDFRenderer.Cursor = CursorSizeAll;
                SelectAnnotation(hitText);
                return;
            }
            if (hitItem != null)
            {
                // Check if it's an ArrowText and the click is on the arrow tip
                if (hitItem is TextAnnotation arrowHit && arrowHit.ArrowOrigin.HasValue
                    && IsNear(pdfPoint.Value, arrowHit.ArrowOrigin.Value, ArrowTipHitRadius))
                {
                    _draggingArrowOrigin = arrowHit;
                    _dragStartPdf = pdfPoint.Value;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(arrowHit);
                    _inkDrawing = true;
                    SelectAnnotation(arrowHit);
                    return;
                }
                // Vertex drag: check if click is near any vertex of the hit annotation
                if (TryBeginVertexDrag(hitItem, pdfPoint.Value, HandleHitRadius))
                {
                    MuPDFRenderer.Cursor = CursorCross;
                    SelectAnnotation(hitItem);
                    return;
                }
                // Non-text annotation: select + start whole-drag
                // Shift+Click: toggle multi-selection
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ToggleAnnotationSelection(hitItem);
                    e.Pointer.Capture(null);
                    return;
                }
                // If item is already in multi-selection, drag all; otherwise replace selection
                if (!_selectedAnnotations.Contains(hitItem))
                    SelectAnnotation(hitItem);
                else
                    _selectedAnnotation = hitItem;
                _selectDragItem = hitItem;
                _dragStartPdf = pdfPoint.Value;
                _multiDragSnapshots = new List<object>();
                foreach (var sel in _selectedAnnotations)
                {
                    var snap = MuPDFRenderer.CapturePreDragSnapshot(sel);
                    if (snap != null) _multiDragSnapshots.Add(snap);
                }
                _inkDrawing = true;
                MuPDFRenderer.Cursor = CursorSizeAll;
                return;
            }
            // No annotation body hit — but check if we clicked a vertex of the currently selected annotation
            // (needed for ellipses where Start/End are at bounding-box corners, outside the ellipse boundary,
            //  and for measurements/polylines whose endpoints may extend past the hit-test region)
            if (hitItem == null && _selectedAnnotation != null)
            {
                if (TryBeginVertexDrag(_selectedAnnotation, pdfPoint.Value, HandleHitRadiusLarge))
                    return;

                // Text right-edge resize handle (outside text body but near the handle dot)
                if (_selectedAnnotation is TextAnnotation { IsStickyNote: false, MaxWidth: > 0 } selText)
                {
                    var rtb = AnnotatedPDFRenderer.GetTextBounds(selText);
                    var handlePoint = new Point(rtb.Right, (rtb.Top + rtb.Bottom) / 2);
                    if (IsNear(pdfPoint.Value, handlePoint, ResizeHandleHitRadius))
                    {
                        _resizingTextAnnotation = selText;
                        _dragStartPdf = pdfPoint.Value;
                        _preDragSnapshot = MuPDFRenderer.CapturePropertySnapshot(selText);
                        _inkDrawing = true;
                        MuPDFRenderer.Cursor = CursorSizeWE;
                        return;
                    }
                }
            }
            // Clicked empty space: deselect any current selection
            if (_selectedAnnotations.Count > 0)
            {
                DeselectAnnotation();
            }
        }
        // Any other tool: just clear the existing selection before drawing
        else if (_selectedAnnotations.Count > 0)
        {
            DeselectAnnotation();
        }

        // ── Tool-specific first-click actions ──
        MuPDFRenderer.ClearTextPlacementPreview();
        switch (tool)
        {
            case InlineAnnotationTool.Eraser:
                MuPDFRenderer.EraseAt(pdfPoint.Value);
                _inkDrawing = true;
                break;

            case InlineAnnotationTool.Text:
                ShowTextInput(pdfPoint.Value, e.GetPosition(MuPDFRenderer));
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.StickyNote:
                ShowTextInput(pdfPoint.Value, e.GetPosition(MuPDFRenderer), isStickyNote: true);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.ArrowText:
                if (_arrowTextOrigin != null)
                {
                    // Second click: freeze arrow at endpoint and show text input
                    MuPDFRenderer.UpdateArrowTextPreview(pdfPoint.Value);
                    ShowTextInput(pdfPoint.Value, e.GetPosition(MuPDFRenderer), _arrowTextOrigin.Value);
                    e.Pointer.Capture(null);
                }
                else
                {
                    // First click: set arrow origin
                    _arrowTextOrigin = pdfPoint.Value;
                    MuPDFRenderer.SetArrowTextPreview(pdfPoint.Value);
                    e.Pointer.Capture(null);
                }
                break;

            case InlineAnnotationTool.MeasureDistance:
                MuPDFRenderer.BeginMeasurement(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.Select:
                // Empty space — start rubber-band marquee selection
                _rubberBandActive = true;
                _rubberBandStartPdf = pdfPoint.Value;
                _inkDrawing = true;
                MuPDFRenderer.SetRubberBand(pdfPoint.Value, pdfPoint.Value);
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                // First click: begin shape (no drag needed — preview follows cursor)
                MuPDFRenderer.BeginShape(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.Polyline:
            {
                bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (MuPDFRenderer.HasActivePolyline)
                {
                    if (e.ClickCount >= 2)
                        MuPDFRenderer.EndPolyline();  // double-click finishes
                    else
                        MuPDFRenderer.AddPolylinePoint(pdfPoint.Value, shift);
                }
                else
                    MuPDFRenderer.BeginPolyline(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;
            }

            default: // Draw, Highlight
                _inkDrawing = true;
                MuPDFRenderer.BeginStroke(pdfPoint.Value);
                break;
        }
    }

    private void OnInkPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_annotateMode) return;

        if (_middlePanning)
        {
            UpdatePan(e);
            return;
        }

        if (!_inkDrawing)
        {
            var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));

            // Two-click shapes: live preview follows cursor without drag
            if (_annotateMode && MuPDFRenderer.HasActiveShape)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateShape(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return;
            }

            // Two-click measurement: live preview follows cursor without drag
            if (_annotateMode && MuPDFRenderer.HasActiveMeasurement)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateMeasurementPreview(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return;
            }

            // Polyline preview: always track cursor when a polyline is active
            if (_annotateMode && MuPDFRenderer.HasActivePolyline)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdatePolylinePreview(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
            // ArrowText preview: track cursor after first click sets origin (freeze while typing)
            else if (_annotateMode && _arrowTextOrigin != null && !TextInputCanvas.IsVisible)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateArrowTextPreview(hoverPdf.Value);
            }
            // Eraser hover highlight: track what's under the cursor
            else if (_annotateMode && MuPDFRenderer.ActiveTool == InlineAnnotationTool.Eraser)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateEraserHover(hoverPdf.Value);
                else
                    MuPDFRenderer.ClearEraserHover();
            }
            // Sticky note hover popup: show when cursor is over any note icon
            else if (_annotateMode)
            {
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateStickyNoteHover(hoverPdf.Value);
                else
                    MuPDFRenderer.ClearStickyNoteHover();

                // Text/Sticky/ArrowText: show ghost at cursor for placement preview
                var at = MuPDFRenderer.ActiveTool;
                if (at is InlineAnnotationTool.Text or InlineAnnotationTool.StickyNote or InlineAnnotationTool.ArrowText)
                {
                    if (hoverPdf.HasValue)
                        MuPDFRenderer.UpdateTextPlacementPreview(at, hoverPdf.Value);
                    else
                        MuPDFRenderer.ClearTextPlacementPreview();
                }
                else
                    MuPDFRenderer.ClearTextPlacementPreview();

                // Hover outline + cursor: show only for tools that can grab annotations
                if (hoverPdf.HasValue
                    && at is InlineAnnotationTool.Select
                        or InlineAnnotationTool.Text
                        or InlineAnnotationTool.ArrowText
                        or InlineAnnotationTool.StickyNote)
                {
                    var hoverHit = MuPDFRenderer.FindTopmostAt(hoverPdf.Value);
                    MuPDFRenderer.UpdateSelectHover(hoverHit);
                    if (at is InlineAnnotationTool.Select)
                        MuPDFRenderer.Cursor = hoverHit != null ? CursorSizeAll : CursorArrow;
                }
                else
                {
                    MuPDFRenderer.UpdateSelectHover(null);
                }
            }
            return;
        }
        e.Handled = true;

        var pdfPoint = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        if (!pdfPoint.HasValue) return;

        // Swipe-to-erase: continuously erase while dragging with eraser tool
        if (MuPDFRenderer.ActiveTool == InlineAnnotationTool.Eraser)
        {
            MuPDFRenderer.EraseAt(pdfPoint.Value);
            return;
        }

        // Handle vertex dragging — moves a single Start/End or Points[n]
        if (_draggingVertexItem != null)
        {
            bool constrain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            switch (_draggingVertexItem)
            {
                case ShapeAnnotation sv:
                {
                    var anchor = _draggingVertexIndex == 0 ? sv.End : sv.Start;
                    var target = pdfPoint.Value;
                    if (constrain)
                    {
                        target = sv.ShapeType is InlineAnnotationTool.Rectangle
                                     or InlineAnnotationTool.Ellipse
                                     or InlineAnnotationTool.RevisionCloud
                            ? AnnotatedPDFRenderer.ConstrainToSquare(anchor, target)
                            : AnnotatedPDFRenderer.ConstrainToAxis(anchor, target);
                    }
                    else
                    {
                        target = MuPDFRenderer.ComputeVertexSnap(sv, target);
                    }
                    if (_draggingVertexIndex == 0) sv.Start = target;
                    else sv.End = target;
                    sv.InvalidatePen();
                    break;
                }
                case MeasurementAnnotation mv:
                {
                    var target = pdfPoint.Value;
                    if (constrain)
                    {
                        var anchor = mv.Points[_draggingVertexIndex == 0 ? 1 : 0];
                        target = AnnotatedPDFRenderer.ConstrainToAxis(anchor, target);
                    }
                    else
                    {
                        target = MuPDFRenderer.ComputeVertexSnap(mv, target);
                    }
                    mv.Points[_draggingVertexIndex] = target;
                    break;
                }
                case InkStroke pv when pv.IsPolyline:
                {
                    var target = pdfPoint.Value;
                    if (constrain && pv.Points.Count >= 2)
                    {
                        // Snap relative to the adjacent vertex
                        int adjIdx = _draggingVertexIndex > 0 ? _draggingVertexIndex - 1
                                                               : (_draggingVertexIndex < pv.Points.Count - 1 ? _draggingVertexIndex + 1 : -1);
                        if (adjIdx >= 0)
                            target = AnnotatedPDFRenderer.ConstrainToAxis(pv.Points[adjIdx], target);
                    }
                    pv.Points[_draggingVertexIndex] = target;
                    pv.InvalidatePen();
                    break;
                }
            }
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Handle text width resize — adjusts MaxWidth via right-edge drag
        if (_resizingTextAnnotation != null)
        {
            var snapped = MuPDFRenderer.ComputeVertexSnap(_resizingTextAnnotation,
                new Point(pdfPoint.Value.X, _resizingTextAnnotation.Position.Y));
            double newWidth = Math.Max(30, snapped.X - _resizingTextAnnotation.Position.X);
            _resizingTextAnnotation.MaxWidth = newWidth;
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Handle arrow origin (tip) dragging — moves only ArrowOrigin
        if (_draggingArrowOrigin != null)
        {
            _draggingArrowOrigin.ArrowOrigin = pdfPoint.Value;
            _dragStartPdf = pdfPoint.Value;
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Handle text annotation dragging (with snap-to-alignment)
        // For ArrowText, only the text box moves — the arrow origin stays pinned.
        if (_draggingTextAnnotation != null)
        {
            double rawDx = pdfPoint.Value.X - _dragStartPdf.X;
            double rawDy = pdfPoint.Value.Y - _dragStartPdf.Y;
            var (dx, dy) = MuPDFRenderer.ComputeSnapDelta(_draggingTextAnnotation, rawDx, rawDy);
            _draggingTextAnnotation.Position = new Point(
                _draggingTextAnnotation.Position.X + dx,
                _draggingTextAnnotation.Position.Y + dy);
            _dragStartPdf = new Point(_dragStartPdf.X + dx, _dragStartPdf.Y + dy);
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Select tool: drag any annotation type (with snap-to-alignment)
        if (_selectDragItem != null)
        {
            double rawDx = pdfPoint.Value.X - _dragStartPdf.X;
            double rawDy = pdfPoint.Value.Y - _dragStartPdf.Y;
            var (dx, dy) = MuPDFRenderer.ComputeSnapDelta(_selectDragItem, rawDx, rawDy);
            foreach (var item in _selectedAnnotations)
                MoveAnnotation(item, dx, dy);
            _dragStartPdf = new Point(_dragStartPdf.X + dx, _dragStartPdf.Y + dy);
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Rubber-band marquee: update rectangle while dragging
        if (_rubberBandActive)
        {
            MuPDFRenderer.SetRubberBand(_rubberBandStartPdf, pdfPoint.Value);
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var tool = MuPDFRenderer.ActiveTool;

        // Update pen cursor preview for freehand tools
        if (tool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight)
            MuPDFRenderer.UpdateCursorPreview(pdfPoint.Value);

        switch (tool)
        {
            case InlineAnnotationTool.MeasureDistance:
                MuPDFRenderer.UpdateMeasurementPreview(pdfPoint.Value, shift);
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                MuPDFRenderer.UpdateShape(pdfPoint.Value, shift);
                break;

            default: // Draw, Highlight
                MuPDFRenderer.AddPoint(pdfPoint.Value);
                break;
        }
    }

    private void OnInkPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_annotateMode) return;

        if (_middlePanning)
        {
            _middlePanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            MuPDFRenderer.Cursor = _spaceHeld ? CursorHand : GetToolCursor(MuPDFRenderer.ActiveTool);
            return;
        }

        if (!_inkDrawing) return;
        _inkDrawing = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // Handle any active drag release — push undo, clear state, restore cursor
        if (_draggingVertexItem != null || _draggingArrowOrigin != null
            || _draggingTextAnnotation != null || _selectDragItem != null)
        {
            FinishDrag();
            return;
        }
        if (_resizingTextAnnotation != null)
        {
            FinishDrag(isPropertyUndo: true);
            return;
        }

        // Handle rubber-band marquee release — select all items in the rectangle
        if (_rubberBandActive)
        {
            _rubberBandActive = false;
            var endPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
            MuPDFRenderer.ClearRubberBand();
            if (endPdf.HasValue)
            {
                double x = Math.Min(_rubberBandStartPdf.X, endPdf.Value.X);
                double y = Math.Min(_rubberBandStartPdf.Y, endPdf.Value.Y);
                double w = Math.Abs(endPdf.Value.X - _rubberBandStartPdf.X);
                double h = Math.Abs(endPdf.Value.Y - _rubberBandStartPdf.Y);
                if (w > 2 || h > 2) // ignore tiny accidental drags
                {
                    var rect = new Rect(x, y, w, h);
                    var found = MuPDFRenderer.FindAnnotationsInRect(rect);
                    if (found.Count > 0)
                    {
                        SavePreSelectState();
                        _selectedAnnotations.Clear();
                        MuPDFRenderer.ClearSelectHighlight();
                        foreach (var item in found)
                        {
                            _selectedAnnotations.Add(item);
                            MuPDFRenderer.AddSelectHighlight(item);
                        }
                        _selectedAnnotation = found[0];
                        SyncToolbarToSelection();
                    }
                }
            }
            MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
            return;
        }

        var tool = MuPDFRenderer.ActiveTool;
        switch (tool)
        {
            case InlineAnnotationTool.MeasureDistance:
                // Measurement uses two-click; EndMeasurement is called on second click in OnInkPointerPressed
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                // Shapes use two-click; EndShape is called on second click in OnInkPointerPressed
                break;

            default: // Draw, Highlight
                MuPDFRenderer.EndStroke();
                MuPDFRenderer.UpdateCursorPreview(null);
                break;
        }
    }

    private void OnAnnotateColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            var color = ColorPalette.GetValueOrDefault(colorName, ColorPalette["Red"]);
            MuPDFRenderer.StrokeColor = color;

            if (MuPDFRenderer.ActiveLayer != null)
                MuPDFRenderer.ActiveLayer.Color = color;

            // Apply to currently selected annotation
            ApplyColorToSelection(color);

            SetActiveColorButton(btn);
            MuPDFRenderer.Focus();
        }
    }

    private void ApplyColorToSelection(Color color)
    {
        if (_selectedAnnotations.Count == 0) return;
        foreach (var selItem in _selectedAnnotations)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(selItem);
            switch (selItem)
            {
                case InkStroke s: s.Color = color; s.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.Color = color; sh.InvalidatePen(); break;
                case TextAnnotation t: t.Color = color; break;
                case MeasurementAnnotation m: m.Color = color; break;
            }
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
        }
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
    }

    private void OnAnnotateToolSelect(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName)
        {
            var tool = Enum.Parse<InlineAnnotationTool>(toolName);
            ApplyToolSwitch(tool);
        }
        MuPDFRenderer.Focus();
    }

    private void OnAnnotateWidth(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widthStr && double.TryParse(widthStr, out double w))
        {
            if (MuPDFRenderer.IsHighlighterMode) return;
            MuPDFRenderer.StrokeWidth = w;
            _normalStrokeWidth = w;

            // Apply to currently selected annotations
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is InkStroke ink)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(ink);
                    ink.Width = w; ink.InvalidatePen();
                    if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
                    sh.StrokeWidth = w; sh.InvalidatePen();
                    if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
                }
            }
            if (_selectedAnnotations.Count > 0) { MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged(); }

            SetActiveWidthButton(btn);
        }
    }

    private void OnAnnotateDashPattern(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string patternName
            && Enum.TryParse<LineDashPattern>(patternName, out var pattern))
        {
            MuPDFRenderer.StrokeDashPattern = pattern;

            // Apply to currently selected annotations
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is InkStroke ink)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(ink);
                    ink.DashPattern = pattern; ink.InvalidatePen();
                    if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
                    sh.DashPattern = pattern; sh.InvalidatePen();
                    if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
                }
            }
            if (_selectedAnnotations.Count > 0) { MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged(); }

            SetActiveDashButton(btn);
        }
    }

    /// <summary>
    /// Highlights a toolbar button with a white border, clearing the previous one
    /// stored in the given field. Used for tool, color, width, and dash buttons.
    /// </summary>
    private static void SetActiveButton(ref Button? field, Button? btn)
    {
        if (field != null) { field.BorderThickness = new Thickness(0); field.BorderBrush = null; }
        field = btn;
        if (btn != null) { btn.BorderThickness = new Thickness(2); btn.BorderBrush = Brushes.White; }
    }

    private void SetActiveToolButton(Button? btn) => SetActiveButton(ref _activeToolButton, btn);
    private void SetActiveColorButton(Button? btn) => SetActiveButton(ref _activeColorButton, btn);
    private void SetActiveWidthButton(Button? btn) => SetActiveButton(ref _activeWidthButton, btn);
    private void SetActiveDashButton(Button? btn) => SetActiveButton(ref _activeDashButton, btn);

    /// <summary>Finds an annotation toolbar button whose Tag matches the given string.</summary>
    private Button? FindToolbarButtonByTag(string tag)
    {
        foreach (var child in AnnotateToolbar.GetVisualDescendants())
        {
            if (child is Button b && b.Tag is string t && t == tag)
                return b;
        }
        return null;
    }

    /// <summary>
    /// Highlights the default tool (Draw), color (Red), and width (3px) buttons
    /// when annotation mode is first activated.
    /// </summary>
    private void HighlightInitialButtons()
    {
        SetActiveToolButton(FindToolbarButtonByTag(MuPDFRenderer.ActiveTool.ToString()));
        SetActiveColorButton(FindToolbarButtonByTag("Red"));
        SetActiveWidthButton(FindToolbarButtonByTag(((int)MuPDFRenderer.StrokeWidth).ToString()));
        SetActiveDashButton(FindToolbarButtonByTag(MuPDFRenderer.StrokeDashPattern.ToString()));
    }

    /// <summary>
    /// Syncs <see cref="_normalStrokeWidth"/> and the width button highlight
    /// after a programmatic width change (e.g. keyboard shortcut).
    /// </summary>
    private void SyncWidthState()
    {
        if (!MuPDFRenderer.IsHighlighterMode)
            _normalStrokeWidth = MuPDFRenderer.StrokeWidth;
        SetActiveWidthButton(FindToolbarButtonByTag(((int)MuPDFRenderer.StrokeWidth).ToString()));
    }

    private void OnAnnotateUndo(object sender, RoutedEventArgs e) { MuPDFRenderer.Undo(); MuPDFRenderer.Focus(); }

    private void OnAnnotateRedo(object sender, RoutedEventArgs e) { MuPDFRenderer.Redo(); MuPDFRenderer.Focus(); }

    private void OnAnnotateClear(object sender, RoutedEventArgs e)
    {
        int count = MuPDFRenderer.CurrentPageAnnotationCount;
        if (count == 0) return;
        MuPDFRenderer.ClearPage();
    }

    private void OnOpacitySliderChanged(object? sender, RoutedEventArgs e)
    {
        if (OpacitySlider == null) return;
        MuPDFRenderer.StrokeOpacity = OpacitySlider.Value;
        if (_selectedAnnotations.Count > 0)
        {
            foreach (var selItem in _selectedAnnotations)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(selItem);
                switch (selItem)
                {
                    case InkStroke s: s.Opacity = OpacitySlider.Value; s.InvalidatePen(); break;
                    case ShapeAnnotation sh: sh.Opacity = OpacitySlider.Value; sh.InvalidatePen(); break;
                    case TextAnnotation t: t.Opacity = OpacitySlider.Value; break;
                }
                if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            }
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
        }
    }

    private void OnAnnotateCustomColor(object sender, RoutedEventArgs e)
    {
        // Open a simple color input via TextBox — parse hex like "#FF6600"
        var hex = "#" + MuPDFRenderer.StrokeColor.R.ToString("X2")
                      + MuPDFRenderer.StrokeColor.G.ToString("X2")
                      + MuPDFRenderer.StrokeColor.B.ToString("X2");
        ColorInputBox.Text = hex;
        ColorInputCanvas.IsVisible = true;
        ColorInputBox.Focus();
        ColorInputBox.SelectAll();
    }

    private void OnColorInputApply(object sender, RoutedEventArgs e)
    {
        if (Color.TryParse(ColorInputBox.Text?.Trim(), out var c))
        {
            MuPDFRenderer.StrokeColor = c;
            if (MuPDFRenderer.ActiveLayer != null)
                MuPDFRenderer.ActiveLayer.Color = c;
        }
        ColorInputCanvas.IsVisible = false;
        MuPDFRenderer.Focus();
    }

    private void OnColorInputCancel(object sender, RoutedEventArgs e)
    {
        ColorInputCanvas.IsVisible = false;
        MuPDFRenderer.Focus();
    }

    private void OnColorInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnColorInputApply(sender!, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { OnColorInputCancel(sender!, e); e.Handled = true; }
    }

    private void UpdateAnnotationCountBadge()
    {
        if (MuPDFRenderer.ActiveLayer is { } layer)
        {
            int count = layer.TotalCount;
            AnnotationCountBadge.Text = count > 0 ? $"{count}" : "";
        }
        else
        {
            AnnotationCountBadge.Text = "";
        }
    }

    private void UpdateFontSizeLabel()
    {
        FontSizeLabel.Text = $"{MuPDFRenderer.TextFontSize}pt";
    }

    private void UpdateUndoRedoButtons()
    {
        bool canUndo = MuPDFRenderer.CanUndoCurrentPage;
        bool canRedo = MuPDFRenderer.CanRedoCurrentPage;
        if (_undoBtn != null) { _undoBtn.Opacity = canUndo ? 1.0 : 0.35; _undoBtn.IsEnabled = canUndo; }
        if (_redoBtn != null) { _redoBtn.Opacity = canRedo ? 1.0 : 0.35; _redoBtn.IsEnabled = canRedo; }
    }

    private void OnAnnotationChanged()
    {
        UpdateAnnotationCountBadge();
        UpdateUndoRedoButtons();
        UpdateActiveLayerLabel();
    }

    /// <summary>Shows a context menu listing all layers; clicking one sets it as the active layer.</summary>
    private void OnLayerPickerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var layers = MuPDFRenderer.Layers;
        if (layers.Count == 0) return;
        var menu = new ContextMenu();
        foreach (var layer in layers.ToList())
        {
            var capturedLayer = layer;
            var item = new MenuItem
            {
                Header = capturedLayer.Name,
                IsChecked = capturedLayer == MuPDFRenderer.ActiveLayer
            };
            item.Click += (_, _) =>
            {
                MuPDFRenderer.ActiveLayer = capturedLayer;
                UpdateActiveLayerLabel();
                MuPDFRenderer.Focus();
            };
            menu.Items.Add(item);
        }
        menu.Open(btn);
    }

    /// <summary>Toggles the visibility of the active annotation layer.</summary>
    private void OnToggleLayerVisibility(object? sender, RoutedEventArgs e)
    {
        var layer = MuPDFRenderer.ActiveLayer;
        if (layer == null) return;
        layer.IsVisible = !layer.IsVisible;
        MuPDFRenderer.InvalidateVisual();
        UpdateActiveLayerLabel();
        MuPDFRenderer.Focus();
    }

    /// <summary>Syncs the layer label and visibility button opacity to the current active layer state.</summary>
    private void UpdateActiveLayerLabel()
    {
        var layer = MuPDFRenderer.ActiveLayer;
        if (layer == null) return;
        if (ActiveLayerLabel != null)
            ActiveLayerLabel.Text = layer.Name;
        if (LayerVisBtn != null)
            LayerVisBtn.Opacity = layer.IsVisible ? 1.0 : 0.35;
    }

    private void UpdateActiveToolLabel(InlineAnnotationTool tool)
    {
        ActiveToolLabel.Text = tool switch
        {
            InlineAnnotationTool.Draw => "Draw",
            InlineAnnotationTool.Highlight => "Highlight",
            InlineAnnotationTool.Rectangle => "Rectangle",
            InlineAnnotationTool.Ellipse => "Ellipse",
            InlineAnnotationTool.Line => "Line",
            InlineAnnotationTool.Arrow => "Arrow",
            InlineAnnotationTool.Polyline => "Polyline",
            InlineAnnotationTool.Text => "Comment",
            InlineAnnotationTool.ArrowText => "Arrow Comment",
            InlineAnnotationTool.StickyNote => "Sticky Note",
            InlineAnnotationTool.MeasureDistance => "Measure",
            InlineAnnotationTool.RevisionCloud => "Cloud",
            InlineAnnotationTool.Eraser => "Eraser",
            InlineAnnotationTool.Select => "Select",
            _ => tool.ToString()
        };
    }

    private void OnFontSizeDecrease(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.TextFontSize = Math.Max(6, MuPDFRenderer.TextFontSize - 2);
        UpdateFontSizeLabel();
        if (_selectedAnnotation is TextAnnotation t)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(t);
            t.FontSize = MuPDFRenderer.TextFontSize;
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged();
        }
    }

    private void OnFontSizeIncrease(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.TextFontSize = Math.Min(72, MuPDFRenderer.TextFontSize + 2);
        UpdateFontSizeLabel();
        if (_selectedAnnotation is TextAnnotation t)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(t);
            t.FontSize = MuPDFRenderer.TextFontSize;
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged();
        }
    }

    private void OnToggleFill(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.IsFilledMode = !MuPDFRenderer.IsFilledMode;

        // Apply to selected closed shape (rect / ellipse / cloud)
        if (_selectedAnnotation is ShapeAnnotation sh
            && sh.ShapeType is InlineAnnotationTool.Rectangle
                            or InlineAnnotationTool.Ellipse
                            or InlineAnnotationTool.RevisionCloud)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
            sh.IsFilled = MuPDFRenderer.IsFilledMode;
            sh.InvalidatePen();
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
        }

        SyncFillToggleButton(MuPDFRenderer.IsFilledMode);
    }

    /// <summary>Syncs the fill toggle button visual state to the given value.</summary>
    private void SyncFillToggleButton(bool isFilled)
    {
        if (FillToggleBtn == null) return;
        FillToggleBtn.BorderThickness = isFilled ? new Thickness(2) : new Thickness(0);
        FillToggleBtn.BorderBrush = isFilled ? Brushes.White : null;
    }

    private static void MoveAnnotation(object item, double dx, double dy)
    {
        switch (item)
        {
            case TextAnnotation t:
                t.Position = new Point(t.Position.X + dx, t.Position.Y + dy);
                if (t.ArrowOrigin.HasValue)
                    t.ArrowOrigin = new Point(t.ArrowOrigin.Value.X + dx, t.ArrowOrigin.Value.Y + dy);
                break;
            case ShapeAnnotation s:
                s.Start = new Point(s.Start.X + dx, s.Start.Y + dy);
                s.End = new Point(s.End.X + dx, s.End.Y + dy);
                s.InvalidatePen();
                break;
            case MeasurementAnnotation m:
                for (int i = 0; i < m.Points.Count; i++)
                    m.Points[i] = new Point(m.Points[i].X + dx, m.Points[i].Y + dy);
                break;
            case InkStroke ink:
                for (int i = 0; i < ink.Points.Count; i++)
                    ink.Points[i] = new Point(ink.Points[i].X + dx, ink.Points[i].Y + dy);
                ink.InvalidatePen();
                break;
        }
    }

    private Point? _pendingArrowOrigin;
    private bool _pendingStickyNote;

    private void ShowTextInput(Point pdfPoint, Point screenPos, Point? arrowOrigin = null, bool isStickyNote = false)
    {
        _textPlacementPdfPoint = pdfPoint;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = arrowOrigin;
        _pendingStickyNote = isStickyNote;
        Canvas.SetLeft(TextInputBorder, Math.Min(screenPos.X, MuPDFRenderer.Bounds.Width - 240));
        Canvas.SetTop(TextInputBorder, Math.Min(screenPos.Y, MuPDFRenderer.Bounds.Height - 100));
        TextInputBox.Text = "";
        TextInputCanvas.IsVisible = true;
        TextInputBox.Focus();
    }

    private void ShowTextEdit(TextAnnotation existing, Point screenPos)
    {
        _textPlacementPdfPoint = existing.Position;
        _editingTextAnnotation = existing;
        // Position the overlay directly over the annotation for in-place editing
        var da = MuPDFRenderer.DisplayArea;
        var bounds = MuPDFRenderer.Bounds;
        if (da.Width > 0 && bounds.Width > 0)
        {
            double annotX = (existing.Position.X - da.X) / da.Width * bounds.Width;
            double annotY = (existing.Position.Y - da.Y) / da.Height * bounds.Height;
            screenPos = new Point(annotX, annotY);
            // Match text input width to annotation's MaxWidth for WYSIWYG editing
            if (existing.MaxWidth > 0)
                TextInputBox.Width = Math.Clamp(existing.MaxWidth / da.Width * bounds.Width, 120, 600);
            else
                TextInputBox.Width = 220;
        }
        Canvas.SetLeft(TextInputBorder, Math.Min(screenPos.X, MuPDFRenderer.Bounds.Width - 240));
        Canvas.SetTop(TextInputBorder, Math.Min(screenPos.Y, MuPDFRenderer.Bounds.Height - 100));
        TextInputBox.Text = existing.Text;
        TextInputCanvas.IsVisible = true;
        TextInputBox.Focus();
    }

    private void OnTextInputCommit(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue && !string.IsNullOrWhiteSpace(TextInputBox.Text))
        {
            if (_editingTextAnnotation != null)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(_editingTextAnnotation);
                _editingTextAnnotation.Text = TextInputBox.Text;
                if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            }
            else if (_pendingStickyNote)
            {
                MuPDFRenderer.PlaceStickyNote(_textPlacementPdfPoint.Value, TextInputBox.Text);
            }
            else if (_pendingArrowOrigin.HasValue)
            {
                MuPDFRenderer.PlaceArrowText(_pendingArrowOrigin.Value,
                    _textPlacementPdfPoint.Value, TextInputBox.Text);
            }
            else
            {
                MuPDFRenderer.PlaceText(_textPlacementPdfPoint.Value, TextInputBox.Text);
            }
        }

        CloseTextInput();
        MuPDFRenderer.InvalidateVisual();
    }

    private void OnTextInputCancel(object sender, RoutedEventArgs e) => CloseTextInput();

    private void OnTextInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Plain Enter = commit; Shift+Enter = newline (handled by AcceptsReturn)
            OnTextInputCommit(sender!, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnTextInputCancel(sender!, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Clicking the canvas background (outside the text input border) auto-commits
    /// the current text, making the input feel more direct — no Done button needed.
    /// </summary>
    private void OnTextInputBackgroundClick(object? sender, PointerPressedEventArgs e)
    {
        // Only commit if the click is outside the TextInputBorder
        var pos = e.GetPosition(TextInputBorder);
        if (pos.X < 0 || pos.Y < 0 || pos.X > TextInputBorder.Bounds.Width || pos.Y > TextInputBorder.Bounds.Height)
        {
            OnTextInputCommit(this, e);
            e.Handled = true;
        }
    }

    private void OnMeasureCalibrate(object sender, RoutedEventArgs e)
    {
        // Always enter guided calibration mode: switch to the measure tool
        // and let the user draw a reference line. The distance input dialog
        // appears after the line is committed (in OnInkPointerPressed).
        // This avoids the old behaviour where the dialog would appear
        // immediately when measurements existed, blocking the PDF and
        // preventing the user from drawing a new reference line.
        _calibrationMode = true;
        ApplyToolSwitch(InlineAnnotationTool.MeasureDistance);
    }

    private void OnCalibrationApply(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(CalibrationValueBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double realMm) && realMm > 0)
        {
            MuPDFRenderer.CalibrateFromLastMeasurement(realMm);
        }
        CalibrationCanvas.IsVisible = false;
        MuPDFRenderer.Focus();
    }

    private void OnCalibrationCancel(object sender, RoutedEventArgs e)
    {
        CalibrationCanvas.IsVisible = false;
        _calibrationMode = false;
        MuPDFRenderer.Focus();
    }

    /// <summary>Centers the calibration dialog overlay in the preview area.</summary>
    private void CenterCalibrationDialog()
    {
        // Use approximate dialog size; Avalonia measures on next layout pass
        const double dialogWidth = 250;
        const double dialogHeight = 110;
        double cx = Math.Max(0, (MuPDFRenderer.Bounds.Width - dialogWidth) / 2);
        double cy = Math.Max(0, (MuPDFRenderer.Bounds.Height - dialogHeight) / 2);
        Canvas.SetLeft(CalibrationBorder, cx);
        Canvas.SetTop(CalibrationBorder, cy);
    }

    private void OnCalibrationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnCalibrationApply(sender!, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnCalibrationCancel(sender!, e);
            e.Handled = true;
        }
    }

    /// <summary>Shows a context menu for the currently selected annotation.</summary>
    private void ShowAnnotationContextMenu(Point screenPos)
    {
        if (_selectedAnnotation == null) return;

        var menu = new Avalonia.Controls.ContextMenu();

        var editItem = new MenuItem { Header = "Edit…" };
        editItem.Click += (_, _) =>
        {
            if (_selectedAnnotation is TextAnnotation t)
                ShowTextEdit(t, screenPos);
        };
        editItem.IsVisible = _selectedAnnotation is TextAnnotation;

        var duplicateItem = new MenuItem { Header = "Duplicate           Ctrl+D" };
        duplicateItem.Click += (_, _) =>
        {
            if (_selectedAnnotation != null)
            {
                _annotationClipboard = _selectedAnnotation;
                PasteAnnotation();
            }
        };

        var copyItem = new MenuItem { Header = "Copy                  Ctrl+C" };
        copyItem.Click += (_, _) =>
        {
            if (_selectedAnnotation != null)
                _annotationClipboard = _selectedAnnotation;
        };

        var deleteItem = new MenuItem { Header = "Delete                Del" };
        deleteItem.Click += (_, _) =>
        {
            if (_selectedAnnotation != null)
            {
                MuPDFRenderer.DeleteAnnotation(_selectedAnnotation);
                _selectedAnnotation = null;
            }
        };

        var sep1 = new Separator();

        var fillItem = new MenuItem
        {
            Header = _selectedAnnotation is ShapeAnnotation { IsFilled: true } ? "Remove Fill" : "Fill Shape"
        };
        fillItem.Click += (_, _) =>
        {
            if (_selectedAnnotation is ShapeAnnotation sh
                && sh.ShapeType is InlineAnnotationTool.Rectangle
                                or InlineAnnotationTool.Ellipse
                                or InlineAnnotationTool.RevisionCloud)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
                sh.IsFilled = !sh.IsFilled;
                MuPDFRenderer.IsFilledMode = sh.IsFilled;
                sh.InvalidatePen();
                if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
                MuPDFRenderer.InvalidateVisual();
                MuPDFRenderer.NotifyAnnotationChanged();
                SyncFillToggleButton(MuPDFRenderer.IsFilledMode);
            }
        };
        fillItem.IsVisible = _selectedAnnotation is ShapeAnnotation sh2
            && sh2.ShapeType is InlineAnnotationTool.Rectangle
                             or InlineAnnotationTool.Ellipse
                             or InlineAnnotationTool.RevisionCloud;

        var bringFrontItem = new MenuItem { Header = "Bring to Front" };
        bringFrontItem.Click += (_, _) =>
        {
            if (_selectedAnnotation != null)
                MuPDFRenderer.BringToFront(_selectedAnnotation);
        };

        var sendBackItem = new MenuItem { Header = "Send to Back" };
        sendBackItem.Click += (_, _) =>
        {
            if (_selectedAnnotation != null)
                MuPDFRenderer.SendToBack(_selectedAnnotation);
        };

        var sep2 = new Separator();

        var matchStyleItem = new MenuItem { Header = "Match Style" };
        matchStyleItem.Click += (_, _) =>
        {
            if (_selectedAnnotation == null) return;
            SavePreSelectState();
            SyncToolbarToSelection();
            DeselectAnnotation();
        };

        var pasteItem = new MenuItem { Header = "Paste                   Ctrl+V" };
        pasteItem.Click += (_, _) => PasteAnnotation();
        pasteItem.IsVisible = _annotationClipboard != null;

        var selectAllItem = new MenuItem { Header = "Select All on Page" };
        selectAllItem.Click += (_, _) => SelectAllOnPage();

        menu.Items.Add(matchStyleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(editItem);
        menu.Items.Add(duplicateItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(sep1);
        menu.Items.Add(fillItem);
        menu.Items.Add(bringFrontItem);
        menu.Items.Add(sendBackItem);
        menu.Items.Add(selectAllItem);
        menu.Items.Add(sep2);
        menu.Items.Add(deleteItem);

        menu.Open(MuPDFRenderer);
    }

    private void OnAnnotateKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            if (!_middlePanning)
                MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
            e.Handled = true;
        }
    }

    /// <summary>
    /// When an annotation is selected, update the toolbar buttons to reflect
    /// its color, width, dash, opacity, font size, and fill state.
    /// This gives Figma-style "inspect on select" feedback.
    /// </summary>
    private void SyncToolbarToSelection()
    {
        if (_selectedAnnotation == null) return;

        Color? color = null;
        double? width = null;
        LineDashPattern? dash = null;
        double? opacity = null;

        switch (_selectedAnnotation)
        {
            case InkStroke s:
                color = s.Color; width = s.Width; dash = s.DashPattern; opacity = s.Opacity;
                break;
            case ShapeAnnotation sh:
                color = sh.Color; width = sh.StrokeWidth; dash = sh.DashPattern; opacity = sh.Opacity;
                break;
            case TextAnnotation t:
                color = t.Color; opacity = t.Opacity;
                break;
            case MeasurementAnnotation m:
                color = m.Color;
                break;
        }

        // Sync color button highlight
        if (color.HasValue)
        {
            var tag = MatchColorTag(color.Value);
            SetActiveColorButton(tag != null ? FindToolbarButtonByTag(tag) : null);
            MuPDFRenderer.StrokeColor = color.Value;
        }

        // Sync width button highlight
        if (width.HasValue)
        {
            SetActiveWidthButton(FindToolbarButtonByTag(((int)width.Value).ToString()));
            if (!MuPDFRenderer.IsHighlighterMode)
            { MuPDFRenderer.StrokeWidth = width.Value; _normalStrokeWidth = width.Value; }
        }

        // Sync dash pattern button highlight
        if (dash.HasValue)
        {
            SetActiveDashButton(FindToolbarButtonByTag(dash.Value.ToString()));
            MuPDFRenderer.StrokeDashPattern = dash.Value;
        }

        // Sync opacity slider
        if (opacity.HasValue && OpacitySlider != null)
            OpacitySlider.Value = opacity.Value;

        // Sync font size for text annotations
        if (_selectedAnnotation is TextAnnotation txt)
        {
            MuPDFRenderer.TextFontSize = txt.FontSize;
            UpdateFontSizeLabel();
        }

        // Sync fill toggle for closed shapes
        if (_selectedAnnotation is ShapeAnnotation shape
            && shape.ShapeType is InlineAnnotationTool.Rectangle
                               or InlineAnnotationTool.Ellipse
                               or InlineAnnotationTool.RevisionCloud)
        {
            MuPDFRenderer.IsFilledMode = shape.IsFilled;
            SyncFillToggleButton(shape.IsFilled);
        }
    }

    private static string? MatchColorTag(Color c) =>
        ColorNames.GetValueOrDefault(c);

    #endregion
}