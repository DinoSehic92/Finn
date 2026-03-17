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
                // Synchronously close diff mode — release secondary renderer
                // and reset DualFileMode BEFORE the new file load proceeds.
                if (pwr.DiffOverlayActive || _diffToggleOpen || _diffSideBySideOpen)
                {
                    if (_diffToggleOpen) { _diffToggleOpen = false; CloseDiffToggle(); }
                    if (_diffSideBySideOpen) { _diffSideBySideOpen = false; StopDisplayAreaSync(); }
                    pwr.CloseDiffModeSync();
                }
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

        if (e.PropertyName == nameof(PreviewViewModel.DiffShowingOriginal) && _diffToggleOpen)
            ShowToggleRenderer(pwr.DiffShowingOriginal);

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
    /// Overlay: red diff highlights. Toggle: A/B document swap. SideBySide: dual-page.
    /// </summary>
    private void SyncDiffOverlay()
    {
        if (pwr == null)
        {
            MuPDFRenderer.ClearDiffOverlay();
            return;
        }

        if (!pwr.DiffOverlayActive)
        {
            MuPDFRenderer.ClearDiffOverlay();
            if (_diffToggleOpen) { _diffToggleOpen = false; CloseDiffToggle(); }
            if (_diffSideBySideOpen)
            {
                _diffSideBySideOpen = false;
                StopDisplayAreaSync();
                _ = pwr.CloseDiffSideBySideAsync();
            }
            return;
        }

        int page = pwr.CurrentPage1;
        switch (pwr.DiffViewMode)
        {
            case DiffViewMode.Overlay:
                if (_diffToggleOpen) { _diffToggleOpen = false; CloseDiffToggle(); }
                if (_diffSideBySideOpen) { _diffSideBySideOpen = false; StopDisplayAreaSync(); _ = pwr.CloseDiffSideBySideAsync(); }
                var diffPath = pwr.GetDiffImagePath(page);
                if (diffPath != null)
                    MuPDFRenderer.SetDiffOverlay(diffPath, page, PdfDiffService.ZOOM);
                else
                    MuPDFRenderer.ClearDiffOverlay();
                break;

            case DiffViewMode.Toggle:
                MuPDFRenderer.ClearDiffOverlay();
                if (_diffSideBySideOpen)
                {
                    _diffSideBySideOpen = false;
                    StopDisplayAreaSync();
                    // Collapse the SBS layout without disposing the document, then
                    // chain the toggle open so it reuses the already-loaded doc.
                    _ = pwr.CollapseSecondaryLayoutAsync().ContinueWith(_ =>
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (pwr.DiffViewMode == DiffViewMode.Toggle && !_diffToggleOpen)
                            {
                                _diffToggleOpen = true;
                                _ = OpenDiffToggleAsync();
                            }
                        }),
                        System.Threading.Tasks.TaskScheduler.Default);
                }
                else if (!_diffToggleOpen)
                {
                    _diffToggleOpen = true;
                    _ = OpenDiffToggleAsync();
                }
                break;

            case DiffViewMode.SideBySide:
                MuPDFRenderer.ClearDiffOverlay();
                if (_diffToggleOpen)
                {
                    _diffToggleOpen = false;
                    // Reset toggle visual state only — do NOT dispose the document.
                    // OpenDiffSideBySideAsync will reuse the already-loaded doc.
                    StopDisplayAreaSync();
                    pwr.DiffShowingOriginal = false;
                    MuPDFRenderer.Opacity = 1;
                    MuPDFRenderer.IsHitTestVisible = true;
                    MuPDFRendererSecondary.Opacity = 1;
                    MuPDFRendererSecondary.IsHitTestVisible = true;
                    MuPDFRendererSecondary.IsVisible = false;
                    Avalonia.Controls.Grid.SetColumn(MuPDFRendererSecondary, 2);
                    // Cancel any pending OpenDiffToggleAsync without disposing the doc.
                    pwr.CancelSecondaryOpen();
                }
                if (!_diffSideBySideOpen && pwr.DiffOriginalPdfPath != null)
                {
                    _diffSideBySideOpen = true;
                    _ = pwr.OpenDiffSideBySideAsync().ContinueWith(_ =>
                        Avalonia.Threading.Dispatcher.UIThread.Post(StartDisplayAreaSync),
                        System.Threading.Tasks.TaskScheduler.Default);
                }
                break;
        }
    }

    private bool _diffSideBySideOpen;
    private bool _diffToggleOpen;
    private bool _syncingDisplayArea; // re-entrancy guard for display area sync
    private IDisposable? _mainDisplayAreaSub;
    private IDisposable? _secondaryDisplayAreaSub;

    /// <summary>
    /// Subscribes to DisplayArea changes on both renderers so that pan/zoom
    /// on one is mirrored to the other (used in Toggle and SideBySide modes).
    /// </summary>
    private void StartDisplayAreaSync()
    {
        StopDisplayAreaSync();
        _mainDisplayAreaSub = MuPDFRenderer.GetObservable(PDFRenderer.DisplayAreaProperty)
            .Subscribe(new DisplayAreaObserver(() =>
            {
                if (_syncingDisplayArea) return;
                if (!IsDiffSyncActive()) return;
                _syncingDisplayArea = true;
                try { MuPDFRendererSecondary.SetDisplayAreaNow(MuPDFRenderer.DisplayArea); }
                finally { _syncingDisplayArea = false; }
            }));
        _secondaryDisplayAreaSub = MuPDFRendererSecondary.GetObservable(PDFRenderer.DisplayAreaProperty)
            .Subscribe(new DisplayAreaObserver(() =>
            {
                if (_syncingDisplayArea) return;
                if (!IsDiffSyncActive()) return;
                _syncingDisplayArea = true;
                try { MuPDFRenderer.SetDisplayAreaNow(MuPDFRendererSecondary.DisplayArea); }
                finally { _syncingDisplayArea = false; }
            }));
    }

    private void StopDisplayAreaSync()
    {
        _mainDisplayAreaSub?.Dispose();
        _mainDisplayAreaSub = null;
        _secondaryDisplayAreaSub?.Dispose();
        _secondaryDisplayAreaSub = null;
    }

    /// <summary>True when diff toggle or side-by-side is open and sync should be active.</summary>
    private bool IsDiffSyncActive() => _diffToggleOpen || _diffSideBySideOpen;

    /// <summary>Simple IObserver that invokes an action on each value.</summary>
    private sealed class DisplayAreaObserver(Action onNext) : IObserver<Rect>
    {
        public void OnNext(Rect value) => onNext();
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

    /// <summary>
    /// Opens A/B toggle mode: moves the secondary renderer into the same
    /// grid cell as the main renderer (column 0) so they overlap, then
    /// loads the original PDF. Only one renderer is visible at a time.
    /// Uses Opacity instead of IsVisible so both renderers participate in
    /// layout (get proper bounds) — IsVisible=false gives 0 bounds which
    /// prevents the PDFRenderer from creating properly-sized bitmaps.
    /// </summary>
    private async Task OpenDiffToggleAsync()
    {
        // Place secondary on top of main — same column, same size.
        // Keep IsVisible=true so it participates in layout; hide with Opacity.
        Avalonia.Controls.Grid.SetColumn(MuPDFRendererSecondary, 0);
        MuPDFRendererSecondary.IsVisible = true;
        MuPDFRendererSecondary.Opacity = 0;
        MuPDFRendererSecondary.IsHitTestVisible = false;

        await pwr.OpenDiffToggleAsync();

        // After the document is loaded, ensure correct state (show B first).
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            pwr.DiffShowingOriginal = false;
            ShowToggleRenderer(showOriginal: false);
            MuPDFRendererSecondary.Contain();
            StartDisplayAreaSync();
        });
    }

    /// <summary>Closes A/B toggle mode: restores renderer positions and visibility.</summary>
    private void CloseDiffToggle()
    {
        StopDisplayAreaSync();
        pwr.DiffShowingOriginal = false;
        // Restore both renderers to normal state (full opacity, interactive)
        // so side-by-side or dual-page mode can use the secondary cleanly.
        MuPDFRenderer.Opacity = 1;
        MuPDFRenderer.IsHitTestVisible = true;
        MuPDFRendererSecondary.Opacity = 1;
        MuPDFRendererSecondary.IsHitTestVisible = true;
        MuPDFRendererSecondary.IsVisible = false;
        // Move secondary back to its normal column for side-by-side mode
        Avalonia.Controls.Grid.SetColumn(MuPDFRendererSecondary, 2);
        _ = pwr.CloseDiffToggleAsync();
    }

    /// <summary>Swaps which renderer is visible using Opacity (both stay in layout).</summary>
    private void ShowToggleRenderer(bool showOriginal)
    {
        MuPDFRenderer.Opacity = showOriginal ? 0 : 1;
        MuPDFRenderer.IsHitTestVisible = !showOriginal;
        MuPDFRendererSecondary.Opacity = showOriginal ? 1 : 0;
        MuPDFRendererSecondary.IsHitTestVisible = showOriginal;
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
        if (pwr == null) return;
        int page = (int)ScrollSlider.Value - 1;
        if (page != pwr.RequestPage1)
            pwr.RequestPage1 = page;
    }

    private void SecondaryPageNrSlider(object sender, RoutedEventArgs e)
    {
        if (pwr == null) return;
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


    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResetView(null, null);
    }

    private void ResetView(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.Contain();
        if (MuPDFRendererSecondary.Bounds.Width > 0 && MuPDFRendererSecondary.Bounds.Height > 0)
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

    private void OnDiffSaveAsLayer(object? sender, RoutedEventArgs e)
    {
        if (pwr == null) return;
        var layer = pwr.CreateDiffAnnotationLayer(pwr.DiffOriginalPdfPath);
        if (layer != null)
        {
            SyncLayers();
            MuPDFRenderer.InvalidateVisual();
            pwr.StatusMessage = $"Diff saved as layer: {layer.Name}";
            ctx?.MarkDirty();
        }
        else
        {
            pwr.StatusMessage = "No diff results to save";
        }
    }

    private async void OnDiffRerun(object? sender, RoutedEventArgs e)
    {
        if (pwr == null || !pwr.CanRerunDiff) return;
        await pwr.RerunDiffWithToleranceAsync();
        if (pwr.DiffViewMode != DiffViewMode.Overlay) return;
        // Force-reload the overlay since the diff image on disk changed.
        // If this page is now clean (tolerance absorbed all differences), clear the overlay.
        int page = pwr.CurrentPage1;
        var diffPath = pwr.GetDiffImagePath(page);
        if (diffPath != null)
            MuPDFRenderer.SetDiffOverlay(diffPath, page, PdfDiffService.ZOOM, forceReload: true);
        else
            MuPDFRenderer.ClearDiffOverlay();
    }

    private async void OnDiffCompareVersions(object? sender, RoutedEventArgs e)
    {
        if (pwr == null || pwr.DiffChoiceA == null || pwr.DiffChoiceB == null) return;
        if (string.Equals(pwr.DiffChoiceA.Path, pwr.DiffChoiceB.Path, StringComparison.OrdinalIgnoreCase))
        {
            pwr.StatusMessage = "A and B are the same — select different versions";
            return;
        }
        // Close current diff mode views before re-running
        if (_diffToggleOpen) { _diffToggleOpen = false; CloseDiffToggle(); }
        if (_diffSideBySideOpen) { _diffSideBySideOpen = false; StopDisplayAreaSync(); }
        MuPDFRenderer.ClearDiffOverlay();

        await pwr.CompareSelectedPathsAsync();
        SyncDiffOverlay();
    }

    /// <summary>Opens the version comparison dialog.</summary>
    private async void OnOpenVersionCompareDialog(object? sender, RoutedEventArgs e)
    {
        if (pwr == null) return;

        // Build choices from the real file (via MainViewModel.CurrentFile)
        var mainVm = this.DataContext as Finn.ViewModels.MainViewModel
                  ?? (this.FindAncestorOfType<Window>()?.DataContext as Finn.ViewModels.MainViewModel);
        var choices = pwr.GetVersionChoicesForDialog(mainVm?.CurrentFile);
        if (choices.Count < 2)
        {
            pwr.StatusMessage = "Not enough versions to compare";
            return;
        }

        var dialog = new Finn.Dialogs.xVersionCompareDia();
        dialog.Populate(choices, pwr.DiffChoiceA, pwr.DiffChoiceB);

        var owner = this.FindAncestorOfType<Window>();
        if (owner != null)
        {
            dialog.FontFamily = owner.FontFamily;
            dialog.RequestedThemeVariant = owner.ActualThemeVariant;
            dialog.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner;
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
            return;
        }

        if (!dialog.Confirmed || dialog.ChoiceA == null || dialog.ChoiceB == null) return;

        pwr.DiffChoiceA = dialog.ChoiceA;
        pwr.DiffChoiceB = dialog.ChoiceB;

        // Close current diff mode views before re-running
        if (_diffToggleOpen) { _diffToggleOpen = false; CloseDiffToggle(); }
        if (_diffSideBySideOpen) { _diffSideBySideOpen = false; StopDisplayAreaSync(); }
        MuPDFRenderer.ClearDiffOverlay();

        // Store the source file so version data persists
        if (mainVm?.CurrentFile != null)
            pwr.DiffSourceFile = mainVm.CurrentFile;

        await pwr.CompareSelectedPathsAsync();
        SyncDiffOverlay();
    }
}