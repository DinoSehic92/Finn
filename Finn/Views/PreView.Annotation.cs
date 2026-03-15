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

namespace Finn.Views;

public partial class PreView
{
    #region Inline Annotation

    private bool _annotateMode;
    private bool _inkDrawing;
    private bool _middlePanning;
    private Point _panStart;
    private Rect _panStartDisplayArea;
    private Avalonia.Controls.ContextMenu? _savedContextMenu;
    private Button? _activeToolButton;
    private Button? _activeColorButton;
    private Button? _activeWidthButton;
    private double _normalStrokeWidth = 3;
    private Point? _textPlacementPdfPoint;
    private TextAnnotation? _editingTextAnnotation;
    private TextAnnotation? _draggingTextAnnotation;
    private Point _dragStartPdf;
    private bool _calibrationMode;
    private Point? _arrowTextOrigin;
    private object? _selectDragItem;

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

        // Annotation only works in single-page view. Collapse dual modes first.
        if (pwr.TwopageMode)
            pwr.TwopageMode = false;
        if (pwr.DualFileMode)
            pwr.DualFileMode = false;

        _annotateMode = true;
        pwr.AnnotationActive = true;
        AnnotateToggle.IsChecked = true;
        AnnotateToolbar.IsVisible = true;
        MuPDFRenderer.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.Pan;

