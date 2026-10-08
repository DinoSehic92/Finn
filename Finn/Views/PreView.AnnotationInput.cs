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

    // Throttle swipe-to-erase: skip EraseAt when pointer hasn't moved far enough
    private Point _lastErasePdf;
    private const double EraseThresholdSq = 4.0; // ~2 PDF pts between erase samples

    private void OnInkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SetActiveAnnotationRenderer(sender);
        if (!_annotateMode) return;
        try
        {
        var point = e.GetCurrentPoint(AnnotationRenderer);

        // Mouse side-buttons: XButton1 = back = Undo, XButton2 = forward = Redo
        if (point.Properties.IsXButton1Pressed)
        {
            AnnotationRenderer.Undo();
            e.Handled = true;
            return;
        }
        if (point.Properties.IsXButton2Pressed)
        {
            AnnotationRenderer.Redo();
            e.Handled = true;
            return;
        }

        // Space+left-click: pan (Figma-style)
        if (_spaceHeld && point.Properties.IsLeftButtonPressed)
        {
            BeginPan(AnnotationRenderer, e);
            return;
        }

        // Middle-mouse: pan even in annotation mode
        if (point.Properties.IsMiddleButtonPressed)
        {
            BeginPan(AnnotationRenderer, e);
            AnnotationRenderer.Cursor = CursorHand;
            return;
        }

        // Right-click: finish/cancel active operations
        if (point.Properties.IsRightButtonPressed)
        {
            if (_matchStyleArmed)
            {
                CancelMatchStyle();
                e.Handled = true;
                return;
            }
            if (AnnotationRenderer.HasActivePolyline)
            {
                // For area measurements with too few points, cancel rather than commit a degenerate shape
                if (AnnotationRenderer.IsActivePolylineAreaMeasure && AnnotationRenderer.ActivePolylinePointCount < 3)
                    AnnotationRenderer.CancelPolyline();
                else
                    AnnotationRenderer.EndPolyline();
                ApplyToolSwitch(InlineAnnotationTool.Select);
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            if (AnnotationRenderer.HasActiveShape)
            {
                AnnotationRenderer.CancelStroke();
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            if (AnnotationRenderer.HasActiveMeasurement)
            {
                AnnotationRenderer.CancelStroke();
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            if (_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null)
            {
                OnTextInputCancel(this, e);
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            if (_arrowTextOrigin != null)
            {
                _arrowTextOrigin = null;
                AnnotationRenderer.ClearArrowTextPreview();
                _inkDrawing = false;
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            if (_inkDrawing)
            {
                AnnotationRenderer.CancelStroke();
                _inkDrawing = false;
                UpdateAnnotationStatusHint();
                e.Handled = true;
                return;
            }
            // Right-click with nothing active: switch to Select tool, then select hit item or deselect.
            ApplyToolSwitch(InlineAnnotationTool.Select);
            var rightPdf = AnnotationRenderer.ScreenToPdf(e.GetPosition(AnnotationRenderer));
            object? rightHit = rightPdf.HasValue ? AnnotationRenderer.FindTopmostAt(rightPdf.Value) : null;
            if (rightHit != null)
            {
                SelectAnnotation(rightHit);
                e.Handled = true;
                return;
            }
            if (_selectedAnnotation != null)
            {
                DeselectAnnotation();
            }
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        var pdfPoint = AnnotationRenderer.ScreenToPdf(e.GetPosition(AnnotationRenderer));
        if (!pdfPoint.HasValue) return;

        if (_matchStyleArmed)
        {
            var matchHit = AnnotationRenderer.FindTopmostAt(pdfPoint.Value);
            if (matchHit != null && _matchStyleSource != null && !ReferenceEquals(matchHit, _matchStyleSource))
            {
                ApplyMatchedStyle(_matchStyleSource, matchHit);
                SelectAnnotation(matchHit);
                CancelMatchStyle();
            }
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        e.Pointer.Capture(AnnotationRenderer);
        e.Handled = true;

        var tool = AnnotationRenderer.ActiveTool;

        // ── Two-click shapes: second click commits ──
        if (AnnotationRenderer.HasActiveShape)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            AnnotationRenderer.UpdateShape(pdfPoint.Value, shift);
            AnnotationRenderer.EndShape();
            ApplyToolSwitch(InlineAnnotationTool.Select);
            e.Pointer.Capture(null);
            return;
        }

        // ── Two-click measurement: second click commits ──
        if (AnnotationRenderer.HasActiveMeasurement)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            AnnotationRenderer.UpdateMeasurementPreview(pdfPoint.Value, shift);
            AnnotationRenderer.EndMeasurement();
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
            else
            {
                // Non-calibration measurement: return to Select so the result label is selectable
                ApplyToolSwitch(InlineAnnotationTool.Select);
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
                hitItem = AnnotationRenderer.FindStickyNoteAt(pdfPoint.Value);
            else if (tool is InlineAnnotationTool.Text or InlineAnnotationTool.ArrowText)
                hitItem = AnnotationRenderer.FindTextAt(pdfPoint.Value);
            else
                hitItem = AnnotationRenderer.FindTopmostAt(pdfPoint.Value);

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
                    if (!AnnotationRenderer.IsActiveLayerLocked)
                    {
                        // Double-click text: open property panel with inline text editor
                        var screenPos = e.GetPosition(AnnotationRenderer);
                        ShowTextEdit(hitText, screenPos);
                    }
                    else
                    {
                        ShowPropertyPanel(hitText, e.GetPosition(AnnotationRenderer));
                    }
                    e.Pointer.Capture(null);
                    return;
                }
                // Locked layer: allow selection but not dragging/editing
                if (AnnotationRenderer.IsActiveLayerLocked)
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
                    _preDragSnapshot = AnnotationRenderer.CapturePreDragSnapshot(hitText);
                    _inkDrawing = true;
                    SelectAnnotation(hitText);
                    return;
                }
                // Right-edge resize handle: check before generic drag
                if (!hitText.IsStickyNote && hitText.MaxWidth > 0)
                {
                    var handlePdf = AnnotationRenderer.GetTextResizeHandlePdfPoint(hitText);
                    if (handlePdf.HasValue && IsNear(pdfPoint.Value, handlePdf.Value, HitRadius(HandleHitScreenPxLarge)))
                    {
                        _resizingTextAnnotation = hitText;
                        _dragStartPdf = pdfPoint.Value;
                        _preDragSnapshot = AnnotationRenderer.CapturePropertySnapshot(hitText);
                        _inkDrawing = true;
                        AnnotationRenderer.Cursor = CursorSizeWE;
                        SelectAnnotation(hitText);
                        return;
                    }
                }
                // Single click: select + start drag
                _draggingTextAnnotation = hitText;
                _dragStartPdf = pdfPoint.Value;
                _preDragSnapshot = AnnotationRenderer.CapturePreDragSnapshot(hitText);
                _inkDrawing = true;
                AnnotationRenderer.Cursor = CursorSizeAll;
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
                && !AnnotationRenderer.IsActiveLayerLocked
                && TryBeginVertexDrag(_selectedAnnotation, pdfPoint.Value, HitRadius(HandleHitScreenPx)))
            {
                AnnotationRenderer.Cursor = CursorCross;
                return;
            }
            if (hitItem != null)
            {
                // Double-click: open property panel for non-text annotations (read-only OK on locked layer)
                if (e.ClickCount >= 2 && hitItem is not TextAnnotation)
                {
                    SelectAnnotation(hitItem);
                    ShowPropertyPanel(hitItem, e.GetPosition(AnnotationRenderer));
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
                if (AnnotationRenderer.IsActiveLayerLocked)
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
                    _preDragSnapshot = AnnotationRenderer.CapturePreDragSnapshot(arrowHit);
                    _inkDrawing = true;
                    SelectAnnotation(arrowHit);
                    return;
                }
                // Vertex drag: check if click is near any vertex of the hit annotation.
                // Skip when the item is part of a multi-selection — move takes priority to
                // avoid accidentally stretching one annotation when the user intends to move all.
                bool inMultiSelect = _selectedAnnotations.Count >= 2 && _selectedAnnotations.Contains(hitItem);
                if (!inMultiSelect && TryBeginVertexDrag(hitItem, pdfPoint.Value, HitRadius(HandleHitScreenPx)))
                {
                    AnnotationRenderer.Cursor = CursorCross;
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
                            var snap = AnnotationRenderer.CapturePreDragSnapshot(sel);
                            if (snap != null) _multiDragSnapshots.Add(snap);
                        }
                        _inkDrawing = true;
                        AnnotationRenderer.Cursor = CursorSizeAll;
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
                    var snap = AnnotationRenderer.CapturePreDragSnapshot(sel);
                    if (snap != null) _multiDragSnapshots.Add(snap);
                }
                _inkDrawing = true;
                AnnotationRenderer.Cursor = CursorSizeAll;
                return;
            }
            // No annotation body hit — but check if we clicked a vertex of the currently selected annotation
            // (needed for ellipses where Start/End are at bounding-box corners, outside the ellipse boundary,
            //  and for measurements/polylines whose endpoints may extend past the hit-test region)
            // Multi-selection / group resize handles take priority over individual vertex drag.
            if (hitItem == null && _selectedAnnotations.Count >= 2 && !AnnotationRenderer.IsActiveLayerLocked)
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
                    var handlePdf = AnnotationRenderer.GetTextResizeHandlePdfPoint(selText);
                    if (handlePdf.HasValue && IsNear(pdfPoint.Value, handlePdf.Value, HitRadius(ResizeHandleHitScreenPx)))
                    {
                        _resizingTextAnnotation = selText;
                        _dragStartPdf = pdfPoint.Value;
                        _preDragSnapshot = AnnotationRenderer.CapturePropertySnapshot(selText);
                        _inkDrawing = true;
                        AnnotationRenderer.Cursor = CursorSizeWE;
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
        // Block creation/mutation when the active layer is locked
        if (AnnotationRenderer.IsActiveLayerLocked)
        {
            SetAnnotationStatusText("Active layer is locked — unlock it in the layer panel or choose another layer to annotate.");
            e.Pointer.Capture(null);
            return;
        }
        AnnotationRenderer.ClearTextPlacementPreview();
        switch (tool)
        {
            case InlineAnnotationTool.Eraser:
                AnnotationRenderer.EraseAt(pdfPoint.Value);
                _lastErasePdf = pdfPoint.Value;
                _inkDrawing = true;
                break;

            case InlineAnnotationTool.Text:
                ShowTextInput(pdfPoint.Value, e.GetPosition(AnnotationRenderer));
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.StickyNote:
                ShowTextInput(pdfPoint.Value, e.GetPosition(AnnotationRenderer), isStickyNote: true);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.ArrowText:
                if (_arrowTextOrigin != null)
                {
                    // Second click: freeze arrow at endpoint and show text input
                    AnnotationRenderer.UpdateArrowTextPreview(pdfPoint.Value);
                    ShowTextInput(pdfPoint.Value, e.GetPosition(AnnotationRenderer), _arrowTextOrigin.Value);
                    e.Pointer.Capture(null);
                }
                else
                {
                    // First click: set arrow origin
                    _arrowTextOrigin = pdfPoint.Value;
                    AnnotationRenderer.SetArrowTextPreview(pdfPoint.Value);
                    e.Pointer.Capture(null);
                }
                break;

            case InlineAnnotationTool.MeasureDistance:
                AnnotationRenderer.BeginMeasurement(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.Select:
                // Empty space — start rubber-band marquee selection
                _rubberBandActive = true;
                _rubberBandCrossing = false;
                _rubberBandAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _rubberBandSubtractive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                _rubberBandStartPdf = pdfPoint.Value;
                _inkDrawing = true;
                AnnotationRenderer.SetRubberBand(pdfPoint.Value, pdfPoint.Value, false);
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                // First click: begin shape (no drag needed — preview follows cursor)
                AnnotationRenderer.BeginShape(pdfPoint.Value);
                e.Pointer.Capture(null);
                break;

            case InlineAnnotationTool.Polyline:
            {
                bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (AnnotationRenderer.HasActivePolyline)
                {
                    bool autoClose = AnnotationRenderer.ShouldAutoCloseActivePolyline(pdfPoint.Value, HitRadius(12));
                    if (e.ClickCount >= 2)
                    {
                        // The first click of the double-click already added a point via ClickCount==1;
                        // remove it so we don't get a duplicate node at the end.
                        AnnotationRenderer.RemoveLastPolylinePoint();
                        AnnotationRenderer.EndPolyline(close: autoClose);
                        ApplyToolSwitch(InlineAnnotationTool.Select);
                    }
                    else if (autoClose)
                    {
                        AnnotationRenderer.EndPolyline(close: true);
                        ApplyToolSwitch(InlineAnnotationTool.Select);
                    }
                    else
                        AnnotationRenderer.AddPolylinePoint(pdfPoint.Value, shift);
                }
                else
                    AnnotationRenderer.BeginPolyline(pdfPoint.Value);
                UpdateAnnotationStatusHint();
                e.Pointer.Capture(null);
                break;
            }

            case InlineAnnotationTool.MeasureArea:
            {
                bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (AnnotationRenderer.HasActivePolyline)
                {
                    bool autoClose = AnnotationRenderer.ShouldAutoCloseActivePolyline(pdfPoint.Value, HitRadius(12));
                    if (e.ClickCount >= 2)
                    {
                        AnnotationRenderer.RemoveLastPolylinePoint();
                        // Need at least 3 points for a valid area. Cancel if insufficient.
                        if (AnnotationRenderer.ActivePolylinePointCount < 3)
                            AnnotationRenderer.CancelPolyline();
                        else
                            AnnotationRenderer.EndPolyline(close: true);
                        ApplyToolSwitch(InlineAnnotationTool.Select);
                    }
                    else if (autoClose)
                    {
                        AnnotationRenderer.EndPolyline(close: true);
                        ApplyToolSwitch(InlineAnnotationTool.Select);
                    }
                    else
                        AnnotationRenderer.AddPolylinePoint(pdfPoint.Value, shift);
                }
                else
                    AnnotationRenderer.BeginPolyline(pdfPoint.Value, asAreaMeasure: true);
                UpdateAnnotationStatusHint();
                e.Pointer.Capture(null);
                break;
            }

            case InlineAnnotationTool.Dot:
                AnnotationRenderer.PlaceDot(pdfPoint.Value);
                ApplyToolSwitch(InlineAnnotationTool.Select);
                e.Pointer.Capture(null);
                break;

            default: // Draw, Highlight
                _inkDrawing = true;
                AnnotationRenderer.BeginStroke(pdfPoint.Value);
                UpdateAnnotationStatusHint();
                break;
        }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerPressed"); }
    }

    private void OnInkPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_selectedAnnotations.Count == 0)
            SetActiveAnnotationRenderer(sender);
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

            var hoverPdf = AnnotationRenderer.ScreenToPdf(e.GetPosition(AnnotationRenderer));

            // Two-click shapes: live preview follows cursor without drag
            if (_annotateMode && AnnotationRenderer.HasActiveShape)
            {
                if (hoverPdf.HasValue)
                    AnnotationRenderer.UpdateShape(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return;
            }

            // Two-click measurement: live preview follows cursor without drag
            if (_annotateMode && AnnotationRenderer.HasActiveMeasurement)
            {
                if (hoverPdf.HasValue)
                    AnnotationRenderer.UpdateMeasurementPreview(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return;
            }

            // Polyline preview: always track cursor when a polyline is active
            if (_annotateMode && AnnotationRenderer.HasActivePolyline)
            {
                if (hoverPdf.HasValue)
                {
                    AnnotationRenderer.UpdatePolylinePreview(hoverPdf.Value,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }
            }
            // ArrowText preview: track cursor after first click sets origin (freeze while typing)
            else if (_annotateMode && _arrowTextOrigin != null && _textPlacementPdfPoint == null)
            {
                if (hoverPdf.HasValue)
                    AnnotationRenderer.UpdateArrowTextPreview(hoverPdf.Value);
            }
            // Eraser hover highlight: track what's under the cursor
            else if (_annotateMode && AnnotationRenderer.ActiveTool == InlineAnnotationTool.Eraser)
            {
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
                        AnnotationRenderer.UpdateEraserHover(hoverPdf.Value);
                    else
                        AnnotationRenderer.ClearEraserHover();
                }
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
                        AnnotationRenderer.UpdateStickyNoteHover(hoverPdf.Value);
                    else
                        AnnotationRenderer.ClearStickyNoteHover();
                }

                // Text/Sticky/ArrowText: show ghost at cursor for placement preview
                var at = AnnotationRenderer.ActiveTool;
                if (at is InlineAnnotationTool.Text or InlineAnnotationTool.StickyNote or InlineAnnotationTool.ArrowText)
                {
                    if (hoverPdf.HasValue)
                        AnnotationRenderer.UpdateTextPlacementPreview(at, hoverPdf.Value);
                    else
                        AnnotationRenderer.ClearTextPlacementPreview();
                }
                else
                    AnnotationRenderer.ClearTextPlacementPreview();

                // Shape/polyline/measurement creation tools: show snap guides on first-click hover
                // so the user can see snapping feedback before committing the first point.
                if (hoverPdf.HasValue && at is InlineAnnotationTool.Rectangle
                        or InlineAnnotationTool.Ellipse
                        or InlineAnnotationTool.Line
                        or InlineAnnotationTool.Arrow
                        or InlineAnnotationTool.RevisionCloud
                        or InlineAnnotationTool.Polyline
                        or InlineAnnotationTool.MeasureArea
                        or InlineAnnotationTool.MeasureDistance)
                {
                    if (hoverMoved)
                        AnnotationRenderer.UpdateSnapPreview(hoverPdf.Value);
                }
                else if (at is not (InlineAnnotationTool.Text or InlineAnnotationTool.StickyNote
                                 or InlineAnnotationTool.ArrowText or InlineAnnotationTool.Select
                                 or InlineAnnotationTool.Eraser))
                {
                    AnnotationRenderer.ClearSnapGuides();
                }

                // Hover outline + cursor: show only for tools that can grab annotations
                if (hoverMoved && hoverPdf.HasValue
                    && at is InlineAnnotationTool.Select
                        or InlineAnnotationTool.Text
                        or InlineAnnotationTool.ArrowText
                        or InlineAnnotationTool.StickyNote)
                {
                    var hoverHit = AnnotationRenderer.FindTopmostAt(hoverPdf.Value);
                    AnnotationRenderer.UpdateSelectHover(hoverHit);
                    ShowHoverEditHint(hoverHit);
                    if (at is InlineAnnotationTool.Select)
                        AnnotationRenderer.Cursor = GetHoverCursor(hoverPdf.Value, hoverHit);
                }
                else if (hoverMoved)
                {
                    AnnotationRenderer.UpdateSelectHover(null);
                    UpdateAnnotationStatusHint();
                }
            }
            return;
        }
        e.Handled = true;

        var pdfPoint = AnnotationRenderer.ScreenToPdf(e.GetPosition(AnnotationRenderer));
        if (!pdfPoint.HasValue) return;

        // Swipe-to-erase: continuously erase while dragging with eraser tool
        if (AnnotationRenderer.ActiveTool == InlineAnnotationTool.Eraser)
        {
            double edx = pdfPoint.Value.X - _lastErasePdf.X;
            double edy = pdfPoint.Value.Y - _lastErasePdf.Y;
            if (edx * edx + edy * edy >= EraseThresholdSq)
            {
                AnnotationRenderer.EraseAt(pdfPoint.Value);
                _lastErasePdf = pdfPoint.Value;
            }
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
                        target = AnnotationRenderer.ComputeVertexSnap(sv, target);
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
                        target = AnnotatedPDFRenderer.ConstrainToFineAngle(anchor, target);
                    }
                    else
                    {
                        target = AnnotationRenderer.ComputeVertexSnap(mv, target);
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
                            target = AnnotatedPDFRenderer.ConstrainToFineAngle(pv.Points[adjIdx], target);
                    }
                    else
                    {
                        target = AnnotationRenderer.ComputeVertexSnap(pv, target);
                    }
                    pv.Points[_draggingVertexIndex] = target;
                    pv.InvalidatePen();
                    break;
                }
            }
            AnnotationRenderer.InvalidateVisual();
            return;
        }

        // Handle text width resize — adjusts MaxWidth via right-edge drag
        if (_resizingTextAnnotation != null)
        {
            double rotation = AnnotationRotation.GetRenderRotation(_resizingTextAnnotation.CreatedAtRotation);
            double newWidth;
            if (Math.Abs(rotation) > 0.01)
            {
                // Project the mouse position onto the text's local horizontal axis.
                // The local X direction in screen space is (cos(angle), sin(angle)).
                var originScreen = AnnotationRenderer.PdfToScreenPoint(_resizingTextAnnotation.Position);
                var mouseScreen = AnnotationRenderer.PdfToScreenPoint(pdfPoint.Value);
                if (originScreen.HasValue && mouseScreen.HasValue)
                {
                    double rad = rotation * Math.PI / 180.0;
                    double lx = Math.Cos(rad), ly = Math.Sin(rad);
                    double dx = mouseScreen.Value.X - originScreen.Value.X;
                    double dy = mouseScreen.Value.Y - originScreen.Value.Y;
                    double projectedPx = dx * lx + dy * ly;
                    newWidth = Math.Max(30, AnnotationRenderer.ScreenToPdfDistance(projectedPx));
                }
                else
                {
                    newWidth = _resizingTextAnnotation.MaxWidth;
                }
            }
            else
            {
                var snapped = AnnotationRenderer.ComputeVertexSnap(_resizingTextAnnotation,
                    new Point(pdfPoint.Value.X, _resizingTextAnnotation.Position.Y));
                newWidth = Math.Max(30, snapped.X - _resizingTextAnnotation.Position.X);
            }
            _resizingTextAnnotation.MaxWidth = newWidth;
            AnnotationRenderer.InvalidateVisual();
            return;
        }

        // Handle arrow origin (tip) dragging — moves only ArrowOrigin
        if (_draggingArrowOrigin != null)
        {
            var target = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? AnnotatedPDFRenderer.ConstrainToFineAngle(_draggingArrowOrigin.Position, pdfPoint.Value)
                : AnnotationRenderer.ComputeVertexSnap(_draggingArrowOrigin, pdfPoint.Value);
            _draggingArrowOrigin.ArrowOrigin = target;
            _dragStartPdf = pdfPoint.Value;
            AnnotationRenderer.InvalidateVisual();
            return;
        }

        // Handle text annotation dragging (with snap-to-alignment)
        // For ArrowText, only the text box moves — the arrow origin stays pinned.
        if (_draggingTextAnnotation != null)
        {
            double rawDx = pdfPoint.Value.X - _dragStartPdf.X;
            double rawDy = pdfPoint.Value.Y - _dragStartPdf.Y;
            var (dx, dy) = AnnotationRenderer.ComputeSnapDelta(_draggingTextAnnotation, rawDx, rawDy);
            _draggingTextAnnotation.Position = new Point(
                _draggingTextAnnotation.Position.X + dx,
                _draggingTextAnnotation.Position.Y + dy);
            _dragStartPdf = new Point(_dragStartPdf.X + dx, _dragStartPdf.Y + dy);
            AnnotationRenderer.InvalidateVisual();
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
            AnnotationRenderer.InvalidateVisual();
            return;
        }

        // Select tool: drag any annotation type (with snap-to-alignment)
        if (_selectDragItem != null)
        {
            double rawDx = pdfPoint.Value.X - _dragStartPdf.X;
            double rawDy = pdfPoint.Value.Y - _dragStartPdf.Y;
            var (dx, dy) = AnnotationRenderer.ComputeSnapDelta(_selectDragItem, rawDx, rawDy);
            foreach (var item in _selectedAnnotations)
                MoveAnnotation(item, dx, dy);
            _dragStartPdf = new Point(_dragStartPdf.X + dx, _dragStartPdf.Y + dy);
            AnnotationRenderer.InvalidateVisual();
            return;
        }

        // Rubber-band marquee: update rectangle while dragging
        if (_rubberBandActive)
        {
            _rubberBandCrossing = pdfPoint.Value.X < _rubberBandStartPdf.X;
            AnnotationRenderer.SetRubberBand(_rubberBandStartPdf, pdfPoint.Value, _rubberBandCrossing);
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var tool = AnnotationRenderer.ActiveTool;

        // Update pen cursor preview for freehand tools
        if (tool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight)
            AnnotationRenderer.UpdateCursorPreview(pdfPoint.Value);

        switch (tool)
        {
            case InlineAnnotationTool.MeasureDistance:
                AnnotationRenderer.SetPolarCursorScreen(e.GetPosition(AnnotationRenderer));
                AnnotationRenderer.UpdateMeasurementPreview(pdfPoint.Value, shift);
                break;

            case InlineAnnotationTool.Polyline:
            case InlineAnnotationTool.MeasureArea:
                AnnotationRenderer.SetPolarCursorScreen(e.GetPosition(AnnotationRenderer));
                AnnotationRenderer.UpdatePolylinePreview(pdfPoint.Value, shift);
                break;

            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
                AnnotationRenderer.SetPolarCursorScreen(e.GetPosition(AnnotationRenderer));
                AnnotationRenderer.UpdateShape(pdfPoint.Value, shift);
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.RevisionCloud:
                AnnotationRenderer.UpdateShape(pdfPoint.Value, shift);
                break;

            default: // Draw, Highlight
                AnnotationRenderer.AddPoint(pdfPoint.Value);
                break;
        }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerMoved"); }
    }

    private void OnInkPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        SetActiveAnnotationRenderer(sender);
        if (!_annotateMode) return;
        try
        {

        if (_middlePanning)
        {
            _middlePanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            AnnotationRenderer.Cursor = _spaceHeld ? CursorHand : GetToolCursor(AnnotationRenderer.ActiveTool);
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
            UpdateAnnotationStatusHint();
            return;
        }
        if (_resizingTextAnnotation != null)
        {
            FinishDrag(isPropertyUndo: true);
            UpdateAnnotationStatusHint();
            return;
        }

        // Handle rubber-band marquee release — select all items in the rectangle
        if (_rubberBandActive)
        {
            _rubberBandActive = false;
            bool crossing = _rubberBandCrossing;
            _rubberBandCrossing = false;
            var endPdf = AnnotationRenderer.ScreenToPdf(e.GetPosition(AnnotationRenderer));
            AnnotationRenderer.ClearRubberBand();
            if (endPdf.HasValue)
            {
                double x = Math.Min(_rubberBandStartPdf.X, endPdf.Value.X);
                double y = Math.Min(_rubberBandStartPdf.Y, endPdf.Value.Y);
                double w = Math.Abs(endPdf.Value.X - _rubberBandStartPdf.X);
                double h = Math.Abs(endPdf.Value.Y - _rubberBandStartPdf.Y);
                if (w > 2 || h > 2) // ignore tiny accidental drags
                {
                    var rect = new Rect(x, y, w, h);
                    var found = AnnotationRenderer.FindAnnotationsInRect(rect, crossing);
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

                        AnnotationRenderer.ClearSelectHighlight();
                        foreach (var item in _selectedAnnotations)
                            AnnotationRenderer.AddSelectHighlight(item);

                        _selectedAnnotation = _selectedAnnotations.Count > 0 ? _selectedAnnotations.First() : null;
                        if (_selectedAnnotation != null)
                            SyncToolbarToSelection();
                    }
                }
                else if (!_rubberBandAdditive && !_rubberBandSubtractive)
                {
                    // Zero-size drag on empty space = deselect all.
                    // DeselectAnnotation handles RestorePreSelectState so the toolbar
                    // color/width/dash/opacity revert correctly after deselecting.
                    if (PropertyPanelCanvas.IsVisible) ClosePropertyPanel();
                    DeselectAnnotation();
                }
            }
            _rubberBandAdditive = false;
            _rubberBandSubtractive = false;
            AnnotationRenderer.Cursor = GetToolCursor(AnnotationRenderer.ActiveTool);
            UpdateAnnotationStatusHint();
            return;
        }

        var tool = AnnotationRenderer.ActiveTool;
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
                AnnotationRenderer.EndStroke();
                AnnotationRenderer.UpdateCursorPreview(null);
                break;
        }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "OnInkPointerReleased"); }
    }
}
