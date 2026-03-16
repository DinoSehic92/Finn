using Finn.ViewModels;
using Finn.Model;
using Finn.Controls;
using Finn.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
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
    private PDFRenderer? _panRenderer;

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
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DeactivateAnnotateMode();
                // Always sync layers on file switch — CurrentPage1 may not
                // change if both files share the same page number.
                SyncLayers();
                SyncDiffOverlay();
            });
        }

        if (e.PropertyName == "CurrentPage1")
        {
            DeselectAnnotation();
            MuPDFRenderer.SetStrokePage(pwr.CurrentPage1);
            SyncLayers();
            SyncDiffOverlay();
        }

        if (e.PropertyName == "DiffOverlayActive" || e.PropertyName == "DiffViewMode")
        {
            SyncDiffOverlay();
        }

        if (e.PropertyName == "DiffSplitPosition")
        {
            if (pwr.DiffOverlayActive && pwr.DiffViewMode == DiffViewMode.Slider)
            {
                MuPDFRenderer.SliderSplitPosition = pwr.DiffSplitPosition;
                MuPDFRenderer.InvalidateVisual();
            }
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
    /// Only calls SetLayers when the collection instance actually changes,
    /// so page navigation within the same file preserves the undo stack.
    /// </summary>
    private void SyncLayers()
    {
        if (pwr == null || pwr.WhiteboardMode) return;
        var layers = pwr.CurrentFile?.AnnotationLayers;
        if (layers != MuPDFRenderer.Layers)
            MuPDFRenderer.SetLayers(layers);
    }

    /// <summary>
    /// Updates the diff display on the renderer for the current page and view mode.
    /// Overlay: red diff highlights. Slider: A/B wipe. SideBySide: dual-page.
    /// </summary>
    private void SyncDiffOverlay()
    {
        if (pwr == null)
        {
            MuPDFRenderer.ClearDiffOverlay();
            MuPDFRenderer.ClearSliderWipe();
            return;
        }

        if (!pwr.DiffOverlayActive)
        {
            MuPDFRenderer.ClearDiffOverlay();
            MuPDFRenderer.ClearSliderWipe();
            // Close side-by-side if it was open for diff
            if (_diffSideBySideOpen)
            {
                _diffSideBySideOpen = false;
                _ = pwr.CloseDiffSideBySideAsync();
            }
            return;
        }

        int page = pwr.CurrentPage1;
        switch (pwr.DiffViewMode)
        {
            case DiffViewMode.Overlay:
                MuPDFRenderer.ClearSliderWipe();
                if (_diffSideBySideOpen) { _diffSideBySideOpen = false; _ = pwr.CloseDiffSideBySideAsync(); }
                var diffPath = pwr.GetDiffImagePath(page);
                if (diffPath != null)
                    MuPDFRenderer.SetDiffOverlay(diffPath, page, PdfDiffService.ZOOM);
                else
                    MuPDFRenderer.ClearDiffOverlay();
                break;

            case DiffViewMode.Slider:
                MuPDFRenderer.ClearDiffOverlay();
                if (_diffSideBySideOpen) { _diffSideBySideOpen = false; _ = pwr.CloseDiffSideBySideAsync(); }
                var origPath = pwr.GetOriginalImagePath(page);
                var revPath = pwr.GetRevisedImagePath(page);
                if (origPath != null && revPath != null)
                {
                    MuPDFRenderer.SliderSplitPosition = pwr.DiffSplitPosition;
                    MuPDFRenderer.SetSliderWipe(origPath, revPath, page, PdfDiffService.ZOOM);
                }
                else
                    MuPDFRenderer.ClearSliderWipe();
                break;

            case DiffViewMode.SideBySide:
                MuPDFRenderer.ClearDiffOverlay();
                MuPDFRenderer.ClearSliderWipe();
                if (!_diffSideBySideOpen && pwr.DiffOriginalPdfPath != null)
                {
                    _diffSideBySideOpen = true;
                    _ = pwr.OpenDiffSideBySideAsync();
                }
                break;
        }
    }

    private bool _diffSideBySideOpen;


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
        MuPDFRenderer.RemoveHandler(PointerMovedEvent, OnRendererPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRenderer.RemoveHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);
        MuPDFRendererSecondary.RemoveHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRendererSecondary.RemoveHandler(PointerMovedEvent, OnRendererPointerMoved);
        MuPDFRendererSecondary.RemoveHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRendererSecondary.RemoveHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);

        MuPDFRenderer.AddHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRenderer.AddHandler(PointerMovedEvent, OnRendererPointerMoved);
        MuPDFRenderer.AddHandler(PointerReleasedEvent, OnRendererPointerReleased);
        MuPDFRenderer.AddHandler(PointerCaptureLostEvent, OnRendererPointerCaptureLost);
        MuPDFRendererSecondary.AddHandler(PointerPressedEvent, OnRendererPointerPressed);
        MuPDFRendererSecondary.AddHandler(PointerMovedEvent, OnRendererPointerMoved);
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
    {
        _rendererPointerDown = true;
        if (_annotateMode) return;

        var point = e.GetCurrentPoint((Visual)sender!);
        if (point.Properties.IsMiddleButtonPressed && sender is PDFRenderer renderer)
        {
            _middlePanning = true;
            _panRenderer = renderer;
            _panStart = e.GetPosition(renderer);
            _panStartDisplayArea = renderer.DisplayArea;
            e.Pointer.Capture(renderer);
            e.Handled = true;
        }
    }

    private void OnRendererPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_middlePanning || _panRenderer == null) return;
        e.Handled = true;
        var current = e.GetPosition(_panRenderer);
        var da = _panStartDisplayArea;
        var bounds = _panRenderer.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        double dx = (_panStart.X - current.X) / bounds.Width  * da.Width;
        double dy = (_panStart.Y - current.Y) / bounds.Height * da.Height;
        _panRenderer.SetDisplayAreaNow(new Rect(da.X + dx, da.Y + dy, da.Width, da.Height));
    }

    private void OnRendererPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _rendererPointerDown = false;
        if (_middlePanning && !_annotateMode)
        {
            _middlePanning = false;
            _panRenderer = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnRendererPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _rendererPointerDown = false;
        if (_middlePanning && !_annotateMode)
        {
            _middlePanning = false;
            _panRenderer = null;
        }
    }

    private void OnDiffCycleMode(object? sender, RoutedEventArgs e) => pwr?.CycleDiffViewMode();
}