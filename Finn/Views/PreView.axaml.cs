using Finn.ViewModels;
using Finn.Model;
using Finn.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MuPDFCore;
using MuPDFCore.MuPDFRenderer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Finn.Views;

public partial class PreView : UserControl
{
    public PreView()
    {
        InitializeComponent();

        ScrollSlider.AddHandler(Slider.ValueChangedEvent, PageNrSlider);
        ScrollSliderSecondary.AddHandler(Slider.ValueChangedEvent, SecondaryPageNrSlider);
        PreviewGrid.AddHandler(Grid.SizeChangedEvent, PreviewSizeChanged);
        TextInputBox.AddHandler(KeyDownEvent, OnTextInputKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OpacitySlider.AddHandler(Slider.ValueChangedEvent, OnOpacitySliderChanged);

        this.AddHandler(LoadedEvent, InitSetup);
    }

    public MainViewModel ctx = null;
    public PreviewViewModel pwr = null;
    public RotateTransform rotation = new RotateTransform(0);
    private bool _rendererPointerDown = false;
    private bool ZoomMode = false;

    public void InitSetup(object sender, RoutedEventArgs e)
    {
        ctx = (MainViewModel)this.DataContext;
        pwr = ctx.PreviewVM;

        pwr.PropertyChanged += OnBindingPwr;

        SetRenderer();

        MuPDFRenderer.AnnotationChanged += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ctx.MarkDirty();
                UpdateAnnotationCountBadge();
                pwr.CurrentFile?.RefreshAnnotationStatus();
            });
        };

        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) =>
            {
                var color = ctx.UI.DarkMode
                    ? ctx.UI.Color1
                    : ctx.UI.Color3;
                pwr.UpdateThemeRegionColor(color);
            };
        }
    }

    public void OnBindingPwr(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "SearchMode") { SetSearchFocus(); }

        // Deactivate annotation mode on file switch. CurrentFile fires from
        // a background thread (SetFileAsync after ConfigureAwait), so dispatch.
        if (e.PropertyName == "CurrentFile" && !pwr.WhiteboardMode)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => DeactivateAnnotateMode());
        }

        if (e.PropertyName == "CurrentPage1")
        {
            MuPDFRenderer.SetStrokePage(pwr.CurrentPage1);
            SyncLayers();
        }

        if (e.PropertyName == "WhiteboardMode")
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (pwr.WhiteboardMode)
                {
                    MuPDFRenderer.SetLayers(pwr.WhiteboardLayers);
                    ActivateAnnotateMode();
                }
                else
                {
                    SyncLayers();
                    DeactivateAnnotateMode();
                }
            });
        }
    }

    /// <summary>
    /// Points the renderer at the correct layer collection for the
    /// current context (whiteboard or file). Safe to call at any time.
    /// </summary>
    private void SyncLayers()
    {
        if (pwr == null || pwr.WhiteboardMode) return;
        var layers = pwr.CurrentFile?.AnnotationLayers;
        MuPDFRenderer.SetLayers(layers);
    }


    public void SetRenderer()
    {
        DeactivateAnnotateMode();
        _inkDrawing = false;
        _middlePanning = false;

        SyncLayers();

        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.PanHighlight;
        MuPDFRenderer.ActivateLinks = false;
        MuPDFRenderer.DrawLinks = false;

        MuPDFRendererSecondary.ActivateLinks = false;
        MuPDFRendererSecondary.DrawLinks = false;

        MuPDFRenderer.RemoveHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRenderer.RemoveHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);
        MuPDFRendererSecondary.RemoveHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRendererSecondary.RemoveHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRendererSecondary.RemoveHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);

        MuPDFRenderer.AddHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRenderer.AddHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRenderer.AddHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);
        MuPDFRendererSecondary.AddHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRendererSecondary.AddHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRendererSecondary.AddHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);

        ctx.PreviewVM.GetRenderControl(MuPDFRenderer, MuPDFRendererSecondary);
    }

    private async void OnSeachRegex(object sender, RoutedEventArgs e)
    {
        string text = SearchRegex.Text;
        await pwr.SearchAsync(text);
    }

    private void SetSearchFocus()
    {
        SearchRegex.Clear();
        SearchRegex.Focus();
    }

    private async void OnClearSearch(object sender, RoutedEventArgs e)
    {
        if (pwr.SearchBusy)
        {
            await pwr.StopSearchAsync();
        }
        else
        {
            pwr.ClearSearch();
            SearchRegex.Clear();
        }
    }

    private void OnStartSearhRegex(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSeachRegex(null, null);
        }
    }

    private void PageNrSlider(object sender, RoutedEventArgs e)
    {
        if (ScrollSlider.IsFocused)
        {
            if ((int)ScrollSlider.Value - 1 != pwr.RequestPage1)
            {
                pwr.RequestPage1 = (int)ScrollSlider.Value - 1;
            }
        }
    }

    private void SecondaryPageNrSlider(object sender, RoutedEventArgs e)
    {
        if (ScrollSliderSecondary.IsFocused)
        {
            int page = (int)ScrollSliderSecondary.Value - 1;
            if (pwr.DualFileMode && pwr.LinkedPageMode)
            {
                if (page != pwr.RequestPage1)
                    pwr.RequestPage1 = page;
            }
            else
            {
                if (page != pwr.RequestPage2)
                    pwr.RequestPage2 = page;
            }
        }
    }


    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResetView(null, null);
    }

    private void ResetView(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.Contain();
        MuPDFRendererSecondary.Contain();
    }

    public void RotateRight(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation + 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    public void RotateLeft(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation - 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    public void RotateNull()
    {
        pwr.Rotation = 0;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    private void ModifiedControlPointerWheelChanged(object sender, PointerWheelEventArgs e)
    {
        bool ctrlHeld = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrlHeld != ZoomMode)
        {
            ZoomMode = ctrlHeld;
            MuPDFRenderer.ZoomEnabled = ZoomMode;
            MuPDFRendererSecondary.ZoomEnabled = ZoomMode;
        }

        if (ZoomMode)
            return;

        if (!_rendererPointerDown && pwr.Pagecount > 0)
        {
            PDFRenderer currentSender = (PDFRenderer)sender;
            bool secondPage = currentSender.Name?.ToString() == "MuPDFRendererSecondary";

            if (e.Delta.Y > 0) pwr.PrevPage(secondPage);
            if (e.Delta.Y < 0) pwr.NextPage(secondPage);
        }
    }

    private void OnRendererPointerPressed(object? sender, PointerPressedEventArgs e)
        => _rendererPointerDown = true;

    private void OnRendererPointerReleased(object? sender, PointerReleasedEventArgs e)
        => _rendererPointerDown = false;

    private void OnRendererPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => _rendererPointerDown = false;

    #region Inline Annotation

    private bool _annotateMode;
    private bool _inkDrawing;
    private bool _middlePanning;
    private Point _panStart;
    private Rect _panStartDisplayArea;
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
        MuPDFRenderer.CancelStroke();
        MuPDFRenderer.UpdateCursorPreview(null);
        MuPDFRenderer.Cursor = Avalonia.Input.Cursor.Default;
        MuPDFRenderer.PointerEventHandlersType = PDFRenderer.PointerEventHandlers.PanHighlight;
        MuPDFRenderer.ActiveTool = InlineAnnotationTool.Draw;

        MuPDFRenderer.RemoveHandler(PointerPressedEvent, OnInkPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerMovedEvent, OnInkPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnInkPointerReleased);
        this.RemoveHandler(KeyDownEvent, OnAnnotateKeyDown);
        SetActiveToolButton(null);
        SetActiveColorButton(null);
        SetActiveWidthButton(null);
    }

    private void OnAnnotateKeyDown(object? sender, KeyEventArgs e)
    {
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
            if (TextInputCanvas.IsVisible)
            {
                OnTextInputCancel(this, e);
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
                _ => null
            };
            if (toolKey.HasValue)
            {
                SwitchTool(toolKey.Value);
                e.Handled = true;
            }
            else if (e.Key == Key.OemOpenBrackets)
            {
                // [ = decrease stroke width
                MuPDFRenderer.StrokeWidth = Math.Max(1, MuPDFRenderer.StrokeWidth - 1);
                SyncWidthState();
                e.Handled = true;
            }
            else if (e.Key == Key.OemCloseBrackets)
            {
                // ] = increase stroke width
                MuPDFRenderer.StrokeWidth = Math.Min(20, MuPDFRenderer.StrokeWidth + 1);
                SyncWidthState();
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
        InlineAnnotationTool.Select => CursorSizeAll,
        _ => CursorCross
    };

    /// <summary>
    /// Core tool-switching logic shared by button clicks and keyboard shortcuts.
    /// </summary>
    private void ApplyToolSwitch(InlineAnnotationTool tool)
    {
        if (MuPDFRenderer.ActiveTool is InlineAnnotationTool.MeasureDistance && tool != MuPDFRenderer.ActiveTool)
        {
            MuPDFRenderer.CancelStroke();
            _inkDrawing = false;
        }
        if (_arrowTextOrigin != null && tool != InlineAnnotationTool.ArrowText)
        {
            _arrowTextOrigin = null;
            MuPDFRenderer.ClearArrowTextPreview();
        }

        MuPDFRenderer.ActiveTool = tool;
        MuPDFRenderer.ClearEraserHover();
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
            MuPDFRenderer.IsHighlighterMode = false;
            MuPDFRenderer.StrokeOpacity = 1.0;
            if (MuPDFRenderer.StrokeWidth >= 12)
                MuPDFRenderer.StrokeWidth = _normalStrokeWidth;
        }

        MuPDFRenderer.Cursor = GetToolCursor(tool);
        SetActiveToolButton(FindToolbarButtonByTag(tool.ToString()));
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
                }
                break;
            }

            case InlineAnnotationTool.Rectangle:
            case InlineAnnotationTool.Ellipse:
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
                _inkDrawing = true;
                MuPDFRenderer.BeginShape(pdfPoint.Value);
                break;

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
            // Eraser hover highlight: track what's under the cursor
            if (_annotateMode && MuPDFRenderer.ActiveTool == InlineAnnotationTool.Eraser)
            {
                var hoverPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
                if (hoverPdf.HasValue)
                    MuPDFRenderer.UpdateEraserHover(hoverPdf.Value);
                else
                    MuPDFRenderer.ClearEraserHover();
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
                _ => Color.FromRgb(214, 64, 69)
            };
            MuPDFRenderer.StrokeColor = color;
            MuPDFRenderer.StrokeOpacity = 1.0;
            MuPDFRenderer.IsHighlighterMode = false;

            // Selecting a solid color implies pen mode if currently on highlight
            if (MuPDFRenderer.ActiveTool == InlineAnnotationTool.Highlight)
                MuPDFRenderer.ActiveTool = InlineAnnotationTool.Draw;

            if (MuPDFRenderer.ActiveLayer != null)
                MuPDFRenderer.ActiveLayer.Color = color;

            SetActiveColorButton(btn);
        }
    }

    private void OnAnnotateHighlight(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            var color = colorName switch
            {
                "Yellow" => Color.FromRgb(229, 168, 32),
                "Pink" => Color.FromRgb(224, 104, 128),
                "Cyan" => Color.FromRgb(64, 176, 208),
                _ => Color.FromRgb(229, 168, 32)
            };
            MuPDFRenderer.StrokeColor = color;
            MuPDFRenderer.StrokeWidth = 12;
            MuPDFRenderer.StrokeOpacity = 0.35;
            MuPDFRenderer.IsHighlighterMode = true;
            MuPDFRenderer.ActiveTool = InlineAnnotationTool.Highlight;

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
            MuPDFRenderer.StrokeWidth = w;
            _normalStrokeWidth = w;
            // Switching width implies pen mode
            MuPDFRenderer.StrokeOpacity = 1.0;
            MuPDFRenderer.IsHighlighterMode = false;

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

    private void OnAnnotateClear(object sender, RoutedEventArgs e) => MuPDFRenderer.ClearPage();

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
            MuPDFRenderer.StrokeOpacity = 1.0;
            MuPDFRenderer.IsHighlighterMode = false;
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

    private void ShowTextInput(Point pdfPoint, Point screenPos, Point? arrowOrigin = null)
    {
        _textPlacementPdfPoint = pdfPoint;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = arrowOrigin;
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

    #region Annotation Save / Copy

    /// <summary>
    /// Renders the current PDF page with ink strokes composited on top.
    /// Returns the PNG-encoded data, or null if nothing can be rendered.
    /// </summary>
    private SKData? RenderAnnotatedPage(double renderZoom = 2.0)
    {
        if (pwr?.MainPreviewFile == null || pwr.Pagecount <= 0) return null;
        int page = pwr.CurrentPage1;
        if (page < 0 || page >= pwr.Pagecount) return null;

        using var ms = new MemoryStream();
        pwr.MainPreviewFile.WriteImage(page, renderZoom, PixelFormats.RGBA,
            ms, RasterOutputFileTypes.PNG, true);
        ms.Position = 0;
        using var pageBitmap = SKBitmap.Decode(ms);
        if (pageBitmap == null) return null;

        using var surface = SKSurface.Create(new SKImageInfo(pageBitmap.Width, pageBitmap.Height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(pageBitmap, 0, 0);

        var strokes = MuPDFRenderer.GetStrokes(page);
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count < 2) continue;

            using var paint = new SKPaint
            {
                Color = stroke.Opacity < 1.0
                    ? new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, (byte)(stroke.Opacity * 255))
                    : new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A),
                StrokeWidth = (float)(stroke.Width * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = stroke.IsHighlighter ? SKStrokeCap.Square : SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };

            var pts = stroke.Points;
            var path = new SKPath();
            path.MoveTo((float)(pts[0].X * renderZoom), (float)(pts[0].Y * renderZoom));

            if (pts.Count == 2)
            {
                path.LineTo((float)(pts[1].X * renderZoom), (float)(pts[1].Y * renderZoom));
            }
            else
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var pm1 = pts[Math.Max(i - 1, 0)];
                    var pi  = pts[i];
                    var pi1 = pts[i + 1];
                    var pi2 = pts[Math.Min(i + 2, pts.Count - 1)];

                    float cp1x = (float)((pi.X + (pi1.X - pm1.X) / 6.0) * renderZoom);
                    float cp1y = (float)((pi.Y + (pi1.Y - pm1.Y) / 6.0) * renderZoom);
                    float cp2x = (float)((pi1.X - (pi2.X - pi.X) / 6.0) * renderZoom);
                    float cp2y = (float)((pi1.Y - (pi2.Y - pi.Y) / 6.0) * renderZoom);
                    float ex   = (float)(pi1.X * renderZoom);
                    float ey   = (float)(pi1.Y * renderZoom);

                    path.CubicTo(cp1x, cp1y, cp2x, cp2y, ex, ey);
                }
            }
            canvas.DrawPath(path, paint);
        }

        // Render shapes
        var shapes = MuPDFRenderer.GetShapes(page);
        foreach (var shape in shapes)
        {
            using var paint = new SKPaint
            {
                Color = shape.Opacity < 1.0
                    ? new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, (byte)(shape.Opacity * 255))
                    : new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, shape.Color.A),
                StrokeWidth = (float)(shape.StrokeWidth * renderZoom),
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };

            float sx = (float)(shape.Start.X * renderZoom);
            float sy = (float)(shape.Start.Y * renderZoom);
            float ex = (float)(shape.End.X * renderZoom);
            float ey = (float)(shape.End.Y * renderZoom);

            switch (shape.ShapeType)
            {
                case InlineAnnotationTool.Line:
                    canvas.DrawLine(sx, sy, ex, ey, paint);
                    break;

                case InlineAnnotationTool.Arrow:
                    canvas.DrawLine(sx, sy, ex, ey, paint);
                    RenderSkiaArrowhead(canvas, paint, sx, sy, ex, ey, renderZoom);
                    break;

                case InlineAnnotationTool.Rectangle:
                    canvas.DrawRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                    Math.Abs(ex - sx), Math.Abs(ey - sy), paint);
                    break;

                case InlineAnnotationTool.Ellipse:
                    canvas.DrawOval(new SKRect(Math.Min(sx, ex), Math.Min(sy, ey),
                                               Math.Max(sx, ex), Math.Max(sy, ey)), paint);
                    break;
            }
        }

        // Render text annotations with frame
        var texts = MuPDFRenderer.GetTexts(page);
        foreach (var t in texts)
        {
            byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;

            // Render arrow line for ArrowText annotations
            if (t.ArrowOrigin.HasValue)
            {
                float ax = (float)(t.ArrowOrigin.Value.X * renderZoom);
                float ay = (float)(t.ArrowOrigin.Value.Y * renderZoom);
                float atx = (float)(t.Position.X * renderZoom);
                float aty = (float)(t.Position.Y * renderZoom);
                using var arrowLinePaint = new SKPaint
                {
                    Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha),
                    StrokeWidth = (float)(1 * renderZoom),
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true
                };
                canvas.DrawLine(ax, ay, atx, aty, arrowLinePaint);
                RenderSkiaArrowhead(canvas, arrowLinePaint, atx, aty, ax, ay, renderZoom);
            }

            using var font = new SKFont(SKTypeface.Default, (float)(t.FontSize * renderZoom));
            using var textPaint = new SKPaint { Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha), IsAntialias = true };
            using var bgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 220), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var framePaint = new SKPaint { Color = new SKColor(t.Color.R, t.Color.G, t.Color.B, 140), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };

            float tx = (float)(t.Position.X * renderZoom);
            float lineHeight = (float)(t.FontSize * renderZoom * 1.3);
            float ty = (float)(t.Position.Y * renderZoom) + (float)(t.FontSize * renderZoom);

            // Measure total extent for frame
            float frameMinX = float.MaxValue, frameMinY = float.MaxValue;
            float frameMaxX = float.MinValue, frameMaxY = float.MinValue;
            var lineInfos = new List<(string text, float y, SKRect bounds)>();
            float curY = ty;
            foreach (var line in t.Text.Split('\n'))
            {
                if (line.Length > 0)
                {
                    font.MeasureText(line, out var tb);
                    lineInfos.Add((line, curY, tb));
                    frameMinX = Math.Min(frameMinX, tx + tb.Left);
                    frameMinY = Math.Min(frameMinY, curY + tb.Top);
                    frameMaxX = Math.Max(frameMaxX, tx + tb.Left + tb.Width);
                    frameMaxY = Math.Max(frameMaxY, curY + tb.Top + tb.Height);
                }
                curY += lineHeight;
            }

            if (lineInfos.Count > 0)
            {
                float pad = 5;
                var frameRect = new SKRoundRect(new SKRect(frameMinX - pad, frameMinY - pad, frameMaxX + pad, frameMaxY + pad), 4, 4);
                canvas.DrawRoundRect(frameRect, bgPaint);
                canvas.DrawRoundRect(frameRect, framePaint);

                foreach (var (text, y, _) in lineInfos)
                    canvas.DrawText(text, tx, y, font, textPaint);
            }
        }

        // Render measurements
        var measurements = MuPDFRenderer.GetMeasurements(page);
        foreach (var m in measurements)
        {
            using var mPaint = new SKPaint
            {
                Color = new SKColor(m.Color.R, m.Color.G, m.Color.B, 220),
                StrokeWidth = (float)(1.5 * renderZoom),
                Style = SKPaintStyle.Stroke,
                PathEffect = SKPathEffect.CreateDash([8f * (float)renderZoom, 6f * (float)renderZoom], 0),
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            var pts = m.Points;
            if (pts.Count >= 2)
            {
                float x0 = (float)(pts[0].X * renderZoom), y0 = (float)(pts[0].Y * renderZoom);
                float x1 = (float)(pts[1].X * renderZoom), y1 = (float)(pts[1].Y * renderZoom);
                canvas.DrawLine(x0, y0, x1, y1, mPaint);

                // End-marks: perpendicular ticks at both endpoints
                float emLen = (float)(6 * renderZoom);
                float ddx = x1 - x0, ddy = y1 - y0;
                float dlen = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (dlen > 1)
                {
                    float nx = -ddy / dlen * emLen, ny = ddx / dlen * emLen;
                    using var emPaint = new SKPaint { Color = mPaint.Color, StrokeWidth = mPaint.StrokeWidth, Style = SKPaintStyle.Stroke, IsAntialias = true };
                    canvas.DrawLine(x0 - nx, y0 - ny, x0 + nx, y0 + ny, emPaint);
                    canvas.DrawLine(x1 - nx, y1 - ny, x1 + nx, y1 + ny, emPaint);
                }
            }

            // Draw label
            var labelPos = m.GetLabelPosition();
            float lx = (float)(labelPos.X * renderZoom), ly = (float)(labelPos.Y * renderZoom);
            using var labelFont = new SKFont(SKTypeface.Default, (float)(10 * renderZoom));
            using var labelPaint = new SKPaint { Color = new SKColor(m.Color.R, m.Color.G, m.Color.B), IsAntialias = true };
            using var labelBg = new SKPaint { Color = new SKColor(255, 255, 255, 200), Style = SKPaintStyle.Fill, IsAntialias = true };
            string label = m.GetLabel();
            float labelWidth = labelFont.MeasureText(label, out var labelBounds);
            canvas.DrawRoundRect(lx + labelBounds.Left - 3, ly + labelBounds.Top - 2,
                labelBounds.Width + 6, labelBounds.Height + 4, 3, 3, labelBg);
            canvas.DrawText(label, lx, ly, labelFont, labelPaint);
        }

        using var image = surface.Snapshot();
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    private static void RenderSkiaArrowhead(SKCanvas canvas, SKPaint paint,
                                            float fx, float fy, float tx, float ty,
                                            double renderZoom)
    {
        double dx = tx - fx;
        double dy = ty - fy;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double headLen = Math.Min(12 * renderZoom, len * 0.4);
        double headAngle = Math.PI / 6;
        double angle = Math.Atan2(dy, dx);

        using var arrowPaint = new SKPaint
        {
            Color = paint.Color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        var path = new SKPath();
        float lx = (float)(tx - headLen * Math.Cos(angle - headAngle));
        float ly = (float)(ty - headLen * Math.Sin(angle - headAngle));
        float rx = (float)(tx - headLen * Math.Cos(angle + headAngle));
        float ry = (float)(ty - headLen * Math.Sin(angle + headAngle));
        path.MoveTo(lx, ly);
        path.LineTo(tx, ty);
        path.LineTo(rx, ry);
        path.Close();
        canvas.DrawPath(path, arrowPaint);
    }

    private void OnAnnotateSave(object sender, RoutedEventArgs e)
    {
        using var data = RenderAnnotatedPage();
        if (data == null) return;

        int page = pwr.CurrentPage1;
        Directory.CreateDirectory(Path.Combine(MainViewModel.SavePath, "Annotations"));
        string filePath = Path.Combine(MainViewModel.SavePath, "Annotations",
            $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_page{page + 1}.png");
        using var fs = File.OpenWrite(filePath);
        data.SaveTo(fs);
        pwr.StatusMessage = $"Saved to {Path.GetFileName(filePath)}";
    }

    private async void OnAnnotateCopy(object sender, RoutedEventArgs e)
    {
        using var data = RenderAnnotatedPage();
        if (data == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        // Write to a temp file and place it on the clipboard as a file drop.
        // This is the most reliable way to paste images into Word, Teams, etc.
        string tempPath = Path.Combine(Path.GetTempPath(), "finn_annotation.png");
        using (var fs = File.Create(tempPath))
            data.SaveTo(fs);

        var storageFile = await topLevel.StorageProvider
            .TryGetFileFromPathAsync(new Uri("file:///" + tempPath.Replace('\\', '/')));
        if (storageFile == null) return;

        var dataObject = new DataObject();
        dataObject.Set(DataFormats.Files, new[] { storageFile });
        await topLevel.Clipboard.SetDataObjectAsync(dataObject);
        pwr.StatusMessage = "Annotation copied to clipboard";
    }

    #endregion
}