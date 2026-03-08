using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using DotNetCampus.Inking;
using DotNetCampus.Inking.StrokeRenderers.WpfForSkiaInkStrokeRenderers;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace Finn.Dialog;

public partial class xPaintDia : Window
{
    private readonly bool _isAnnotateMode;

    private double _currentZoom = 1.0;
    private Matrix _matrix = Matrix.Identity;
    private bool _isPanning;
    private Point _panLast;
    private readonly MatrixTransform _drawingTransform = new();
    private PixelSize _originalPixelSize;

    private const double MinZoom = 0.05;
    private const double MaxZoom = 20.0;
    private const double ZoomBase = 1.2;
    private const double PanStep = 50.0;

    // Landscape A4 at 96 DPI: 297mm × 210mm
    private const double A4Width = 1122;
    private const double A4Height = 794;

    public xPaintDia()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        ThicknessSlider.AddHandler(Slider.ValueChangedEvent, SetPenThickness);
        OpacitySlider.AddHandler(Slider.ValueChangedEvent, SetPenOpacity);

        DrawingArea.Width = A4Width;
        DrawingArea.Height = A4Height;
        DrawingArea.RenderTransform = _drawingTransform;
        DrawingArea.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);

        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer = new WpfForSkiaInkStrokeRenderer();
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = SKColors.Red;

        SetupZoomPan();
    }

    public xPaintDia(Bitmap pageBitmap) : this()
    {
        _isAnnotateMode = true;
        Title = "Annotate Page";

        _originalPixelSize = pageBitmap.PixelSize;
        PageImage.Source = pageBitmap;
        DrawingArea.Width = pageBitmap.Size.Width;
        DrawingArea.Height = pageBitmap.Size.Height;

        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkThickness = 4;

        BackgroundColorSection.IsVisible = false;
        ThicknessSlider.Value = 4;
        ThicknessSlider.Maximum = 30;
    }

    private void SetupZoomPan()
    {
        ZoomViewport.AddHandler(PointerWheelChangedEvent, OnViewportWheel, RoutingStrategies.Tunnel);
        ZoomViewport.AddHandler(PointerPressedEvent, OnViewportPointerPressed, RoutingStrategies.Tunnel);
        ZoomViewport.AddHandler(PointerMovedEvent, OnViewportPointerMoved, RoutingStrategies.Tunnel);
        ZoomViewport.AddHandler(PointerReleasedEvent, OnViewportPointerReleased, RoutingStrategies.Tunnel);
        ZoomViewport.SizeChanged += (_, _) => FitToViewport();
        Opened += (_, _) => FitToViewport();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();

        if (e.Key == Key.D0 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            FitToViewport();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key is Key.OemPlus or Key.Add)
                ZoomAroundViewportCenter(ZoomBase);
            else if (e.Key is Key.OemMinus or Key.Subtract)
                ZoomAroundViewportCenter(1.0 / ZoomBase);
        }
    }

    #region Zoom & Pan
    private void FitToViewport()
    {
        double vw = ZoomViewport.Bounds.Width;
        double vh = ZoomViewport.Bounds.Height;
        double dw = DrawingArea.Width;
        double dh = DrawingArea.Height;
        if (vw <= 0 || vh <= 0 || dw <= 0 || dh <= 0) return;

        double scale = Math.Min(vw / dw, vh / dh);
        double offsetX = (vw - dw * scale) / 2;
        double offsetY = (vh - dh * scale) / 2;

        _currentZoom = scale;
        _matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        _drawingTransform.Matrix = _matrix;
        ZoomLabel.Text = $"{_currentZoom * 100:F0}%";
    }

    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double delta = e.Delta.Y;
            if (delta == 0) { e.Handled = true; return; }

            double factor = Math.Pow(ZoomBase, delta);
            double newZoom = Math.Clamp(_currentZoom * factor, MinZoom, MaxZoom);

            factor = newZoom / _currentZoom;
            if (Math.Abs(factor - 1.0) < 1e-9) { e.Handled = true; return; }

            _currentZoom = newZoom;

            var pos = e.GetPosition(ZoomViewport);
            _matrix = _matrix
                * Matrix.CreateTranslation(-pos.X, -pos.Y)
                * Matrix.CreateScale(factor, factor)
                * Matrix.CreateTranslation(pos.X, pos.Y);

            ApplyTransform();
        }
        else
        {
            double dx = e.Delta.X * PanStep;
            double dy = e.Delta.Y * PanStep;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                dx += dy;
                dy = 0;
            }

            if (dx == 0 && dy == 0) { e.Handled = true; return; }

            _matrix *= Matrix.CreateTranslation(dx, dy);
            ApplyTransform();
        }

        e.Handled = true;
    }

    private void ZoomAroundViewportCenter(double factor)
    {
        double newZoom = Math.Clamp(_currentZoom * factor, MinZoom, MaxZoom);
        factor = newZoom / _currentZoom;
        if (Math.Abs(factor - 1.0) < 1e-9) return;

        _currentZoom = newZoom;

        double cx = ZoomViewport.Bounds.Width / 2;
        double cy = ZoomViewport.Bounds.Height / 2;
        _matrix = _matrix
            * Matrix.CreateTranslation(-cx, -cy)
            * Matrix.CreateScale(factor, factor)
            * Matrix.CreateTranslation(cx, cy);

        ApplyTransform();
    }

    private void ZoomIn(object sender, RoutedEventArgs e) => ZoomAroundViewportCenter(ZoomBase);
    private void ZoomOut(object sender, RoutedEventArgs e) => ZoomAroundViewportCenter(1.0 / ZoomBase);

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(ZoomViewport).Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panLast = e.GetPosition(ZoomViewport);
            e.Pointer.Capture(ZoomViewport);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        e.Handled = true;

        var current = e.GetPosition(ZoomViewport);
        var delta = current - _panLast;
        _panLast = current;

        _matrix *= Matrix.CreateTranslation(delta.X, delta.Y);
        ApplyTransform();
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void ResetZoom(object sender, RoutedEventArgs e) => FitToViewport();
    #endregion

    #region Tools
    private void ToggleStroke(object sender, RoutedEventArgs e)
    {
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer =
            ToggleStrokeButton.IsChecked == true ? new WpfForSkiaInkStrokeRenderer() : null;
    }

    private void SetPenColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string colorName)
        {
            var field = typeof(SKColors).GetField(colorName);
            if (field != null)
            {
                InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = (SKColor)field.GetValue(null)!;
                InkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                EraserToggle.IsChecked = false;
            }
        }
    }

    private void SetPenThickness(object sender, RoutedEventArgs e)
    {
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkThickness = (float)ThicknessSlider.Value;
    }

    private void SetPenOpacity(object sender, RoutedEventArgs e)
    {
        byte opacity = (byte)OpacitySlider.Value;
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor =
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor.WithAlpha(opacity);
    }

    private void ToggleEraser(object sender, RoutedEventArgs e)
    {
        InkCanvas.EditingMode = EraserToggle.IsChecked == true
            ? InkCanvasEditingMode.EraseByPoint
            : InkCanvasEditingMode.Ink;
    }

    private void SetWhiteboardColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string color)
        {
            if (color == "White") DrawingArea.Background = new SolidColorBrush(Colors.White);
            if (color == "Antique") DrawingArea.Background = new SolidColorBrush(Colors.AntiqueWhite);
            if (color == "Black") DrawingArea.Background = new SolidColorBrush(Colors.Black);
        }
    }

    private void UndoLine(object sender, RoutedEventArgs e)
    {
        if (InkCanvas.Strokes.Count > 0)
            InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(InkCanvas.Strokes.Last());
    }

    private void CleanCanvas(object sender, RoutedEventArgs e)
    {
        foreach (var stroke in InkCanvas.Strokes.ToList())
            InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(stroke);
    }
    #endregion

    #region Save
    private void SaveImage(object sender, RoutedEventArgs e)
    {
        if (_isAnnotateMode)
            SaveAnnotation();
        else
            SaveWhiteboard();
    }

    private void SaveAnnotation()
    {
        if (_originalPixelSize.Width <= 0 || _originalPixelSize.Height <= 0) return;

        var saved = _drawingTransform.Matrix;
        double sx = _originalPixelSize.Width / DrawingArea.Width;
        double sy = _originalPixelSize.Height / DrawingArea.Height;
        _drawingTransform.Matrix = Matrix.CreateScale(sx, sy);

        var rtb = new RenderTargetBitmap(_originalPixelSize);
        rtb.Render(DrawingArea);

        _drawingTransform.Matrix = saved;

        Directory.CreateDirectory("C:\\Finn\\Annotations");
        string path = Path.Combine("C:\\Finn\\Annotations",
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
        rtb.Save(path);
    }

    private void SaveWhiteboard()
    {
        Directory.CreateDirectory("C:\\Finn\\Sketches");
        string path = "C:\\Finn\\Sketches\\" + DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss") + ".pdf";

        using var skPaint = new SKPaint();
        skPaint.IsAntialias = true;
        skPaint.Style = SKPaintStyle.Fill;

        SKRect bounds = InkCanvas.Bounds.ToSKRect();

        using SKWStream stream = SKFileWStream.OpenStream(path);
        var document = SKDocument.CreatePdf(stream);

        using SKCanvas skCanvas = document.BeginPage(bounds.Width, bounds.Height);
        for (var i = 0; i < InkCanvas.Strokes.Count; i++)
        {
            var stroke = InkCanvas.Strokes[i];
            skPaint.Color = stroke.Color;
            skCanvas.DrawPath(stroke.Path, skPaint);
        }

        document.EndPage();
        document.Close();
    }
    #endregion
}