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
using MuPDFCore.MuPDFRenderer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

        // Centralized keyboard shortcuts — replaces per-button HotKey attributes
        // so every shortcut respects CanExecute guards and mode state.
        this.AddHandler(KeyDownEvent, OnPreviewShortcutKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        this.AddHandler(LoadedEvent, InitSetup);
    }

    private MainViewModel ctx = null!;
    private PreviewViewModel pwr = null!;
    private bool _suppressWheelPageChange = false;
    private bool ZoomMode = false;
    private PDFRenderer? _panRenderer;

    // Pan state — shared between PreView.axaml.cs and PreView.Annotation.cs
    private bool _middlePanning;
    private Point _panStart;
    private Rect _panStartDisplayArea;

    private void InitSetup(object sender, RoutedEventArgs e)
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

    private void OnBindingPwr(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "SearchMode":
                SetSearchFocus();
                break;

            case "CurrentFile" when !pwr.WhiteboardMode:
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    DeactivateAnnotateMode();
                    if (pwr.DiffOverlayActive || _diffToggleOpen || _diffSideBySideOpen)
                    {
                        CloseDiffViews();
                        pwr.CloseDiffModeSync();
                    }
                    SyncLayers();
                    SyncDiffOverlay();
                });
                break;

            case "CurrentPage1":
                DeselectAnnotation();
                MuPDFRenderer.SetStrokePage(pwr.CurrentPage1);
                SyncLayers();
                SyncDiffOverlay();
                if (_annotateMode) UpdateUndoRedoButtons();
                break;

            case "DiffOverlayActive":
            case "DiffViewMode":
                SyncDiffOverlay();
                break;

            case nameof(PreviewViewModel.DiffShowingOriginal) when _diffToggleOpen:
                ShowToggleRenderer(pwr.DiffShowingOriginal);
                DiffABLabel.Text = pwr.DiffShowingOriginal ? "A" : "B";
                break;

            case "WhiteboardMode":
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
                break;
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
    /// Closes any active diff view mode (Toggle or SideBySide) at the view layer.
    /// Resets renderer state (opacity, column, display-area sync) so the next
    /// mode transition starts from a clean slate.
    /// Awaitable so callers can ensure cleanup finishes before opening a new mode.
    /// </summary>
    private async Task CloseDiffViewsAsync()
    {
        if (_diffToggleOpen)
        {
            _diffToggleOpen = false;
            await CloseDiffToggleAsync();
        }
        if (_diffSideBySideOpen)
        {
            _diffSideBySideOpen = false;
            StopDisplayAreaSync();
            await pwr.CloseDiffSideBySideAsync();
        }
    }

    /// <summary>Synchronous overload — only use when the caller cannot await (e.g., PropertyChanged handler).
    /// Fires disposal as fire-and-forget; prefer CloseDiffViewsAsync when possible.</summary>
    private void CloseDiffViews()
    {
        if (_diffToggleOpen)
        {
            _diffToggleOpen = false;
            CloseDiffToggleSync();
            _ = pwr.CloseDiffToggleAsync();
        }
        if (_diffSideBySideOpen)
        {
            _diffSideBySideOpen = false;
            StopDisplayAreaSync();
            _ = pwr.CloseDiffSideBySideAsync();
        }
    }

    /// <summary>
    /// Updates the diff display on the renderer for the current page and view mode.
    /// Overlay: red diff highlights. Toggle: A/B document swap. SideBySide: dual-page.
    /// Optimised: Toggle↔SBS transitions reuse the already-loaded secondary document
    /// via lightweight layout-only reconfiguration instead of dispose+recreate.
    /// </summary>
    private async void SyncDiffOverlay()
    {
        if (pwr == null)
        {
            MuPDFRenderer.ClearDiffOverlay();
            return;
        }

        if (!pwr.DiffOverlayActive)
        {
            MuPDFRenderer.ClearDiffOverlay();
            await CloseDiffViewsAsync();
            return;
        }

        int page = pwr.CurrentPage1;
        switch (pwr.DiffViewMode)
        {
            case DiffViewMode.Overlay:
                await CloseDiffViewsAsync();
                var diffPath = pwr.HasDiffResults ? pwr.GetDiffImagePath(page) : null;
                if (diffPath != null)
                    MuPDFRenderer.SetDiffOverlay(diffPath, page, PdfDiffService.ZOOM);
                else
                    MuPDFRenderer.ClearDiffOverlay();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => MuPDFRenderer.Contain(), Avalonia.Threading.DispatcherPriority.Render);
                break;

            case DiffViewMode.Toggle:
                MuPDFRenderer.ClearDiffOverlay();
                if (_diffSideBySideOpen)
                {
                    // Fast path: SBS→Toggle — secondary document already loaded,
                    // just reconfigure layout without disposing the document.
                    _diffSideBySideOpen = false;
                    StopDisplayAreaSync();
                    await pwr.CollapseSecondaryLayoutAsync();
                    _diffToggleOpen = true;
                    await OpenDiffToggleAsync();
                    MuPDFRenderer.Contain();
                }
                else if (!_diffToggleOpen)
                {
                    _diffToggleOpen = true;
                    await OpenDiffToggleAsync();
                    MuPDFRenderer.Contain();
                }
                break;

            case DiffViewMode.SideBySide:
                MuPDFRenderer.ClearDiffOverlay();
                if (_diffToggleOpen)
                {
                    // Fast path: Toggle→SBS — secondary document already loaded,
                    // just reconfigure layout without disposing the document.
                    _diffToggleOpen = false;
                    CloseDiffToggleSync();
                    // Don't dispose — OpenDiffSideBySideAsync will reuse secondaryFile
                }
                if (!_diffSideBySideOpen && pwr.DiffOriginalPdfPath != null)
                {
                    _diffSideBySideOpen = true;
                    bool opened = await pwr.OpenDiffSideBySideAsync();
                    if (opened)
                    {
                        MuPDFRenderer.Contain();
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
                        if (MuPDFRendererSecondary.IsVisible && MuPDFRendererSecondary.Bounds is { Width: > 0, Height: > 0 })
                            MuPDFRendererSecondary.Contain();
                        StartDisplayAreaSync();
                    }
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

    /// <summary>Resets toggle renderer visuals (opacity, column, visibility) synchronously.</summary>
    private void CloseDiffToggleSync()
    {
        StopDisplayAreaSync();
        pwr.DiffShowingOriginal = false;
        MuPDFRenderer.Opacity = 1;
        MuPDFRenderer.IsHitTestVisible = true;
        MuPDFRendererSecondary.Opacity = 1;
        MuPDFRendererSecondary.IsHitTestVisible = true;
        MuPDFRendererSecondary.IsVisible = false;
        Avalonia.Controls.Grid.SetColumn(MuPDFRendererSecondary, 2);
    }

    /// <summary>Closes A/B toggle mode and awaits full secondary document disposal.</summary>
    private async Task CloseDiffToggleAsync()
    {
        CloseDiffToggleSync();
        await pwr.CloseDiffToggleAsync();
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

    /// <summary>
    /// Centralized Ctrl+ keyboard shortcut handler. Replaces per-button HotKey
    /// attributes so every shortcut respects mode guards (CanSearch, CanToggleLayout, etc.).
    /// Registered as Tunnel so it runs before the annotation handler, but only
    /// processes Ctrl+key combos that don't overlap with annotation shortcuts.
    /// </summary>
    private void OnPreviewShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (pwr == null) return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        // Annotation mode has its own Ctrl+ shortcuts (Z, Y, C, V, D, S, A).
        // Let the annotation handler take full priority.
        if (_annotateMode) return;

        // Don't intercept while a text input overlay is visible
        if (TextInputCanvas.IsVisible || CalibrationCanvas.IsVisible || ColorInputCanvas.IsVisible)
            return;

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.G when !shift:
                pwr.DarkMode = !pwr.DarkMode;
                e.Handled = true;
                break;

            case Key.D when !shift:
                if (pwr.CanToggleLayout && !pwr.DualFileMode)
                    pwr.TwopageMode = !pwr.TwopageMode;
                e.Handled = true;
                break;

            case Key.L when !shift:
                if (pwr.ShowLinkedPageButton)
                    pwr.LinkedPageMode = !pwr.LinkedPageMode;
                e.Handled = true;
                break;

            case Key.F when !shift:
                // Allow closing search even when CanSearch is false
                if (pwr.SearchMode || pwr.CanSearch)
                    pwr.SearchMode = !pwr.SearchMode;
                e.Handled = true;
                break;

            case Key.T when shift:
                if (pwr.ShowDiffToggle)
                    pwr.DiffShowingOriginal = !pwr.DiffShowingOriginal;
                e.Handled = true;
                break;
        }
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

    private void RotateRight(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation + 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    private void RotateLeft(object sender, RoutedEventArgs e)
    {
        pwr.Rotation = pwr.Rotation - 90;
        MuPDFRenderer.UpdateLayout();
        MuPDFRenderer.Contain();
    }

    private void RotateNull()
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

        if (!_suppressWheelPageChange && pwr.Pagecount > 0)
        {
            PDFRenderer currentSender = (PDFRenderer)sender;
            bool secondPage = currentSender.Name?.ToString() == "MuPDFRendererSecondary";

            if (e.Delta.Y > 0) pwr.PrevPage(secondPage);
            if (e.Delta.Y < 0) pwr.NextPage(secondPage);
        }
    }

    /// <summary>Begins a middle-mouse pan on the specified renderer.</summary>
    private void BeginPan(PDFRenderer renderer, PointerEventArgs e)
    {
        _middlePanning = true;
        _panRenderer = renderer;
        _panStart = e.GetPosition(renderer);
        _panStartDisplayArea = renderer.DisplayArea;
        e.Pointer.Capture(renderer);
        e.Handled = true;
    }

    /// <summary>Updates the pan position during a middle-mouse drag.</summary>
    private void UpdatePan(PointerEventArgs e)
    {
        if (_panRenderer == null) return;
        e.Handled = true;
        var current = e.GetPosition(_panRenderer);
        var da = _panStartDisplayArea;
        var bounds = _panRenderer.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double dx = (_panStart.X - current.X) / bounds.Width  * da.Width;
        double dy = (_panStart.Y - current.Y) / bounds.Height * da.Height;
        _panRenderer.SetDisplayAreaNow(new Rect(da.X + dx, da.Y + dy, da.Width, da.Height));
    }

    private void OnRendererPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _suppressWheelPageChange = true;
        if (_annotateMode) return;

        var point = e.GetCurrentPoint((Visual)sender!);
        if (point.Properties.IsMiddleButtonPressed && sender is PDFRenderer renderer)
            BeginPan(renderer, e);
    }

    private void OnRendererPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_middlePanning || _panRenderer == null) return;
        UpdatePan(e);
    }

    private void OnRendererPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _suppressWheelPageChange = false;
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
        _suppressWheelPageChange = false;
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
            // The layer was added to the same collection the renderer already
            // holds, so SyncLayers (reference check) won't call SetLayers.
            // Manually recount and activate the new layer so it renders.
            MuPDFRenderer.ActiveLayer = layer;
            MuPDFRenderer.NotifyLayersChanged();
            pwr.StatusMessage = $"Diff saved as layer: {layer.Name}";
            ctx?.MarkDirty();
        }
        else
        {
            pwr.StatusMessage = "No diff results to save";
        }
    }

    private void OnCancelDiff(object? sender, RoutedEventArgs e) => pwr?.CancelDiff();

    /// <summary>Close Dual File mode from the banner close button.</summary>
    private void OnCloseDualFileMode(object? sender, RoutedEventArgs e)
    {
        if (pwr != null) pwr.DualFileMode = false;
    }

    /// <summary>Close Diff comparison mode from the banner close button.</summary>
    private async void OnCloseDiffMode(object? sender, RoutedEventArgs e)
    {
        if (pwr == null) return;
        await CloseDiffViewsAsync();
        pwr.CloseDiffModeSync();
        MuPDFRenderer.ClearDiffOverlay();
        MuPDFRenderer.Contain();
    }

    // ── Diff mode radio-button Click handlers ────────────────────────
    // OneWay bindings + Click ensures exactly one is always selected.
    // Clicking the already-active button is a no-op (DiffViewMode doesn't change,
    // OneWay binding keeps it checked).

    private void OnDiffModeOverlay(object? sender, RoutedEventArgs e)
    {
        if (pwr is { HasDiffResults: true })
            pwr.DiffViewMode = DiffViewMode.Overlay;
    }

    private void OnDiffModeToggle(object? sender, RoutedEventArgs e)
    {
        if (pwr != null) pwr.DiffViewMode = DiffViewMode.Toggle;
    }

    private void OnDiffModeSBS(object? sender, RoutedEventArgs e)
    {
        if (pwr != null) pwr.DiffViewMode = DiffViewMode.SideBySide;
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
        await CloseDiffViewsAsync();
        MuPDFRenderer.ClearDiffOverlay();

        await pwr.CompareSelectedPathsAsync();
        SyncDiffOverlay();
    }

    /// <summary>
    /// Runs the slow pixel-level diff comparison from the toolbar button.
    /// The user is already in dual view — this adds overlay results.
    /// </summary>
    private async void OnRunDiffComparison(object? sender, RoutedEventArgs e)
    {
        if (pwr == null || pwr.DiffChoiceA == null || pwr.DiffChoiceB == null) return;
        if (string.Equals(pwr.DiffChoiceA.Path, pwr.DiffChoiceB.Path, StringComparison.OrdinalIgnoreCase))
        {
            pwr.StatusMessage = "A and B are the same";
            return;
        }

        // Remember current view mode — the comparison will reload diff state
        var mode = pwr.DiffViewMode;
        await CloseDiffViewsAsync();
        MuPDFRenderer.ClearDiffOverlay();

        await pwr.CompareSelectedPathsAsync();

        // Restore the view mode the user had before (e.g. SideBySide)
        pwr.DiffViewMode = mode;
        SyncDiffOverlay();
    }

    /// <summary>Opens the version comparison dialog.</summary>
    private async void OnOpenVersionCompareDialog(object? sender, RoutedEventArgs e)
    {
        if (pwr == null) return;

        // Build choices from the real file (via MainViewModel.CurrentFile)
        var mainVm = this.DataContext as Finn.ViewModels.MainViewModel
                  ?? (this.FindAncestorOfType<Window>()?.DataContext as Finn.ViewModels.MainViewModel);
        var currentFile = mainVm?.CurrentFile;
        IEnumerable<FileData>? appendedFiles = null;

        // Only include sibling/child attached files as comparison options when
        // the file itself has no version history. When versions exist, they
        // are the natural comparison targets and showing 50+ siblings would
        // overwhelm the dialog.
        if (currentFile != null && !currentFile.HasVersions
            && mainVm?.CurrentProject?.StoredFiles != null)
        {
            if (currentFile.IsAppendedFile && currentFile.ParentFile != null)
            {
                var parent = currentFile.ParentFile;
                appendedFiles = mainVm.CurrentProject.StoredFiles
                    .Where(f => f != currentFile && (f == parent || f.ParentNamn == parent.Namn));
            }
            else if (currentFile.HasChildren)
            {
                appendedFiles = mainVm.CurrentProject.StoredFiles
                    .Where(f => f.ParentNamn == currentFile.Namn);
            }
        }
        var choices = pwr.GetVersionChoicesForDialog(currentFile, appendedFiles);
        if (choices.Count < 2)
        {
            pwr.StatusMessage = "Not enough versions to compare";
            return;
        }

        var dialog = new Finn.Dialogs.xVersionCompareDia();
        // Pass MainViewModel as DataContext so UI.CornerRadius / UI.Shadow / UI.BorderThickness
        // bindings in the dialog (and the global App.axaml Border/DataGrid styles) resolve correctly.
        if (mainVm != null)
            dialog.DataContext = mainVm;
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

        // Close current diff mode views before entering new comparison
        await CloseDiffViewsAsync();
        MuPDFRenderer.ClearDiffOverlay();

        // Store the source file so version data persists
        if (mainVm?.CurrentFile != null)
            pwr.DiffSourceFile = mainVm.CurrentFile;

        // Enter dual view immediately — Toggle or SideBySide work instantly.
        // The user can run the slow pixel comparison later via the toolbar button.
        // Default to SideBySide if not already in a diff view mode.
        if (pwr.DiffViewMode == DiffViewMode.Overlay)
            pwr.DiffViewMode = DiffViewMode.SideBySide;
        pwr.EnterDiffView(dialog.ChoiceA.Path, dialog.ChoiceB.Path, pwr.DiffSourceFile);
        SyncDiffOverlay();
    }
}