        MuPDFRenderer.EnsureDefaultLayer();
        MuPDFRenderer.AddHandler(PointerPressedEvent, OnInkPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerMovedEvent, OnInkPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerReleasedEvent, OnInkPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        this.AddHandler(KeyDownEvent, OnAnnotateKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Suppress the context menu so right-click can finish polylines
        _savedContextMenu = MuPDFRenderer.ContextMenu as Avalonia.Controls.ContextMenu;
        MuPDFRenderer.ContextMenu = null;

        // Highlight default tool/color/width buttons to match initial state
        HighlightInitialButtons();
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
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        _draggingTextAnnotation = null;
        _selectDragItem = null;
        _calibrationMode = false;
        _arrowTextOrigin = null;
        _pendingStickyNote = false;
        MuPDFRenderer.CancelStroke();
        MuPDFRenderer.CancelPolyline();
        MuPDFRenderer.UpdateCursorPreview(null);
        MuPDFRenderer.Cursor = Avalonia.Input.Cursor.Default;
        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.PanHighlight;
        MuPDFRenderer.ActiveTool = InlineAnnotationTool.Draw;

        MuPDFRenderer.RemoveHandler(PointerPressedEvent, OnInkPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerMovedEvent, OnInkPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnInkPointerReleased);
        this.RemoveHandler(KeyDownEvent, OnAnnotateKeyDown);

        // Restore the context menu
        if (_savedContextMenu != null)
        {
            MuPDFRenderer.ContextMenu = _savedContextMenu;
            _savedContextMenu = null;
        }
        SetActiveToolButton(null);
        SetActiveColorButton(null);
        SetActiveWidthButton(null);
    }

    private void OnAnnotateKeyDown(object? sender, KeyEventArgs e)
    {
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

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            MuPDFRenderer.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            MuPDFRenderer.Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnAnnotateCopy(this, e);
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
            else
            {
                DeactivateAnnotateMode();
            }
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

    private static readonly Avalonia.Input.Cursor CursorCross = new(Avalonia.Input.StandardCursorType.Cross);
    private static readonly Avalonia.Input.Cursor CursorNo = new(Avalonia.Input.StandardCursorType.No);
    private static readonly Avalonia.Input.Cursor CursorIbeam = new(Avalonia.Input.StandardCursorType.Ibeam);
    private static readonly Avalonia.Input.Cursor CursorSizeAll = new(Avalonia.Input.StandardCursorType.SizeAll);

    private static Avalonia.Input.Cursor GetToolCursor(InlineAnnotationTool tool) => tool switch
    {
        InlineAnnotationTool.Eraser => CursorNo,
        InlineAnnotationTool.Text => CursorIbeam,
        InlineAnnotationTool.StickyNote => CursorIbeam,
        InlineAnnotationTool.Select => CursorSizeAll,
        InlineAnnotationTool.Polyline => CursorCross,
        _ => CursorCross
    };

    /// <summary>
    /// Core tool-switching logic shared by button clicks and keyboard shortcuts.
    /// </summary>
    private void ApplyToolSwitch(InlineAnnotationTool tool)
    {
        var previousTool = MuPDFRenderer.ActiveTool;

        if (previousTool is InlineAnnotationTool.MeasureDistance && tool != previousTool)
        {
            MuPDFRenderer.CancelStroke();
            _inkDrawing = false;
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

        MuPDFRenderer.ActiveTool = tool;
        MuPDFRenderer.ClearEraserHover();
        MuPDFRenderer.ClearSelectHighlight();
        MuPDFRenderer.ClearStickyNoteHover();
        MuPDFRenderer.UpdateCursorPreview(null);

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

        // Middle-mouse: pan even in annotation mode
        if (point.Properties.IsMiddleButtonPressed)
        {
            _middlePanning = true;
            _panStart = e.GetPosition(MuPDFRenderer);
            _panStartDisplayArea = MuPDFRenderer.DisplayArea;
            e.Pointer.Capture(MuPDFRenderer);
            e.Handled = true;
            MuPDFRenderer.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            return;
        }

        // Right-click: finish active polyline, or cancel any other in-progress operation
        if (point.Properties.IsRightButtonPressed)
        {
            if (MuPDFRenderer.HasActivePolyline)
            {
                MuPDFRenderer.EndPolyline();
                e.Handled = true;
                return;
            }
            if (TextInputCanvas.IsVisible)
            {
                OnTextInputCancel(this, e);
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
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        var pdfPoint = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        if (!pdfPoint.HasValue) return;

        e.Pointer.Capture(MuPDFRenderer);
        e.Handled = true;

        var tool = MuPDFRenderer.ActiveTool;
        switch (tool)
        {
            case InlineAnnotationTool.Eraser:
                MuPDFRenderer.EraseAt(pdfPoint.Value);
                _inkDrawing = true;
                break;

            case InlineAnnotationTool.Text:
            {
                var existing = MuPDFRenderer.FindTextAt(pdfPoint.Value);
                if (existing != null && e.ClickCount >= 2)
                {
                    // Double-click: edit existing text
                    ShowTextEdit(existing, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                }
                else if (existing != null)
                {
                    // Single-click on existing: start drag
                    _draggingTextAnnotation = existing;
                    _dragStartPdf = pdfPoint.Value;
                    _inkDrawing = true;
                }
                else
                {
                    ShowTextInput(pdfPoint.Value, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                }
                break;
            }

            case InlineAnnotationTool.StickyNote:
            {
                var existingSN = MuPDFRenderer.FindStickyNoteAt(pdfPoint.Value);
                if (existingSN != null && e.ClickCount >= 2)
                {
                    ShowTextEdit(existingSN, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                }
                else if (existingSN != null)
                {
                    _draggingTextAnnotation = existingSN;
                    _dragStartPdf = pdfPoint.Value;
                    _inkDrawing = true;
                }
                else
                {
                    ShowTextInput(pdfPoint.Value, e.GetPosition(MuPDFRenderer), isStickyNote: true);
                    e.Pointer.Capture(null);
                }
                break;
            }

            case InlineAnnotationTool.ArrowText:
            {
                var existingAT = MuPDFRenderer.FindTextAt(pdfPoint.Value);
                if (existingAT != null && e.ClickCount >= 2)
                {
                    ShowTextEdit(existingAT, e.GetPosition(MuPDFRenderer));
                    e.Pointer.Capture(null);
                }
                else if (existingAT != null)
                {
                    // Single-click on existing: drag (arrow origin stays fixed)
                    _draggingTextAnnotation = existingAT;
                    _dragStartPdf = pdfPoint.Value;
                    _inkDrawing = true;
                }
                else
                {
                    // Press to set arrow anchor, drag to endpoint, release to place text
                    _arrowTextOrigin = pdfPoint.Value;
                    MuPDFRenderer.SetArrowTextPreview(pdfPoint.Value);
                    _inkDrawing = true;
                }
                break;
            }

            case InlineAnnotationTool.MeasureDistance:
                _inkDrawing = true;
                MuPDFRenderer.BeginMeasurement(pdfPoint.Value);
                break;

            case InlineAnnotationTool.Select:
            {
                var hit = MuPDFRenderer.FindTopmostAt(pdfPoint.Value);
                if (hit != null)
                {
                    _selectDragItem = hit;
                    _dragStartPdf = pdfPoint.Value;
                    _inkDrawing = true;
                    MuPDFRenderer.SetSelectHighlight(hit);
                }
                break;
            }

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                _inkDrawing = true;
                MuPDFRenderer.BeginShape(pdfPoint.Value);
                break;

            case InlineAnnotationTool.Polyline:
            {
                if (MuPDFRenderer.HasActivePolyline)
                    MuPDFRenderer.AddPolylinePoint(pdfPoint.Value);
                else
                    MuPDFRenderer.BeginPolyline(pdfPoint.Value);
                // Don't set _inkDrawing — polyline uses multi-click, not drag
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
        if (_middlePanning)
        {
            e.Handled = true;
            var current = e.GetPosition(MuPDFRenderer);
            var da = _panStartDisplayArea;
            var bounds = MuPDFRenderer.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            double dx = (_panStart.X - current.X) / bounds.Width  * da.Width;
            double dy = (_panStart.Y - current.Y) / bounds.Height * da.Height;
            MuPDFRenderer.SetDisplayAreaNow(new Rect(da.X + dx, da.Y + dy, da.Width, da.Height));
            return;
        }

        if (!_inkDrawing)
        {
            // Polyline preview: always track cursor when a polyline is active
            if (_annotateMode && MuPDFRenderer.HasActivePolyline)
            {
                var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdatePolylinePreview(hoverPdf.Value);
            }
            // Eraser hover highlight: track what's under the cursor
            else if (_annotateMode && MuPDFRenderer.ActiveTool == InlineAnnotationTool.Eraser)
            {
                var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateEraserHover(hoverPdf.Value);
                else
                    MuPDFRenderer.ClearEraserHover();
            }
            // Sticky note hover popup: show when cursor is over any note icon
            else if (_annotateMode)
            {
                var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateStickyNoteHover(hoverPdf.Value);
                else
                    MuPDFRenderer.ClearStickyNoteHover();
            }
            // Pen cursor preview when hovering (not drawing)
            else if (_annotateMode && MuPDFRenderer.ActiveTool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight)
            {
                var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
                MuPDFRenderer.UpdateCursorPreview(hoverPdf);
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

        // Handle text annotation dragging
        if (_draggingTextAnnotation != null)
        {
            double dx = pdfPoint.Value.X - _dragStartPdf.X;
            double dy = pdfPoint.Value.Y - _dragStartPdf.Y;
            _draggingTextAnnotation.Position = new Point(
                _draggingTextAnnotation.Position.X + dx,
                _draggingTextAnnotation.Position.Y + dy);
            _dragStartPdf = pdfPoint.Value;
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // Select tool: drag any annotation type
        if (_selectDragItem != null)
        {
            double dx = pdfPoint.Value.X - _dragStartPdf.X;
            double dy = pdfPoint.Value.Y - _dragStartPdf.Y;
            MoveAnnotation(_selectDragItem, dx, dy);
            _dragStartPdf = pdfPoint.Value;
            MuPDFRenderer.InvalidateVisual();
            return;
        }

        // ArrowText drag: update live preview arrow from origin to cursor
        if (_arrowTextOrigin != null && MuPDFRenderer.ActiveTool == InlineAnnotationTool.ArrowText)
        {
            MuPDFRenderer.UpdateArrowTextPreview(pdfPoint.Value);
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
        if (_middlePanning)
        {
            _middlePanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
            return;
        }

        if (!_inkDrawing) return;
        _inkDrawing = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        // Handle Select tool drag release
        if (_selectDragItem != null)
        {
            _selectDragItem = null;
            MuPDFRenderer.ClearSelectHighlight();
            MuPDFRenderer.NotifyAnnotationChanged();
            return;
        }

        // Handle text annotation drag release (single-click move only; double-click edits)
        if (_draggingTextAnnotation != null)
        {
            _draggingTextAnnotation = null;
            return;
        }

        // ArrowText drag release: show text input at the endpoint
        if (_arrowTextOrigin != null && MuPDFRenderer.ActiveTool == InlineAnnotationTool.ArrowText)
        {
            var releasePos = e.GetPosition(MuPDFRenderer);
            var releasePdf = MuPDFRenderer.ScreenToPdf(releasePos);
            if (releasePdf.HasValue)
            {
                ShowTextInput(releasePdf.Value, releasePos, _arrowTextOrigin.Value);
            }
            else
            {
                _arrowTextOrigin = null;
                MuPDFRenderer.ClearArrowTextPreview();
            }
            return;
        }

        var tool = MuPDFRenderer.ActiveTool;
        switch (tool)
        {
            case InlineAnnotationTool.MeasureDistance:
                MuPDFRenderer.EndMeasurement();
                // Guided calibration: after drawing the reference measurement, show calibration input
                if (_calibrationMode)
                {
                    _calibrationMode = false;
                    CalibrationPromptText.Text = "Enter the real-world distance for the line you just drew:";
                    CalibrationValueBox.Text = "";
                    CalibrationCanvas.IsVisible = true;
                    CalibrationValueBox.Focus();
                }
                break;

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
            case InlineAnnotationTool.RevisionCloud:
                MuPDFRenderer.EndShape();
                break;

            default: // Draw, Highlight
                MuPDFRenderer.EndStroke();
                break;
        }
    }

    private void OnAnnotateColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            var color = colorName switch
            {
                "Red" => Color.FromRgb(214, 64, 69),
                "Blue" => Color.FromRgb(59, 130, 217),
                "Green" => Color.FromRgb(61, 163, 95),
                "Black" => Color.FromRgb(34, 34, 34),
                "Orange" => Color.FromRgb(232, 125, 47),
                "Purple" => Color.FromRgb(139, 92, 246),
                _ => Color.FromRgb(214, 64, 69)
            };
            MuPDFRenderer.StrokeColor = color;

            if (MuPDFRenderer.ActiveLayer != null)
                MuPDFRenderer.ActiveLayer.Color = color;

            SetActiveColorButton(btn);
        }
    }

    private void OnAnnotateToolSelect(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName)
        {
            var tool = Enum.Parse<InlineAnnotationTool>(toolName);
            ApplyToolSwitch(tool);
        }
    }

    private void OnAnnotateWidth(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widthStr && double.TryParse(widthStr, out double w))
        {
            if (MuPDFRenderer.IsHighlighterMode) return;
            MuPDFRenderer.StrokeWidth = w;
            _normalStrokeWidth = w;

            SetActiveWidthButton(btn);
        }
    }

    /// <summary>
    /// Highlights the currently selected tool button in the annotation toolbar
    /// with a subtle border, clearing the previous selection.
    /// </summary>
    private void SetActiveToolButton(Button? btn)
    {
        if (_activeToolButton != null)
        {
            _activeToolButton.BorderThickness = new Thickness(0);
            _activeToolButton.BorderBrush = null;
        }
        _activeToolButton = btn;
        if (btn != null)
        {
            btn.BorderThickness = new Thickness(2);
            btn.BorderBrush = Brushes.White;
        }
    }

    private void SetActiveColorButton(Button? btn)
    {
        if (_activeColorButton != null)
        {
            _activeColorButton.BorderThickness = new Thickness(0);
            _activeColorButton.BorderBrush = null;
        }
        _activeColorButton = btn;
        if (btn != null)
        {
            btn.BorderThickness = new Thickness(2);
            btn.BorderBrush = Brushes.White;
        }
    }

    private void SetActiveWidthButton(Button? btn)
    {
        if (_activeWidthButton != null)
        {
            _activeWidthButton.BorderThickness = new Thickness(0);
            _activeWidthButton.BorderBrush = null;
        }
        _activeWidthButton = btn;
        if (btn != null)
        {
            btn.BorderThickness = new Thickness(2);
            btn.BorderBrush = Brushes.White;
        }
    }

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

    private void OnAnnotateUndo(object sender, RoutedEventArgs e) => MuPDFRenderer.Undo();

    private void OnAnnotateRedo(object sender, RoutedEventArgs e) => MuPDFRenderer.Redo();

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
    }

    private void OnColorInputCancel(object sender, RoutedEventArgs e)
    {
        ColorInputCanvas.IsVisible = false;
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
    }

    private void OnFontSizeIncrease(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.TextFontSize = Math.Min(72, MuPDFRenderer.TextFontSize + 2);
        UpdateFontSizeLabel();
    }

    private void OnToggleFill(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.IsFilledMode = !MuPDFRenderer.IsFilledMode;
        if (FillToggleBtn != null)
        {
            FillToggleBtn.BorderThickness = MuPDFRenderer.IsFilledMode ? new Thickness(2) : new Thickness(0);
            FillToggleBtn.BorderBrush = MuPDFRenderer.IsFilledMode ? Brushes.White : null;
        }
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
                _editingTextAnnotation.Text = TextInputBox.Text;
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

        TextInputCanvas.IsVisible = false;
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = null;
        _pendingStickyNote = false;
        _arrowTextOrigin = null;
        MuPDFRenderer.ClearArrowTextPreview();
        MuPDFRenderer.InvalidateVisual();
    }

    private void OnTextInputCancel(object sender, RoutedEventArgs e)
    {
        TextInputCanvas.IsVisible = false;
        _textPlacementPdfPoint = null;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = null;
        _pendingStickyNote = false;
        _arrowTextOrigin = null;
        MuPDFRenderer.ClearArrowTextPreview();
    }

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

    private void OnMeasureCalibrate(object sender, RoutedEventArgs e)
    {
        // Check if any measurements exist on the current page
        var existing = MuPDFRenderer.GetMeasurements(pwr.CurrentPage1);
        if (existing.Count == 0)
        {
            // No measurements yet — enter guided calibration mode:
            // switch to measure tool and let the user draw a reference line first.
            _calibrationMode = true;
            MuPDFRenderer.ActiveTool = InlineAnnotationTool.MeasureDistance;
            MuPDFRenderer.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
            return;
        }

        CalibrationPromptText.Text = "Enter real-world distance for the last measurement:";
        CalibrationValueBox.Text = "";
        CalibrationCanvas.IsVisible = true;
        CalibrationValueBox.Focus();
    }

    private void OnCalibrationApply(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(CalibrationValueBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double realMm) && realMm > 0)
        {
            MuPDFRenderer.CalibrateFromLastMeasurement(realMm);
        }
        CalibrationCanvas.IsVisible = false;
    }

    private void OnCalibrationCancel(object sender, RoutedEventArgs e)
    {
        CalibrationCanvas.IsVisible = false;
        _calibrationMode = false;
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

    #endregion
}