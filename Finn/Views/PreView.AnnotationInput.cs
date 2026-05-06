using Finn.Model;
using Finn.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using MuPDFCore.MuPDFRenderer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Finn.Views;

/// <summary>
/// Pointer input handling for inline annotations (pressed, moved, released).
/// </summary>
public partial class PreView
{
    // Throttle hover hit-testing: skip scan when cursor hasn't moved enough (#14)
    private Point _lastHoverPdf;
    private const double HoverThresholdSq = 1.0; // ~1 PDF pt ≈ sub-pixel at most zooms

    private void OnInkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_annotateMode) return;
        try
        {
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
            if (_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null)
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
                if (MuPDFRenderer.ActiveTool != InlineAnnotationTool.Select)
                    ApplyToolSwitch(InlineAnnotationTool.Select);
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
                    if (!MuPDFRenderer.IsActiveLayerLocked)
                    {
                        // Double-click text: open property panel with inline text editor
                        var screenPos = e.GetPosition(MuPDFRenderer);
                        ShowTextEdit(hitText, screenPos);
                    }
                    else
                    {
                        ShowPropertyPanel(hitText, e.GetPosition(MuPDFRenderer));
                    }
                    e.Pointer.Capture(null);
                    return;
                }
                // Locked layer: allow selection but not dragging/editing
                if (MuPDFRenderer.IsActiveLayerLocked)
                {
                    SelectAnnotation(hitText);
                    e.Pointer.Capture(null);
                    return;
                }
                // Check if the click is specifically on the arrow origin (tip)
                if (hitText.ArrowOrigin.HasValue && IsNear(pdfPoint.Value, hitText.ArrowOrigin.Value, HitRadius(ArrowTipHitScreenPx)))
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
                    if (IsNear(pdfPoint.Value, handlePoint, HitRadius(HandleHitScreenPxLarge)))
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
            // Prioritize vertex drag of the currently selected annotation over
            // body-hit of any overlapping item. This mirrors how tools like
            // Figma and Illustrator work: once selected, an annotation's
            // handles take priority so vertices behind filled shapes can still
            // be dragged.
            if (hitItem != null && _selectedAnnotation != null
                && hitItem != _selectedAnnotation
                && !MuPDFRenderer.IsActiveLayerLocked
                && TryBeginVertexDrag(_selectedAnnotation, pdfPoint.Value, HitRadius(HandleHitScreenPx)))
            {
                MuPDFRenderer.Cursor = CursorCross;
                return;
            }
            if (hitItem != null)
            {
                // Double-click: open property panel for non-text annotations (read-only OK on locked layer)
                if (e.ClickCount >= 2 && hitItem is not TextAnnotation)
                {
                    SelectAnnotation(hitItem);
                    ShowPropertyPanel(hitItem, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                    return;
                }
                // Shift+Click: toggle multi-selection
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ToggleAnnotationSelection(hitItem);
                    e.Pointer.Capture(null);
                    return;
                }
                // Locked layer: allow selection but not dragging
                if (MuPDFRenderer.IsActiveLayerLocked)
                {
                    SelectAnnotation(hitItem);
                    e.Pointer.Capture(null);
                    return;
                }
                // Check if it's an ArrowText and the click is on the arrow tip
                if (hitItem is TextAnnotation arrowHit && arrowHit.ArrowOrigin.HasValue
                    && IsNear(pdfPoint.Value, arrowHit.ArrowOrigin.Value, HitRadius(ArrowTipHitScreenPx)))
                {
                    _draggingArrowOrigin = arrowHit;
                    _dragStartPdf = pdfPoint.Value;
                    _preDragSnapshot = MuPDFRenderer.CapturePreDragSnapshot(arrowHit);
                    _inkDrawing = true;
                    SelectAnnotation(arrowHit);
                    return;
                }
                // Vertex drag: check if click is near any vertex of the hit annotation
                if (TryBeginVertexDrag(hitItem, pdfPoint.Value, HitRadius(HandleHitScreenPx)))
                {
                    MuPDFRenderer.Cursor = CursorCross;
                    SelectAnnotation(hitItem);
                    return;
                }

                // If click is inside the bounding box of the selected annotation (not just on the edge), start move drag
                if (_selectedAnnotations.Count > 0 && _selectedAnnotations.Contains(hitItem))
                {
                    var bounds = Finn.Controls.AnnotatedPDFRenderer.GetAnnotationBounds(hitItem);
                    if (bounds.Contains(pdfPoint.Value))
                    {
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
                }
                // Non-text annotation: select + start whole-drag
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
            // Multi-selection / group resize handles take priority over individual vertex drag.
            if (hitItem == null && _selectedAnnotations.Count >= 2 && !MuPDFRenderer.IsActiveLayerLocked)
            {
                if (TryBeginGroupResize(pdfPoint.Value))
                    return;
            }
            if (hitItem == null && _selectedAnnotation != null)
            {
                if (TryBeginVertexDrag(_selectedAnnotation, pdfPoint.Value, HitRadius(HandleHitScreenPxLarge)))
                    return;

                // Text right-edge resize handle (outside text body but near the handle dot)
                if (_selectedAnnotation is TextAnnotation { IsStickyNote: false, MaxWidth: > 0 } selText)
                {
                    var rtb = AnnotatedPDFRenderer.GetTextBounds(selText);
                    var handlePoint = new Point(rtb.Right, (rtb.Top + rtb.Bottom) / 2);
                    if (IsNear(pdfPoint.Value, handlePoint, HitRadius(ResizeHandleHitScreenPx)))
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
            // Multi-selection resize handle: check combined bounding-box corners
            if (hitItem == null && _selectedAnnotations.Count >= 2 && !MuPDFRenderer.IsActiveLayerLocked)
            {
                if (TryBeginGroupResize(pdfPoint.Value))
                    return;
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
        // Block creation/mutation when the active layer is locked
        if (MuPDFRenderer.IsActiveLayerLocked)
        {
            e.Pointer.Capture(null);
            return;
        }
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
                _rubberBandAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _rubberBandSubtractive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
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
                    bool autoClose = MuPDFRenderer.ShouldAutoCloseActivePolyline(pdfPoint.Value, HitRadius(12));
                    if (e.ClickCount >= 2)
                    {
                        // The first click of the double-click already added a point via ClickCount==1;
                        // remove it so we don't get a duplicate node at the end.
                        MuPDFRenderer.RemoveLastPolylinePoint();
                        MuPDFRenderer.EndPolyline(close: autoClose);
                    }
                    else if (autoClose)
                    {
                        MuPDFRenderer.EndPolyline(close: true);
                    }
                    else
                        MuPDFRenderer.AddPolylinePoint(pdfPoint.Value, shift);
                }
                else
                    MuPDFRenderer.BeginPolyline(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;
            }

            case InlineAnnotationTool.MeasureArea:
            {
                bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (MuPDFRenderer.HasActivePolyline)
                {
                    bool autoClose = MuPDFRenderer.ShouldAutoCloseActivePolyline(pdfPoint.Value, HitRadius(12));
                    if (e.ClickCount >= 2)
                    {
                        MuPDFRenderer.RemoveLastPolylinePoint();
                        MuPDFRenderer.EndPolyline(close: true);
                    }
                    else if (autoClose)
                    {
                        MuPDFRenderer.EndPolyline(close: true);
                    }
                    else
                        MuPDFRenderer.AddPolylinePoint(pdfPoint.Value, shift);
                }
                else
                    MuPDFRenderer.BeginPolyline(pdfPoint.Value, asAreaMeasure: true);
                e.Pointer.Capture(null);
                break;
            }

            case InlineAnnotationTool.Dot:
                MuPDFRenderer.PlaceDot(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;

            default: // Draw, Highlight
                _inkDrawing = true;
                MuPDFRenderer.BeginStroke(pdfPoint.Value);
                break;
        }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerPressed"); }
    }

    private void OnInkPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_annotateMode) return;
        try
        {

        if (_middlePanning)
        {
            UpdatePan(e);
            return;
        }

        if (!_inkDrawing)
        {
            // In annotation mode, mark as handled so the PDFRenderer's internal
            // Pan handler cannot override our cursor on every PointerMoved.
            e.Handled = true;

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
                {
                    MuPDFRenderer.UpdatePolylinePreview(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }
            }
            // ArrowText preview: track cursor after first click sets origin (freeze while typing)
            else if (_annotateMode && _arrowTextOrigin != null && _textPlacementPdfPoint == null)
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
            // General hover: sticky-note popup, text placement ghost, select outline
            else if (_annotateMode)
            {
                // Throttle hover hit-testing: skip when cursor barely moved (#14)
                bool hoverMoved = true;
                if (hoverPdf.HasValue)
                {
                    double hdx = hoverPdf.Value.X - _lastHoverPdf.X;
                    double hdy = hoverPdf.Value.Y - _lastHoverPdf.Y;
                    if (hdx * hdx + hdy * hdy < HoverThresholdSq)
                        hoverMoved = false;
                    else
                        _lastHoverPdf = hoverPdf.Value;
                }

                if (hoverMoved)
                {
                    if (hoverPdf.HasValue)
                        MuPDFRenderer.UpdateStickyNoteHover(hoverPdf.Value);
                    else
                        MuPDFRenderer.ClearStickyNoteHover();
                }

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
                if (hoverMoved && hoverPdf.HasValue
                    && at is InlineAnnotationTool.Select
                        or InlineAnnotationTool.Text
                        or InlineAnnotationTool.ArrowText
                        or InlineAnnotationTool.StickyNote)
                {
                    var hoverHit = MuPDFRenderer.FindTopmostAt(hoverPdf.Value);
                    MuPDFRenderer.UpdateSelectHover(hoverHit);
                    if (at is InlineAnnotationTool.Select)
                        MuPDFRenderer.Cursor = GetHoverCursor(hoverPdf.Value, hoverHit);
                }
                else if (hoverMoved)
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
                            : AnnotatedPDFRenderer.ConstrainToFineAngle(anchor, target);
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
                    else
                    {
                        target = MuPDFRenderer.ComputeVertexSnap(pv, target);
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

        // Group resize: proportionally scale all selected annotations
        if (_groupResizing)
        {
            var anchor = _groupResizeAnchor;
            // Signed distance from anchor to the original dragged corner
            double origW = _groupResizeDragCorner.X - anchor.X;
            double origH = _groupResizeDragCorner.Y - anchor.Y;
            // Avoid division by zero for degenerate (flat) selections
            if (Math.Abs(origW) < 1) origW = origW < 0 ? -1 : 1;
            if (Math.Abs(origH) < 1) origH = origH < 0 ? -1 : 1;
            // Cursor distance from anchor in the same signed space
            double curW = pdfPoint.Value.X - anchor.X;
            double curH = pdfPoint.Value.Y - anchor.Y;
            double sx = curW / origW;
            double sy = curH / origH;
            // Shift: uniform scale
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                double uniform = Math.Max(Math.Abs(sx), Math.Abs(sy));
                sx = sx < 0 ? -uniform : uniform;
                sy = sy < 0 ? -uniform : uniform;
            }
            // Prevent zero/tiny scale
            if (Math.Abs(sx) < 0.05) sx = Math.Sign(sx) == 0 ? 0.05 : 0.05 * Math.Sign(sx);
            if (Math.Abs(sy) < 0.05) sy = Math.Sign(sy) == 0 ? 0.05 : 0.05 * Math.Sign(sy);
            // Restore from full state snapshots, then apply scale
            if (_groupResizeStates != null)
            {
                foreach (var state in _groupResizeStates)
                    AnnotatedPDFRenderer.RestoreGroupResizeSnapshot(state);
            }
            foreach (var sel in _selectedAnnotations)
                AnnotatedPDFRenderer.ScaleAnnotation(sel, anchor, sx, sy);
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
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerMoved"); }
    }

    private void OnInkPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_annotateMode) return;
        try
        {

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
            || _draggingTextAnnotation != null || _selectDragItem != null
            || _groupResizing)
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
                        if (_rubberBandSubtractive)
                        {
                            foreach (var item in found)
                                _selectedAnnotations.Remove(item);
                        }
                        else if (_rubberBandAdditive)
                        {
                            foreach (var item in found)
                                _selectedAnnotations.Add(item);
                        }
                        else
                        {
                            SavePreSelectState();
                            _selectedAnnotations.Clear();
                            foreach (var item in found)
                                _selectedAnnotations.Add(item);
                        }

                        MuPDFRenderer.ClearSelectHighlight();
                        foreach (var item in _selectedAnnotations)
                            MuPDFRenderer.AddSelectHighlight(item);

                        _selectedAnnotation = _selectedAnnotations.Count > 0 ? _selectedAnnotations.First() : null;
                        if (_selectedAnnotation != null)
                            SyncToolbarToSelection();
                    }
                }
            }
            _rubberBandAdditive = false;
            _rubberBandSubtractive = false;
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
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerReleased"); }
    }
}
