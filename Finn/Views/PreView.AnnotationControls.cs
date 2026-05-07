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
using System.Collections.Generic;
using System.Linq;

namespace Finn.Views;

public partial class PreView
{
    private StackPanel? _propertyDashRow;
    private StackPanel? _propertyOpacityRow;
    private Button? _propertyClosePolyBtn;

    private void OnAnnotateColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            var color = ColorPalette.GetValueOrDefault(colorName, ColorPalette["Red"]);
            MuPDFRenderer.StrokeColor = color;

            if (MuPDFRenderer.ActiveLayer != null)
                MuPDFRenderer.ActiveLayer.Color = color;

            // Apply to currently selected annotation
            ApplyColorToSelection(color);

            SetActiveColorButton(btn);
            UpdateColorIndicator(color);
            UpdateAnnotationStatusHint();
            MuPDFRenderer.Focus();
        }
    }

    private void ApplyColorToSelection(Color color)
    {
        if (_selectedAnnotations.Count == 0) return;
        var snaps = new List<object>(_selectedAnnotations.Count);
        foreach (var selItem in _selectedAnnotations)
        {
            if (_propertyPanelTarget == null)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(selItem);
                if (snap != null) snaps.Add(snap);
            }
            switch (selItem)
            {
                case InkStroke s: s.Color = color; s.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.Color = color; sh.InvalidatePen(); break;
                case TextAnnotation t: t.Color = color; break;
                case MeasurementAnnotation m: m.Color = color; break;
            }
        }
        if (snaps.Count > 0)
            MuPDFRenderer.PushGroupPropertyUndo(snaps);
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
    }

    private void OnAnnotateToolSelect(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName)
        {
            var tool = Enum.Parse<InlineAnnotationTool>(toolName);
            ApplyToolSwitch(tool);
        }
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    private void OnAnnotateWidth(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widthStr && double.TryParse(widthStr, out double w))
        {
            if (MuPDFRenderer.IsHighlighterMode) return;
            MuPDFRenderer.StrokeWidth = w;
            _normalStrokeWidth = w;

            // Apply to currently selected annotations
            var snaps = new List<object>();
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is InkStroke ink)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(ink);
                    if (snap != null) snaps.Add(snap);
                    ink.Width = w; ink.InvalidatePen();
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
                    if (snap != null) snaps.Add(snap);
                    sh.StrokeWidth = w; sh.InvalidatePen();
                }
            }
            if (snaps.Count > 0) MuPDFRenderer.PushGroupPropertyUndo(snaps);
            if (_selectedAnnotations.Count > 0) { MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged(); }

            SetActiveWidthButton(btn);
            UpdateBrushSizeIndicator(w);
            UpdateAnnotationStatusHint();
        }
    }

    private void OnAnnotateDashPattern(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string patternName
            && Enum.TryParse<LineDashPattern>(patternName, out var pattern))
        {
            MuPDFRenderer.StrokeDashPattern = pattern;

            // Apply to currently selected annotations
            // Apply to currently selected annotations
            var snaps = new List<object>();
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is InkStroke ink)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(ink);
                    if (snap != null) snaps.Add(snap);
                    ink.DashPattern = pattern; ink.InvalidatePen();
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
                    if (snap != null) snaps.Add(snap);
                    sh.DashPattern = pattern; sh.InvalidatePen();
                }
            }
            if (snaps.Count > 0) MuPDFRenderer.PushGroupPropertyUndo(snaps);
            if (_selectedAnnotations.Count > 0) { MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged(); }

            SetActiveDashButton(btn);
            UpdateDashIndicator(pattern);
            UpdateAnnotationStatusHint();
        }
    }

    private Button? _activeCornerRadiusButton;

    private void OnAnnotateCornerRadius(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string radiusStr && double.TryParse(radiusStr, out double r))
        {
            MuPDFRenderer.ShapeCornerRadius = r;

            // Apply to currently selected annotations
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is ShapeAnnotation sh && sh.ShapeType == InlineAnnotationTool.Rectangle)
                {
                    sh.CornerRadius = r;
                    sh.InvalidatePen();
                }
                else if (selItem is InkStroke { IsPolyline: true } ink)
                {
                    ink.CornerRadius = r;
                    ink.InvalidatePen();
                }
            }
            if (_selectedAnnotations.Count > 0) { MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged(); }

            SetActiveButton(ref _activeCornerRadiusButton, btn);
            UpdateCornerIndicator(r);
            UpdateAnnotationStatusHint();
        }
    }

    /// <summary>
    /// Highlights a toolbar button with the system accent color border, clearing the
    /// previous one stored in the given field. Used for tool, color, width, and dash buttons.
    /// </summary>
    private static void SetActiveButton(ref Button? field, Button? btn)
    {
        if (field != null)
        {
            field.BorderThickness = new Thickness(0);
            field.BorderBrush = null;
            field.Background = Brushes.Transparent;
            field.Opacity = 1.0;
        }
        field = btn;
        if (btn != null)
        {
            btn.BorderThickness = new Thickness(2);
            btn.BorderBrush = btn.TryFindResource("SystemAccentColor", btn.ActualThemeVariant, out var accentObj) && accentObj is Color accentColor
                ? new SolidColorBrush(accentColor)
                : Brushes.White;
            btn.Background = btn.TryFindResource("SystemAccentColor", btn.ActualThemeVariant, out accentObj) && accentObj is Color activeColor
                ? new SolidColorBrush(Color.FromArgb(32, activeColor.R, activeColor.G, activeColor.B))
                : new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            btn.Opacity = 1.0;
        }
    }

    private void SetActiveToolButton(Button? btn) => SetActiveButton(ref _activeToolButton, btn);
    private void SetActiveColorButton(Button? btn) => SetActiveButton(ref _activeColorButton, btn);
    private void SetActiveWidthButton(Button? btn) => SetActiveButton(ref _activeWidthButton, btn);
    private void SetActiveDashButton(Button? btn) => SetActiveButton(ref _activeDashButton, btn);

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
        SetActiveDashButton(FindToolbarButtonByTag(MuPDFRenderer.StrokeDashPattern.ToString()));
        SyncFlyoutIndicators();
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
        UpdateBrushSizeIndicator(MuPDFRenderer.StrokeWidth);
    }

    /// <summary>Updates the color flyout button indicator to reflect the current color.</summary>
    private void UpdateColorIndicator(Color color)
    {
        if (ColorFlyoutIndicator != null)
            ColorFlyoutIndicator.Fill = new SolidColorBrush(color);
    }

    /// <summary>Updates the brush size flyout button indicator to reflect the current width.</summary>
    private void UpdateBrushSizeIndicator(double width)
    {
        if (BrushSizeIndicator == null) return;
        double size = width switch { <= 1 => 4, <= 2 => 6, _ => 9 };
        BrushSizeIndicator.Width = size;
        BrushSizeIndicator.Height = size;
    }

    /// <summary>Updates the dash pattern flyout button indicator to reflect the current pattern.</summary>
    private void UpdateDashIndicator(LineDashPattern pattern)
    {
        if (DashIndicator == null) return;
        DashIndicator.StrokeDashArray = pattern switch
        {
            LineDashPattern.Dashed => new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
            LineDashPattern.Dotted => new Avalonia.Collections.AvaloniaList<double> { 1, 2 },
            LineDashPattern.DashDot => new Avalonia.Collections.AvaloniaList<double> { 4, 2, 1, 2 },
            _ => null,
        };
    }

    /// <summary>Updates the corner radius flyout button indicator to reflect the current radius.</summary>
    private void UpdateCornerIndicator(double radius)
    {
        if (CornerIndicator != null)
            CornerIndicator.CornerRadius = new CornerRadius(Math.Min(radius, 7));
    }

    /// <summary>Syncs all flyout indicators to the current renderer state.</summary>
    private void SyncFlyoutIndicators()
    {
        UpdateColorIndicator(MuPDFRenderer.StrokeColor);
        UpdateBrushSizeIndicator(MuPDFRenderer.StrokeWidth);
        UpdateDashIndicator(MuPDFRenderer.StrokeDashPattern);
        UpdateCornerIndicator(MuPDFRenderer.ShapeCornerRadius);
    }

    private void OnAnnotateUndo(object sender, RoutedEventArgs e) { MuPDFRenderer.Undo(); MuPDFRenderer.Focus(); }

    private void OnAnnotateRedo(object sender, RoutedEventArgs e) { MuPDFRenderer.Redo(); MuPDFRenderer.Focus(); }

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
        if (_selectedAnnotations.Count > 0)
        {
            foreach (var selItem in _selectedAnnotations)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(selItem);
                switch (selItem)
                {
                    case InkStroke s: s.Opacity = OpacitySlider.Value; s.InvalidatePen(); break;
                    case ShapeAnnotation sh: sh.Opacity = OpacitySlider.Value; sh.InvalidatePen(); break;
                    case TextAnnotation t: t.Opacity = OpacitySlider.Value; break;
                }
                if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            }
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
        }
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
        MuPDFRenderer.Focus();
    }

    private void OnColorInputCancel(object sender, RoutedEventArgs e)
    {
        ColorInputCanvas.IsVisible = false;
        MuPDFRenderer.Focus();
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

    private void UpdateUndoRedoButtons()
    {
        bool canUndo = MuPDFRenderer.CanUndoCurrentPage;
        bool canRedo = MuPDFRenderer.CanRedoCurrentPage;
        if (_undoBtn != null) { _undoBtn.Opacity = canUndo ? 1.0 : 0.35; _undoBtn.IsEnabled = canUndo; }
        if (_redoBtn != null) { _redoBtn.Opacity = canRedo ? 1.0 : 0.35; _redoBtn.IsEnabled = canRedo; }
    }

    private void OnAnnotationChanged()
    {
        UpdateAnnotationCountBadge();
        UpdateUndoRedoButtons();
        UpdateActiveLayerLabel();
        ctx?.MarkDirty();
    }

    /// <summary>Shows a context menu listing all layers; clicking one sets it as the active layer.</summary>
    private void OnLayerPickerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var layers = MuPDFRenderer.Layers;
        if (layers.Count == 0) return;
        var menu = new ContextMenu();
        foreach (var layer in layers.ToList())
        {
            var capturedLayer = layer;
            var suffix = capturedLayer.IsLocked ? " \U0001F512" : "";
            var item = new MenuItem
            {
                Header = capturedLayer.Name + suffix,
                IsChecked = capturedLayer == MuPDFRenderer.ActiveLayer
            };
            item.Click += (_, _) =>
            {
                MuPDFRenderer.ActiveLayer = capturedLayer;
                UpdateActiveLayerLabel();
                MuPDFRenderer.Focus();
            };
            menu.Items.Add(item);
        }
        menu.Open(btn);
    }

    /// <summary>Toggles the visibility of the active annotation layer.</summary>
    private void OnToggleLayerVisibility(object? sender, RoutedEventArgs e)
    {
        var layer = MuPDFRenderer.ActiveLayer;
        if (layer == null) return;
        layer.IsVisible = !layer.IsVisible;
        MuPDFRenderer.InvalidateVisual();
        UpdateActiveLayerLabel();
        MuPDFRenderer.Focus();
    }

    /// <summary>Toggles the lock state of the active annotation layer.</summary>
    private void OnToggleLayerLock(object? sender, RoutedEventArgs e)
    {
        var layer = MuPDFRenderer.ActiveLayer;
        if (layer == null) return;
        layer.IsLocked = !layer.IsLocked;
        MuPDFRenderer.InvalidateVisual();
        UpdateActiveLayerLabel();
        MuPDFRenderer.Focus();
    }

    /// <summary>Syncs the layer label, visibility and lock button state to the current active layer.</summary>
    private void UpdateActiveLayerLabel()
    {
        var layer = MuPDFRenderer.ActiveLayer;
        if (layer == null) return;
        if (ActiveLayerLabel != null)
            ActiveLayerLabel.Text = layer.IsLocked ? $"{layer.Name} \U0001F512" : layer.Name;
        if (LayerVisBtn != null)
            LayerVisBtn.Opacity = layer.IsVisible ? 1.0 : 0.35;
        if (LayerLockBtn != null)
            LayerLockBtn.Opacity = layer.IsLocked ? 1.0 : 0.35;
    }

    private void OnFontSizeDecrease(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.TextFontSize = Math.Max(6, MuPDFRenderer.TextFontSize - 2);
        UpdateFontSizeLabel();
        if (_selectedAnnotation is TextAnnotation t)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(t);
            t.FontSize = MuPDFRenderer.TextFontSize;
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged();
        }
    }

    private void OnFontSizeIncrease(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.TextFontSize = Math.Min(72, MuPDFRenderer.TextFontSize + 2);
        UpdateFontSizeLabel();
        if (_selectedAnnotation is TextAnnotation t)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(t);
            t.FontSize = MuPDFRenderer.TextFontSize;
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual(); MuPDFRenderer.NotifyAnnotationChanged();
        }
    }

    private void OnToggleFill(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.IsFilledMode = !MuPDFRenderer.IsFilledMode;

        // Apply to selected closed shape (rect / ellipse / cloud)
        if (_selectedAnnotation is ShapeAnnotation sh
            && sh.ShapeType is InlineAnnotationTool.Rectangle
                            or InlineAnnotationTool.Ellipse
                            or InlineAnnotationTool.RevisionCloud)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(sh);
            sh.IsFilled = MuPDFRenderer.IsFilledMode;
            sh.InvalidatePen();
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
        }
        // Apply to selected closed polyline
        else if (_selectedAnnotation is InkStroke { IsPolyline: true, IsClosed: true } poly)
        {
            var snap = MuPDFRenderer.CapturePropertySnapshot(poly);
            poly.IsFilled = MuPDFRenderer.IsFilledMode;
            poly.InvalidatePen();
            if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            MuPDFRenderer.InvalidateVisual();
            MuPDFRenderer.NotifyAnnotationChanged();
        }

        SyncFillToggleButton(MuPDFRenderer.IsFilledMode);
    }

    /// <summary>Applies accent-color active styling to a toggle button, or resets it to transparent.</summary>
    private static void SyncToggleButton(Button? btn, bool active)
    {
        if (btn == null) return;
        btn.BorderThickness = active ? new Thickness(2) : new Thickness(0);
        btn.BorderBrush = active
            ? (btn.TryFindResource("SystemAccentColor", btn.ActualThemeVariant, out var obj) && obj is Color c
                ? new SolidColorBrush(c) : Brushes.White)
            : null;
        btn.Background = active
            ? (btn.TryFindResource("SystemAccentColor", btn.ActualThemeVariant, out var bgObj) && bgObj is Color bgColor
                ? new SolidColorBrush(Color.FromArgb(32, bgColor.R, bgColor.G, bgColor.B))
                : new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)))
            : Brushes.Transparent;
    }

    /// <summary>Syncs the fill toggle button visual state to the given value.</summary>
    private void SyncFillToggleButton(bool isFilled) => SyncToggleButton(FillToggleBtn, isFilled);

    private void OnToggleGrid(object sender, RoutedEventArgs e)
    {
        MuPDFRenderer.SnapToGrid = !MuPDFRenderer.SnapToGrid;
        SyncGridToggleButton(MuPDFRenderer.SnapToGrid);
        MuPDFRenderer.InvalidateVisual();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    /// <summary>Syncs the grid toggle button visual state to the given value.</summary>
    private void SyncGridToggleButton(bool active) => SyncToggleButton(GridToggleBtn, active);

    private void OnToggleStatusHints(object? sender, RoutedEventArgs e)
    {
        _showAnnotationStatusHints = !_showAnnotationStatusHints;
        SyncStatusHintsToggleButton();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    private void SyncStatusHintsToggleButton()
    {
        _statusHintsToggleBtn ??= this.FindControl<Button>("StatusHintsToggleBtn");
        SyncToggleButton(_statusHintsToggleBtn, _showAnnotationStatusHints);
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

    private void ShowTextInput(Point pdfPoint, Point screenPos, Point? arrowOrigin = null, bool isStickyNote = false)
    {
        _textPlacementPdfPoint = pdfPoint;
        _editingTextAnnotation = null;
        _pendingArrowOrigin = arrowOrigin;
        _pendingStickyNote = isStickyNote;

        // Use the property panel with text row visible, but hide non-text rows
        _propertyPanelTarget = null;
        Canvas.SetLeft(PropertyPanelBorder, Math.Min(screenPos.X, MuPDFRenderer.Bounds.Width - 240));
        Canvas.SetTop(PropertyPanelBorder, Math.Min(screenPos.Y, MuPDFRenderer.Bounds.Height - 100));
        PropertyStrokeRow.IsVisible = false;
        PropertyFillBtn.IsVisible = false;
        PropertyCornerRadiusRow.IsVisible = false;
        PropertyActionsRow.IsVisible = false;
        var closePolyBtn = this.FindControl<Button>("PropertyClosePolyBtn");
        if (closePolyBtn != null) closePolyBtn.IsVisible = false;
        PropertyTextRow.IsVisible = true;
        PropertyTextBox.Text = "";

        // Give the TextBox a finite width matching the annotation's intended render width
        // (TextMaxWidth in PDF units → screen pixels). Without this the TextBox has no
        // width constraint inside the Canvas and TextWrapping="Wrap" never activates.
        var da = MuPDFRenderer.DisplayArea;
        var bounds = MuPDFRenderer.Bounds;
        if (da.Width > 0 && bounds.Width > 0)
            PropertyTextBox.Width = Math.Clamp(MuPDFRenderer.TextMaxWidth / da.Width * bounds.Width, 120, 400);
        else
            PropertyTextBox.Width = 220;

        PropertyPanelCanvas.IsVisible = true;
        PropertyTextBox.Focus();
        UpdateAnnotationStatusHint();
    }

    private void ShowTextEdit(TextAnnotation existing, Point screenPos)
    {
        _textPlacementPdfPoint = existing.Position;
        _editingTextAnnotation = existing;
        // Position the property panel directly over the annotation for in-place editing
        var da = MuPDFRenderer.DisplayArea;
        var bounds = MuPDFRenderer.Bounds;
        if (da.Width > 0 && bounds.Width > 0)
        {
            double annotX = (existing.Position.X - da.X) / da.Width * bounds.Width;
            double annotY = (existing.Position.Y - da.Y) / da.Height * bounds.Height;
            screenPos = new Point(annotX, annotY);
            // Match text input width to annotation's MaxWidth for WYSIWYG editing
            double pdfWidth = existing.MaxWidth > 0 ? existing.MaxWidth : MuPDFRenderer.TextMaxWidth;
            PropertyTextBox.Width = Math.Clamp(pdfWidth / da.Width * bounds.Width, 120, 400);
        }
        // Show the property panel with text editing for the annotation
        ShowPropertyPanel(existing, screenPos);
    }

    private void OnTextInputCommit(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue && !string.IsNullOrWhiteSpace(PropertyTextBox.Text))
        {
            if (_editingTextAnnotation != null)
            {
                var snap = MuPDFRenderer.CapturePropertySnapshot(_editingTextAnnotation);
                _editingTextAnnotation.Text = PropertyTextBox.Text;
                // Reset to the default wrap width before auto-sizing so that
                // editing with more text can wrap correctly, and editing with
                // less text still shrinks the box to fit.
                _editingTextAnnotation.MaxWidth = MuPDFRenderer.TextMaxWidth;
                MuPDFRenderer.AutoSizeTextWidth(_editingTextAnnotation);
                if (snap != null) MuPDFRenderer.PushPropertyUndo(snap);
            }
            else if (_pendingStickyNote)
            {
                MuPDFRenderer.PlaceStickyNote(_textPlacementPdfPoint.Value, PropertyTextBox.Text);
            }
            else if (_pendingArrowOrigin.HasValue)
            {
                MuPDFRenderer.PlaceArrowText(_pendingArrowOrigin.Value,
                    _textPlacementPdfPoint.Value, PropertyTextBox.Text);
            }
            else
            {
                MuPDFRenderer.PlaceText(_textPlacementPdfPoint.Value, PropertyTextBox.Text);
            }
        }

        CloseTextInput();
        MuPDFRenderer.InvalidateVisual();
        UpdateAnnotationStatusHint();
    }

    private void OnTextInputCancel(object sender, RoutedEventArgs e)
    {
        CloseTextInput();
        UpdateAnnotationStatusHint();
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
        // Always enter guided calibration mode: switch to the measure tool
        // and let the user draw a reference line. The distance input dialog
        // appears after the line is committed (in OnInkPointerPressed).
        // This avoids the old behaviour where the dialog would appear
        // immediately when measurements existed, blocking the PDF and
        // preventing the user from drawing a new reference line.
        _calibrationMode = true;
        ApplyToolSwitch(InlineAnnotationTool.MeasureDistance);
        UpdateAnnotationStatusHint();
    }

    private void OnCalibrationApply(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(CalibrationValueBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double realMm) && realMm > 0)
        {
            MuPDFRenderer.CalibrateFromLastMeasurement(realMm);
            if (MuPDFRenderer.HasInconsistentMeasurementScales())
                MuPDFRenderer.NormalizeMeasurementScales();
        }
        CalibrationCanvas.IsVisible = false;
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    private void OnCalibrationCancel(object sender, RoutedEventArgs e)
    {
        CalibrationCanvas.IsVisible = false;
        _calibrationMode = false;
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    /// <summary>Centers the calibration dialog overlay in the preview area.</summary>
    private void CenterCalibrationDialog()
    {
        // Use approximate dialog size; Avalonia measures on next layout pass
        const double dialogWidth = 250;
        const double dialogHeight = 110;
        double cx = Math.Max(0, (MuPDFRenderer.Bounds.Width - dialogWidth) / 2);
        double cy = Math.Max(0, (MuPDFRenderer.Bounds.Height - dialogHeight) / 2);
        Canvas.SetLeft(CalibrationBorder, cx);
        Canvas.SetTop(CalibrationBorder, cy);
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

    private void OnPropertyDuplicate(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null && !MuPDFRenderer.IsActiveLayerLocked)
        {
            _annotationClipboard = _propertyPanelTarget;
            PasteAnnotation();
        }
        ClosePropertyPanel();
    }

    private void OnPropertyMatchStyle(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            ArmMatchStyle(_propertyPanelTarget);
        }
        ClosePropertyPanel();
    }

    private void OnPropertyBringToFront(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            MuPDFRenderer.BringToFront(_propertyPanelTarget);
            if (pwr != null) pwr.StatusMessage = "Annotation moved to front";
        }
        ClosePropertyPanel();
    }

    private void OnPropertySendToBack(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            MuPDFRenderer.SendToBack(_propertyPanelTarget);
            if (pwr != null) pwr.StatusMessage = "Annotation sent to back";
        }
        ClosePropertyPanel();
    }

    private void OnPropertyClosePolyline(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is InkStroke { IsPolyline: true } poly)
            MuPDFRenderer.TogglePolylineClosed(poly);
        ClosePropertyPanel();
    }

    private void OnPropertyDelete(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            MuPDFRenderer.DeleteAnnotation(_propertyPanelTarget);
            _selectedAnnotation = null;
            _selectedAnnotations.Clear();
            MuPDFRenderer.ClearSelectHighlight();
        }
        ClosePropertyPanel();
    }

    private void OnAnnotateKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            if (!_middlePanning)
                MuPDFRenderer.Cursor = GetToolCursor(MuPDFRenderer.ActiveTool);
            e.Handled = true;
        }
    }

    /// <summary>
    /// When an annotation is selected, update the toolbar buttons to reflect
    /// its color, width, dash, opacity, font size, and fill state.
    /// This gives Figma-style "inspect on select" feedback.
    /// </summary>
    private void SyncToolbarToSelection()
    {
        if (_selectedAnnotation == null) return;

        Color? color = null;
        double? width = null;
        LineDashPattern? dash = null;
        double? opacity = null;

        switch (_selectedAnnotation)
        {
            case InkStroke s:
                color = s.Color; width = s.Width; dash = s.DashPattern; opacity = s.Opacity;
                break;
            case ShapeAnnotation sh:
                color = sh.Color; width = sh.StrokeWidth; dash = sh.DashPattern; opacity = sh.Opacity;
                break;
            case TextAnnotation t:
                color = t.Color; opacity = t.Opacity;
                break;
            case MeasurementAnnotation m:
                color = m.Color;
                break;
        }

        // Sync color button highlight
        if (color.HasValue)
        {
            var tag = MatchColorTag(color.Value);
            SetActiveColorButton(tag != null ? FindToolbarButtonByTag(tag) : null);
            MuPDFRenderer.StrokeColor = color.Value;
            UpdateColorIndicator(color.Value);
        }

        // Sync width button highlight
        if (width.HasValue)
        {
            SetActiveWidthButton(FindToolbarButtonByTag(((int)width.Value).ToString()));
            if (!MuPDFRenderer.IsHighlighterMode)
            { MuPDFRenderer.StrokeWidth = width.Value; _normalStrokeWidth = width.Value; }
            UpdateBrushSizeIndicator(width.Value);
        }

        // Sync dash pattern button highlight
        if (dash.HasValue)
        {
            SetActiveDashButton(FindToolbarButtonByTag(dash.Value.ToString()));
            MuPDFRenderer.StrokeDashPattern = dash.Value;
            UpdateDashIndicator(dash.Value);
        }

        // Sync opacity slider
        if (opacity.HasValue && OpacitySlider != null)
            OpacitySlider.Value = opacity.Value;

        // Sync font size for text annotations
        if (_selectedAnnotation is TextAnnotation txt)
        {
            MuPDFRenderer.TextFontSize = txt.FontSize;
            UpdateFontSizeLabel();
        }

        // Sync fill toggle for closed shapes
        if (_selectedAnnotation is ShapeAnnotation shape
            && shape.ShapeType is InlineAnnotationTool.Rectangle
                               or InlineAnnotationTool.Ellipse
                               or InlineAnnotationTool.RevisionCloud)
        {
            MuPDFRenderer.IsFilledMode = shape.IsFilled;
            SyncFillToggleButton(shape.IsFilled);
        }
    }

    private static string? MatchColorTag(Color c) =>
        ColorNames.GetValueOrDefault(c);

    // ── Property panel (double-click to edit shape/stroke properties) ──────

    private void ShowPropertyPanel(object item, Point screenPos)
    {
        _propertyPanelTarget = item;
        SelectAnnotation(item);

        Canvas.SetLeft(PropertyPanelBorder, Math.Min(screenPos.X, MuPDFRenderer.Bounds.Width - 220));
        Canvas.SetTop(PropertyPanelBorder, Math.Min(screenPos.Y + 10, MuPDFRenderer.Bounds.Height - 100));

        bool isDot = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Dot };
        bool isText = item is TextAnnotation;
        bool isMeasure = item is MeasurementAnnotation;
        bool isPolyline = item is InkStroke { IsPolyline: true, IsAreaMeasure: false, Points.Count: >= 3 };
        bool isAreaPolyline = item is InkStroke { IsPolyline: true, IsAreaMeasure: true };
        bool isGenericStroke = item is InkStroke { IsPolyline: false };
        bool isShape = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud or InlineAnnotationTool.Line or InlineAnnotationTool.Arrow };

        bool hasStroke = isDot || isPolyline || isAreaPolyline || isGenericStroke || isShape;
        bool hasDash = (isPolyline || isAreaPolyline || isGenericStroke || isShape) && !isDot;
        bool hasFill = (item is ShapeAnnotation sf
            && sf.ShapeType is InlineAnnotationTool.Rectangle
                            or InlineAnnotationTool.Ellipse
                            or InlineAnnotationTool.RevisionCloud)
            || item is InkStroke { IsPolyline: true, IsClosed: true };
        bool hasCornerRadius = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle }
            || item is InkStroke { IsPolyline: true, IsAreaMeasure: false };

        PropertyStrokeRow.IsVisible = hasStroke;
        _propertyDashRow ??= this.FindControl<StackPanel>("PropertyDashRow");
        if (_propertyDashRow != null) _propertyDashRow.IsVisible = hasDash;
        PropertyFillBtn.IsVisible = hasFill;
        PropertyCornerRadiusRow.IsVisible = hasCornerRadius;
        _propertyOpacityRow ??= this.FindControl<StackPanel>("PropertyOpacityRow");
        if (_propertyOpacityRow != null) _propertyOpacityRow.IsVisible = !isText;
        _propertyClosePolyBtn ??= this.FindControl<Button>("PropertyClosePolyBtn");
        if (_propertyClosePolyBtn != null) _propertyClosePolyBtn.IsVisible = isPolyline || isAreaPolyline;

        if ((isPolyline || isAreaPolyline) && _propertyClosePolyBtn != null)
        {
            var poly = (InkStroke)item;
            ToolTip.SetTip(_propertyClosePolyBtn, poly.IsClosed ? "Open polyline" : "Close polyline");
        }

        if (hasFill)
        {
            bool isFilled = item is ShapeAnnotation sha ? sha.IsFilled
                           : item is InkStroke ink ? ink.IsFilled
                           : false;
            PropertyFillBtn.BorderThickness = isFilled ? new Thickness(2) : new Thickness(0);
            PropertyFillBtn.BorderBrush = isFilled ? Brushes.White : null;
        }

        double opacity = item switch
        {
            InkStroke s => s.Opacity,
            ShapeAnnotation sh => sh.Opacity,
            TextAnnotation t => t.Opacity,
            MeasurementAnnotation m => 1.0,
            _ => 1.0
        };
        PropertyOpacitySlider.Value = opacity;

        // Show inline text editor for text annotations
        PropertyTextRow.IsVisible = isText;
        if (isText && item is TextAnnotation textItem)
            PropertyTextBox.Text = textItem.Text;

        PropertyPanelCanvas.IsVisible = true;

        if (isText)
            PropertyTextBox.Focus();
    }

    private void ClosePropertyPanel()
    {
        PropertyPanelCanvas.IsVisible = false;
        // Restore the transparent background for normal (non-text-edit) usage
        PropertyPanelCanvas.Background = Avalonia.Media.Brushes.Transparent;
        _propertyPanelTarget = null;
        FlushPropertySessionUndo();
        UpdateAnnotationStatusHint();
        MuPDFRenderer.Focus();
    }

    private void OnPropertyPanelBackgroundClick(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(PropertyPanelBorder);
        if (pos.X < 0 || pos.Y < 0 || pos.X > PropertyPanelBorder.Bounds.Width || pos.Y > PropertyPanelBorder.Bounds.Height)
        {
            // If text editing is active, commit on outside click
            if (_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null)
                OnTextInputCommit(this, e);
            else
                ClosePropertyPanel();
            e.Handled = true;
        }
    }

    private void OnPropertyColor(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (sender is not Button btn || btn.Tag is not string colorName) return;
        var color = ColorPalette.GetValueOrDefault(colorName, ColorPalette["Red"]);
        foreach (var selItem in _selectedAnnotations)
            StagePropertyUndoSnapshot(selItem);
        ApplyColorToSelection(color);
        MuPDFRenderer.StrokeColor = color;
        SetActiveColorButton(FindToolbarButtonByTag(colorName));
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyWidth(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (sender is not Button btn || btn.Tag is not string widthStr || !double.TryParse(widthStr, out double w)) return;
        foreach (var selItem in _selectedAnnotations)
        {
            StagePropertyUndoSnapshot(selItem);
            switch (selItem)
            {
                case InkStroke ink: ink.Width = w; ink.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.StrokeWidth = w; sh.InvalidatePen(); break;
            }
        }
        MuPDFRenderer.StrokeWidth = w;
        _normalStrokeWidth = w;
        SetActiveWidthButton(FindToolbarButtonByTag(widthStr));
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyDash(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (sender is not Button btn || btn.Tag is not string patternName
            || !Enum.TryParse<LineDashPattern>(patternName, out var pattern)) return;
        foreach (var selItem in _selectedAnnotations)
        {
            StagePropertyUndoSnapshot(selItem);
            switch (selItem)
            {
                case InkStroke ink: ink.DashPattern = pattern; ink.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.DashPattern = pattern; sh.InvalidatePen(); break;
            }
        }
        MuPDFRenderer.StrokeDashPattern = pattern;
        SetActiveDashButton(FindToolbarButtonByTag(patternName));
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyFill(object sender, RoutedEventArgs e)
    {
        bool isFilled;
        if (_propertyPanelTarget is ShapeAnnotation sh)
        {
            StagePropertyUndoSnapshot(sh);
            sh.IsFilled = !sh.IsFilled;
            sh.InvalidatePen();
            isFilled = sh.IsFilled;
            MuPDFRenderer.IsFilledMode = isFilled;
            SyncFillToggleButton(isFilled);
        }
        else if (_propertyPanelTarget is InkStroke { IsPolyline: true, IsClosed: true } poly)
        {
            StagePropertyUndoSnapshot(poly);
            poly.IsFilled = !poly.IsFilled;
            poly.InvalidatePen();
            isFilled = poly.IsFilled;
        }
        else return;

        PropertyFillBtn.BorderThickness = isFilled ? new Thickness(2) : new Thickness(0);
        PropertyFillBtn.BorderBrush = isFilled ? Brushes.White : null;
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyCornerRadius(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (sender is not Button btn || btn.Tag is not string radiusStr || !double.TryParse(radiusStr, out double r)) return;
        foreach (var selItem in _selectedAnnotations)
        {
            StagePropertyUndoSnapshot(selItem);
            switch (selItem)
            {
                case ShapeAnnotation sh when sh.ShapeType == InlineAnnotationTool.Rectangle:
                    sh.CornerRadius = r; sh.InvalidatePen(); break;
                case InkStroke { IsPolyline: true } ink:
                    ink.CornerRadius = r; ink.InvalidatePen(); break;
            }
        }
        MuPDFRenderer.ShapeCornerRadius = r;
        SetActiveButton(ref _activeCornerRadiusButton, btn);
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyOpacityChanged(object? sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null || PropertyOpacitySlider == null) return;
        double val = PropertyOpacitySlider.Value;
        foreach (var selItem in _selectedAnnotations)
        {
            StagePropertyUndoSnapshot(selItem);
            switch (selItem)
            {
                case InkStroke s: s.Opacity = val; s.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.Opacity = val; sh.InvalidatePen(); break;
                case TextAnnotation t: t.Opacity = val; break;
            }
        }
        MuPDFRenderer.StrokeOpacity = val;
        if (OpacitySlider != null) OpacitySlider.Value = val;
        MuPDFRenderer.InvalidateVisual();
        MuPDFRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }
}

