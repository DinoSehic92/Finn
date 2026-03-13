using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Finn.Model;
using Finn.ViewModels;
using System;
using System.Collections.Generic;

namespace Finn.Dialogs
{
    public partial class xDiffDia : Window
    {
        private double _currentZoom = 1.0;
        private Matrix _matrix = Matrix.Identity;
        private bool _isPanning;
        private Point _panLast;
        private readonly MatrixTransform _contentTransform = new();

        private const double MinZoom = 0.1;
        private const double MaxZoom = 20.0;
        private const double ZoomBase = 1.2;
        private const double PanStep = 50.0;

        public xDiffDia()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
            PageSlider.AddHandler(Slider.ValueChangedEvent, OnPageSliderChanged);

            ContentArea.RenderTransform = _contentTransform;
            ContentArea.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);

            SetupZoomPan();
            SetupAnnotationInput();
        }

        private DiffViewModel VM => (DiffViewModel)DataContext!;

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            await VM.RunDiffAsync();
            FitToViewport();
            RedrawAnnotations();
            VM.PropertyChanged += OnVMPropertyChanged;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            VM.PropertyChanged -= OnVMPropertyChanged;
            VM.Cleanup();
        }

        private void OnVMPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DiffViewModel.CurrentPageIndex))
                RedrawAnnotations();
        }

        private async void OnApplyTolerance(object? sender, RoutedEventArgs e)
        {
            await VM.ApplyToleranceAsync();
        }

        private void OnPageSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (!PageSlider.IsFocused) return;
            int target = (int)PageSlider.Value - 1;
            if (target != VM.CurrentPageIndex)
            {
                VM.CurrentPageIndex = target;
                FitToViewport();
            }
        }

        private void OnSideBySide(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 0;
            UpdateToggles();
            FitToViewport();
        }

        private void OnOverlay(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 1;
            UpdateToggles();
            FitToViewport();
        }

        private void OnDiffOnly(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 2;
            UpdateToggles();
            FitToViewport();
        }

        private void UpdateToggles()
        {
            SideBySideBtn.IsChecked = VM.IsSideBySide;
            OverlayBtn.IsChecked = VM.IsOverlay;
            DiffOnlyBtn.IsChecked = VM.IsDiffOnly;
        }

        #region Zoom & Pan

        private void SetupZoomPan()
        {
            ZoomViewport.AddHandler(PointerWheelChangedEvent, OnViewportWheel, RoutingStrategies.Tunnel);
            ZoomViewport.AddHandler(PointerPressedEvent, OnViewportPointerPressed, RoutingStrategies.Tunnel);
            ZoomViewport.AddHandler(PointerMovedEvent, OnViewportPointerMoved, RoutingStrategies.Tunnel);
            ZoomViewport.AddHandler(PointerReleasedEvent, OnViewportPointerReleased, RoutingStrategies.Tunnel);
            ZoomViewport.SizeChanged += (_, _) => FitToViewport();
        }

        private void FitToViewport()
        {
            double vw = ZoomViewport.Bounds.Width;
            double vh = ZoomViewport.Bounds.Height;
            if (vw <= 0 || vh <= 0) return;

            // Use the viewport size as the content's natural size so it fits exactly
            ContentArea.Width = vw;
            ContentArea.Height = vh;

            _currentZoom = 1.0;
            _matrix = Matrix.Identity;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _contentTransform.Matrix = _matrix;
            ZoomLabel.Text = $"{_currentZoom * 100:F0}%";
        }

        private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Wheel = zoom around pointer
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
            else if (e.KeyModifiers == KeyModifiers.None)
            {
                // Plain wheel = page navigation
                if (e.Delta.Y < 0)
                    VM.NextPage();
                else if (e.Delta.Y > 0)
                    VM.PreviousPage();
            }
            else
            {
                // Shift+Wheel = horizontal pan
                double dx = e.Delta.Y * PanStep;
                if (dx == 0) { e.Handled = true; return; }

                _matrix *= Matrix.CreateTranslation(dx, 0);
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

        private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(ZoomViewport).Properties;
            if (props.IsMiddleButtonPressed || props.IsRightButtonPressed)
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

        private void OnZoomIn(object? sender, RoutedEventArgs e) => ZoomAroundViewportCenter(ZoomBase);
        private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomAroundViewportCenter(1.0 / ZoomBase);
        private void OnResetZoom(object? sender, RoutedEventArgs e) => FitToViewport();

        #endregion

        #region Annotations

        private bool _isDrawing;
        private Polyline? _activePolyline;
        private DiffAnnotation? _activeAnnotation;

        private void OnToggleDraw(object? sender, RoutedEventArgs e)
        {
            if (VM.ActiveTool == AnnotationTool.Draw)
                VM.ActiveTool = AnnotationTool.None;
            else
                VM.ActiveTool = AnnotationTool.Draw;
            SyncAnnotationToggles();
        }

        private void OnToggleHighlight(object? sender, RoutedEventArgs e)
        {
            if (VM.ActiveTool == AnnotationTool.Highlight)
                VM.ActiveTool = AnnotationTool.None;
            else
                VM.ActiveTool = AnnotationTool.Highlight;
            SyncAnnotationToggles();
        }

        private void SyncAnnotationToggles()
        {
            DrawBtn.IsChecked = VM.IsDrawTool;
            HighlightBtn.IsChecked = VM.IsHighlightTool;
            AnnotationCanvas.IsHitTestVisible = VM.IsAnnotating;
        }

        private void SetupAnnotationInput()
        {
            AnnotationCanvas.AddHandler(PointerPressedEvent, OnAnnotationPointerPressed, RoutingStrategies.Tunnel);
            AnnotationCanvas.AddHandler(PointerMovedEvent, OnAnnotationPointerMoved, RoutingStrategies.Tunnel);
            AnnotationCanvas.AddHandler(PointerReleasedEvent, OnAnnotationPointerReleased, RoutingStrategies.Tunnel);
        }

        private void OnAnnotationPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!VM.IsAnnotating) return;
            var props = e.GetCurrentPoint(AnnotationCanvas).Properties;
            if (!props.IsLeftButtonPressed) return;

            _isDrawing = true;
            e.Pointer.Capture(AnnotationCanvas);
            e.Handled = true;

            bool isHighlight = VM.ActiveTool == AnnotationTool.Highlight;

            _activeAnnotation = new DiffAnnotation
            {
                Tool = VM.ActiveTool,
                Color = isHighlight ? "#FFFF00" : VM.AnnotationColor,
                StrokeWidth = isHighlight ? 20 : VM.AnnotationStrokeWidth,
                PageIndex = VM.CurrentPageIndex
            };

            _activePolyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.Parse(_activeAnnotation.Color)),
                StrokeThickness = _activeAnnotation.StrokeWidth,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Opacity = isHighlight ? 0.35 : 1.0,
                Points = []
            };
            AnnotationCanvas.Children.Add(_activePolyline);

            var pos = e.GetPosition(AnnotationCanvas);
            _activeAnnotation.Points.Add((pos.X, pos.Y));
            _activePolyline.Points = [pos];
        }

        private void OnAnnotationPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDrawing || _activePolyline == null || _activeAnnotation == null) return;
            e.Handled = true;

            var pos = e.GetPosition(AnnotationCanvas);
            _activeAnnotation.Points.Add((pos.X, pos.Y));

            // Rebuild the points list so Avalonia picks up the change
            var pts = new List<Point>(_activePolyline.Points) { pos };
            _activePolyline.Points = pts;
        }

        private void OnAnnotationPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            e.Pointer.Capture(null);
            e.Handled = true;

            if (_activeAnnotation != null && _activeAnnotation.Points.Count > 1)
                VM.AddAnnotation(_activeAnnotation);

            _activePolyline = null;
            _activeAnnotation = null;
        }

        private void OnAnnotationUndo(object? sender, RoutedEventArgs e)
        {
            VM.UndoAnnotation();
            RedrawAnnotations();
        }

        private void OnAnnotationClear(object? sender, RoutedEventArgs e)
        {
            VM.ClearAnnotations();
            RedrawAnnotations();
        }

        /// <summary>
        /// Redraws all persisted annotations for the current page onto the canvas.
        /// Called after undo/clear or page change.
        /// </summary>
        private void RedrawAnnotations()
        {
            AnnotationCanvas.Children.Clear();

            foreach (var ann in VM.CurrentAnnotations)
            {
                bool isHighlight = ann.Tool == AnnotationTool.Highlight;
                var pts = new List<Point>(ann.Points.Count);
                foreach (var (x, y) in ann.Points)
                    pts.Add(new Point(x, y));

                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.Parse(ann.Color)),
                    StrokeThickness = ann.StrokeWidth,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                    Opacity = isHighlight ? 0.35 : 1.0,
                    Points = pts
                };
                AnnotationCanvas.Children.Add(polyline);
            }
        }

        #endregion
    }
}
