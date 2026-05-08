using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using MuPDFCore.MuPDFRenderer;
using System;

namespace Finn.Views;

/// <summary>
/// Screenshot / region-capture mode.  Works in both normal view and annotation mode.
/// Activate via the screenshot button or Ctrl+Shift+S.  Drag to select a PDF region;
/// on release the region is rendered and copied to the clipboard as a PNG.
/// </summary>
public partial class PreView
{
    private bool  _screenshotMode;
    private bool  _screenshotDragging;
    private Point _screenshotStartPdf;

    // ── Activation ────────────────────────────────────────────────────────────

    private void ActivateScreenshotMode()
    {
        if (pwr?.CurrentFile == null && !pwr.WhiteboardMode) return;
        // Screenshot only works in single-page single-file view
        if (pwr.TwopageMode || pwr.DualFileMode) return;
        // If annotation mode is active, deactivate it first to avoid conflicting input handlers
        if (_annotateMode) DeactivateAnnotateMode();

        _screenshotMode = true;
        _screenshotDragging = false;

        // Intercept pointer events before annotation handlers and the renderer's
        // built-in pan handler (Tunnel fires before the element's own handlers).
        MuPDFRenderer.AddHandler(PointerPressedEvent,  OnScreenshotPointerPressed,  RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerMovedEvent,    OnScreenshotPointerMoved,    RoutingStrategies.Tunnel);
        MuPDFRenderer.AddHandler(PointerReleasedEvent, OnScreenshotPointerReleased, RoutingStrategies.Tunnel);

        MuPDFRenderer.Cursor = new Cursor(StandardCursorType.Cross);
        MuPDFRenderer.SetScreenshotMode(true);

        if (ScreenshotToggle != null)
            ScreenshotToggle.IsChecked = true;

        ScreenshotModeBanner.IsVisible = true;
        pwr.StatusMessage = "Drag to capture — Esc to cancel";

        // Ensure the renderer has focus so pointer events are not swallowed
        // by a previously focused control (e.g. the search text box).
        MuPDFRenderer.Focus();
    }

    private void DeactivateScreenshotMode()
    {
        if (!_screenshotMode) return;

        _screenshotMode    = false;
        _screenshotDragging = false;

        MuPDFRenderer.SetScreenshotMode(false);
        MuPDFRenderer.ClearRubberBand();
        MuPDFRenderer.RemoveHandler(PointerPressedEvent,  OnScreenshotPointerPressed);
        MuPDFRenderer.RemoveHandler(PointerMovedEvent,    OnScreenshotPointerMoved);
        MuPDFRenderer.RemoveHandler(PointerReleasedEvent, OnScreenshotPointerReleased);

        if (ScreenshotToggle != null)
            ScreenshotToggle.IsChecked = false;

        ScreenshotModeBanner.IsVisible = false;

        // Restore cursor to whatever mode is active
        MuPDFRenderer.Cursor = _annotateMode
            ? GetToolCursor(MuPDFRenderer.ActiveTool)
            : Avalonia.Input.Cursor.Default;
    }

    // ── Button / shortcut entry points ────────────────────────────────────────

    private void OnScreenshotToolClick(object? sender, RoutedEventArgs e)
    {
        if (_screenshotMode) DeactivateScreenshotMode();
        else                 ActivateScreenshotMode();
    }

    // ── Pointer handlers ──────────────────────────────────────────────────────

    private void OnScreenshotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_screenshotMode) return;
        var pt = e.GetCurrentPoint(MuPDFRenderer);
        if (!pt.Properties.IsLeftButtonPressed) return;

        var pdfPt = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        if (!pdfPt.HasValue) return;

        _screenshotStartPdf = pdfPt.Value;
        _screenshotDragging = true;
        MuPDFRenderer.SetRubberBand(pdfPt.Value, pdfPt.Value);
        e.Pointer.Capture(MuPDFRenderer);
        e.Handled = true;
    }

    private void OnScreenshotPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_screenshotMode || !_screenshotDragging) return;

        var pdfPt = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        if (pdfPt.HasValue)
            MuPDFRenderer.SetRubberBand(_screenshotStartPdf, pdfPt.Value);

        e.Handled = true;
    }

    private async void OnScreenshotPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_screenshotMode || !_screenshotDragging) return;

        _screenshotDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        var endPdf = MuPDFRenderer.ScreenToPdf(e.GetPosition(MuPDFRenderer));
        // Restore cross cursor momentarily before full deactivation cleans up
        DeactivateScreenshotMode();

        if (!endPdf.HasValue) return;

        double x = Math.Min(_screenshotStartPdf.X, endPdf.Value.X);
        double y = Math.Min(_screenshotStartPdf.Y, endPdf.Value.Y);
        double w = Math.Abs(endPdf.Value.X - _screenshotStartPdf.X);
        double h = Math.Abs(endPdf.Value.Y - _screenshotStartPdf.Y);

        // Ignore accidental tiny drags (< 4 PDF pts ≈ a few screen pixels)
        if (w < 4 || h < 4)
        {
            pwr.StatusMessage = "";
            return;
        }

        var pdfRect = new Rect(x, y, w, h);
        await CaptureRegionAsync(pdfRect, pwr.CurrentPage1);
    }
}
