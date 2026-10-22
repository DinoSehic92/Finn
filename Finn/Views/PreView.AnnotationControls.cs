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
    private Grid? _propertyOpacityRow;
    private Button? _propertyClosePolyBtn;
    private Border? _propertyClosePolySeparator;
    private Button? _propertyCalibrateBtn;
    private Border? _propertyCalibrateSeparator;
    private MeasurementAnnotation? _calibrationReferenceMeasurement;
    private Button? _propertySolidBgToggleBtn;
    private StackPanel? _propertyBoundaryConditionRow;

    // Font-size undo coalescing: track the annotation and time of the last
    // size change so rapid +/- taps collapse into a single undo step.
    private TextAnnotation? _fontSizeUndoTarget;
    private object? _fontSizeUndoSnapshot;
    private DateTime _fontSizeLastChange = DateTime.MinValue;
    private const double FontSizeCoalesceMs = 800;

    // Opacity slider undo coalescing: same approach — capture snapshot on first
    // movement, coalesce subsequent moves within the window into a single undo step.
    // _opacityUndoSelection tracks which items were snapshotted so a selection change
    // always starts a fresh window even within the time threshold.
    private List<object>? _opacityUndoSnapshots;
    private HashSet<object>? _opacityUndoSelection;
    private DateTime _opacityLastChange = DateTime.MinValue;
    private const double OpacityCoalesceMs = 600;
    private bool _isUpdatingDotSpacingSlider;
    private bool _isUpdatingRebarBarsSpacingSlider;

    private void OnAnnotateColor(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string colorName)
        {
            var color = ColorPalette.GetValueOrDefault(colorName, ColorPalette["Red"]);
            AnnotationRenderer.StrokeColor = color;
            if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
                _pendingTextColor = color;

            if (AnnotationRenderer.ActiveLayer != null)
                AnnotationRenderer.ActiveLayer.Color = color;

            // Apply to currently selected annotation
            ApplyColorToSelection(color);

            SetActiveColorButton(btn);
            UpdateColorIndicator(color);
            UpdateAnnotationStatusHint();
            AnnotationRenderer.Focus();
        }
    }

    private void OnAnnotateArrowHead(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string styleName }
            || !Enum.TryParse(styleName, out ArrowHeadStyle style)) return;

        if (_pendingArrowOrigin.HasValue)
        {
            _pendingArrowHeadStyle = style;
            SyncArrowHeadStyleIndicator(style);
            return;
        }

        AnnotationRenderer.CurrentArrowHeadStyle = style;
        var snapshots = new List<object>();
        foreach (var item in _selectedAnnotations)
        {
            var snapshot = AnnotationRenderer.CapturePropertySnapshot(item);
            switch (item)
            {
                case ShapeAnnotation { ShapeType: InlineAnnotationTool.Arrow } arrow:
                    if (snapshot != null) snapshots.Add(snapshot);
                    arrow.ArrowHead = style;
                    break;
                case TextAnnotation { ArrowOrigin: not null } text:
                    if (snapshot != null) snapshots.Add(snapshot);
                    text.ArrowHead = style;
                    break;
            }
        }
        if (snapshots.Count > 0)
            AnnotationRenderer.PushGroupPropertyUndo(snapshots);
        if (_selectedAnnotations.Count > 0)
        {
            AnnotationRenderer.InvalidateVisual();
            AnnotationRenderer.NotifyAnnotationChanged();
        }
        SyncArrowHeadStyleIndicator(style);
    }

    private void SyncArrowHeadStyleIndicator(ArrowHeadStyle style)
    {
        var indicator = this.FindControl<TextBlock>("ArrowHeadStyleIndicator");
        if (indicator != null)
            indicator.Text = style switch
            {
                ArrowHeadStyle.Open => "▷",
                ArrowHeadStyle.Stealth => "➤",
                ArrowHeadStyle.Dot => "●",
                _ => "▶"
            };
        var button = this.FindControl<Button>("ArrowHeadStyleButton");
        if (button != null)
            ToolTip.SetTip(button, $"Arrowhead style: {style}");

        if (this.FindControl<Grid>("PropertyArrowHeadRow") is Grid row)
        {
            foreach (var option in row.Children.OfType<Button>())
            {
                bool active = option.Tag is string tag && tag == style.ToString();
                option.BorderThickness = active ? new Thickness(2) : new Thickness(0);
                option.BorderBrush = active ? Brushes.White : null;
            }
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
                var snap = AnnotationRenderer.CapturePropertySnapshot(selItem);
                if (snap != null) snaps.Add(snap);
            }
            switch (selItem)
            {
                case InkStroke s: s.Color = color; s.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.Color = color; sh.InvalidatePen(); break;
                case TextAnnotation t: t.Color = color; t.InvalidateArrowPen(); break;
                case MeasurementAnnotation m: m.Color = color; break;
            }
        }
        if (snaps.Count > 0)
            AnnotationRenderer.PushGroupPropertyUndo(snaps);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnAnnotateToolSelect(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName)
        {
            var tool = Enum.Parse<InlineAnnotationTool>(toolName);
            ApplyToolSwitch(tool);
        }
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
    }

    private void OnAnnotateWidth(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string widthStr && double.TryParse(widthStr, out double w))
        {
            if (_selectedAnnotations.Any(item => item is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition })) return;
            if (AnnotationRenderer.IsHighlighterMode)
            {
                _highlighterStrokeWidth = w * HighlighterWidthMultiplier;
                AnnotationRenderer.StrokeWidth = _highlighterStrokeWidth;
            }
            else
            {
                AnnotationRenderer.StrokeWidth = w;
                _normalStrokeWidth = w;
                _highlighterStrokeWidth = Math.Max(12, _normalStrokeWidth * HighlighterWidthMultiplier);
            }

            // Apply to currently selected annotations
            var snaps = new List<object>();
            foreach (var selItem in _selectedAnnotations)
            {
                double annotationWidth = AnnotationRenderer.IsHighlighterMode
                    ? _highlighterStrokeWidth
                    : w;
                if (selItem is InkStroke ink)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(ink);
                    if (snap != null) snaps.Add(snap);
                    ink.Width = annotationWidth; ink.InvalidatePen();
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(sh);
                    if (snap != null) snaps.Add(snap);
                    sh.StrokeWidth = annotationWidth; sh.InvalidatePen();
                }
            }
            if (snaps.Count > 0) AnnotationRenderer.PushGroupPropertyUndo(snaps);
            if (_selectedAnnotations.Count > 0) { AnnotationRenderer.InvalidateVisual(); AnnotationRenderer.NotifyAnnotationChanged(); }

            SetActiveWidthButton(btn);
            UpdateBrushSizeIndicator(AnnotationRenderer.StrokeWidth);
            UpdateAnnotationStatusHint();
        }
    }

    private void OnAnnotateDashPattern(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string patternName
            && Enum.TryParse<LineDashPattern>(patternName, out var pattern))
        {
            if (_selectedAnnotations.Count == 0 && IsStructuralLinePattern(pattern)
                && AnnotationRenderer.ActiveTool is not (InlineAnnotationTool.Line or InlineAnnotationTool.Polyline)) return;
            if (_selectedAnnotations.Any(item => item is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition })) return;
            if (_selectedAnnotations.Count > 0 && IsStructuralLinePattern(pattern)
                && _selectedAnnotations.Any(item => !SupportsStructuralLinePattern(item))) return;
            AnnotationRenderer.StrokeDashPattern = pattern;

            // Apply to currently selected annotations
            // Apply to currently selected annotations
            var snaps = new List<object>();
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is InkStroke ink)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(ink);
                    if (snap != null) snaps.Add(snap);
                    ink.DashPattern = pattern; ink.InvalidatePen();
                }
                else if (selItem is ShapeAnnotation sh)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(sh);
                    if (snap != null) snaps.Add(snap);
                    sh.DashPattern = pattern; sh.InvalidatePen();
                }
            }
            if (snaps.Count > 0) AnnotationRenderer.PushGroupPropertyUndo(snaps);
            if (_selectedAnnotations.Count > 0) { AnnotationRenderer.InvalidateVisual(); AnnotationRenderer.NotifyAnnotationChanged(); }

            SetActiveDashButton(btn);
            UpdateDashIndicator(pattern);
            UpdateAnnotationStatusHint();
        }
    }

    private Button? _activeCornerRadiusButton;
    private Button? _cornerRadiusBtn;
    private Button? _brushSizeBtn;
    private Button? _dashPatternBtn;
    private StackPanel? _fontSizeGroup;

    // Cached control references to avoid repeated FindControl tree walks
    private TextBlock? _propertyFontSizeLabel;
    private Button? _propertyFrameToggleBtn;
    private Button? _layerPickerBtn;
    private StructuralLinePatternIcon? _springLineDashIndicator;

    private static bool IsStructuralLinePattern(LineDashPattern pattern) =>
        pattern is LineDashPattern.ContinuousSpring or LineDashPattern.Rock or LineDashPattern.Soil;

    private static bool SupportsStructuralLinePattern(object item) => item switch
    {
        ShapeAnnotation shape => AnnotatedPDFRenderer.SupportsStructuralLinePattern(shape),
        InkStroke stroke => AnnotatedPDFRenderer.SupportsStructuralLinePattern(stroke),
        _ => false
    };

    private static bool SupportsPatternSide(object item) =>
        item is InkStroke { IsRebar: true } or InkStroke { IsDotLine: true } || SupportsStructuralLinePattern(item);

    private void OnAnnotateCornerRadius(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string radiusStr && double.TryParse(radiusStr, out double r))
        {
            if (AnnotationRenderer.ActiveTool == InlineAnnotationTool.Rebar)
                AnnotationRenderer.RebarCornerRadius = r;
            else
                AnnotationRenderer.ShapeCornerRadius = r;

            // Apply to currently selected annotations using the same immediate
            // capture→mutate→push pattern as OnAnnotateWidth / OnAnnotateDashPattern.
            // StagePropertyUndoSnapshot is deferred (only flushed on panel close) so
            // it must not be used here.
            var snaps = new List<object>();
            foreach (var selItem in _selectedAnnotations)
            {
                if (selItem is ShapeAnnotation sh && sh.ShapeType == InlineAnnotationTool.Rectangle)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(sh);
                    if (snap != null) snaps.Add(snap);
                    sh.CornerRadius = r;
                    sh.InvalidatePen();
                }
                else if (selItem is InkStroke { IsPolyline: true } ink)
                {
                    var snap = AnnotationRenderer.CapturePropertySnapshot(ink);
                    if (snap != null) snaps.Add(snap);
                    ink.CornerRadius = r;
                    ink.InvalidatePen();
                }
            }
            if (snaps.Count > 0) AnnotationRenderer.PushGroupPropertyUndo(snaps);
            if (_selectedAnnotations.Count > 0) { AnnotationRenderer.InvalidateVisual(); AnnotationRenderer.NotifyAnnotationChanged(); }

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
        SetActiveToolButton(FindToolbarButtonByTag(AnnotationRenderer.ActiveTool.ToString()));
        SetActiveColorButton(FindToolbarButtonByTag("Red"));
        SetActiveWidthButton(FindToolbarButtonByTag(((int)AnnotationRenderer.StrokeWidth).ToString()));
        SetActiveDashButton(FindToolbarButtonByTag(AnnotationRenderer.StrokeDashPattern.ToString()));
        SyncFlyoutIndicators();
        UpdateCornerRadiusVisibility();
        UpdateFontSizeVisibility();
    }

    /// <summary>
    /// Syncs <see cref="_normalStrokeWidth"/> and the width button highlight
    /// after a programmatic width change (e.g. keyboard shortcut).
    /// </summary>
    private void SyncWidthState()
    {
        if (!AnnotationRenderer.IsHighlighterMode)
            _normalStrokeWidth = AnnotationRenderer.StrokeWidth;
        SetActiveWidthButton(FindToolbarButtonByTag(((int)AnnotationRenderer.StrokeWidth).ToString()));
        UpdateBrushSizeIndicator(AnnotationRenderer.StrokeWidth);
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
        _springLineDashIndicator ??= this.FindControl<StructuralLinePatternIcon>("SpringLineDashIndicator");
        bool isStructuralPattern = pattern is LineDashPattern.ContinuousSpring or LineDashPattern.Rock or LineDashPattern.Soil;
        DashIndicator.IsVisible = !isStructuralPattern;
        if (_springLineDashIndicator != null)
        {
            _springLineDashIndicator.IsVisible = isStructuralPattern;
            if (isStructuralPattern) _springLineDashIndicator.Pattern = pattern;
        }
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

    private void SyncCornerRadiusIndicator(double radius)
    {
        foreach (var option in PropertyCornerRadiusRow.Children.OfType<Button>())
        {
            bool active = option.Tag is string tag
                && double.TryParse(tag, out double value)
                && Math.Abs(value - radius) < 0.001;
            option.BorderThickness = active ? new Thickness(2) : new Thickness(0);
            option.BorderBrush = active ? Brushes.White : null;
        }
    }

    /// <summary>Syncs all flyout indicators to the current renderer state.</summary>
    private void SyncFlyoutIndicators()
    {
        UpdateColorIndicator(AnnotationRenderer.StrokeColor);
        UpdateBrushSizeIndicator(AnnotationRenderer.StrokeWidth);
        UpdateDashIndicator(AnnotationRenderer.StrokeDashPattern);
        UpdateCornerIndicator(AnnotationRenderer.ActiveTool == InlineAnnotationTool.Rebar
            ? AnnotationRenderer.RebarCornerRadius
            : AnnotationRenderer.ShapeCornerRadius);
    }

    private void OnAnnotateUndo(object sender, RoutedEventArgs e) { AnnotationRenderer.Undo(); AnnotationRenderer.Focus(); }

    private void OnAnnotateRedo(object sender, RoutedEventArgs e) { AnnotationRenderer.Redo(); AnnotationRenderer.Focus(); }

    private async void OnAnnotateClear(object sender, RoutedEventArgs e)
    {
        int count = AnnotationRenderer.CurrentPageAnnotationCount;
        if (count == 0) return;
        var owner = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        if (owner == null) return;
        var mainVm = this.DataContext as Finn.ViewModels.MainViewModel
                  ?? owner.DataContext as Finn.ViewModels.MainViewModel;
        if (mainVm == null) return;
        await mainVm.ConfirmDeleteDia(owner);
        if (!mainVm.Confirmed) return;
        AnnotationRenderer.ClearPage();
    }

    private void OnOpacitySliderChanged(object? sender, RoutedEventArgs e)
    {
        if (OpacitySlider == null) return;
        AnnotationRenderer.StrokeOpacity = OpacitySlider.Value;
        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
        {
            _pendingTextOpacity = OpacitySlider.Value;
            return;
        }
        if (_selectedAnnotations.Count == 0) return;

        var now = DateTime.UtcNow;
        bool sameSelection = _opacityUndoSelection != null
            && _opacityUndoSelection.SetEquals(_selectedAnnotations);
        bool withinWindow = sameSelection
            && (now - _opacityLastChange).TotalMilliseconds < OpacityCoalesceMs;

        if (!withinWindow)
        {
            // Begin a new coalesce window: capture pre-change snapshots for all selected items.
            _opacityUndoSnapshots = new List<object>(_selectedAnnotations.Count);
            _opacityUndoSelection = new HashSet<object>(_selectedAnnotations);
            foreach (var selItem in _selectedAnnotations)
            {
                var snap = AnnotationRenderer.CapturePropertySnapshot(selItem);
                if (snap != null) _opacityUndoSnapshots.Add(snap);
            }
            if (_opacityUndoSnapshots.Count > 0)
                AnnotationRenderer.PushGroupPropertyUndo(_opacityUndoSnapshots);
        }

        _opacityLastChange = now;

        foreach (var selItem in _selectedAnnotations)
        {
            switch (selItem)
            {
                case InkStroke s: s.Opacity = OpacitySlider.Value; s.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.Opacity = OpacitySlider.Value; sh.InvalidatePen(); break;
                case TextAnnotation t: t.Opacity = OpacitySlider.Value; break;
            }
        }
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnAnnotateCustomColor(object sender, RoutedEventArgs e)
    {
        // Open a simple color input via TextBox — parse hex like "#FF6600"
        var hex = "#" + AnnotationRenderer.StrokeColor.R.ToString("X2")
                      + AnnotationRenderer.StrokeColor.G.ToString("X2")
                      + AnnotationRenderer.StrokeColor.B.ToString("X2");
        ColorInputBox.Text = hex;
        ColorInputCanvas.IsVisible = true;
        ColorInputBox.Focus();
        ColorInputBox.SelectAll();
    }

    private void OnColorInputApply(object sender, RoutedEventArgs e)
    {
        if (Color.TryParse(ColorInputBox.Text?.Trim(), out var c))
        {
            if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
            {
                _pendingTextColor = c;
                AnnotationRenderer.StrokeColor = c;
            }
            else
            {
                AnnotationRenderer.StrokeColor = c;
                if (AnnotationRenderer.ActiveLayer != null)
                    AnnotationRenderer.ActiveLayer.Color = c;
                ApplyColorToSelection(c);
            }
            // Clear any active palette highlight — custom color doesn't match a preset
            SetActiveColorButton(null);
            UpdateColorIndicator(c);
        }
        ColorInputCanvas.IsVisible = false;
        AnnotationRenderer.Focus();
    }

    private void OnColorInputCancel(object sender, RoutedEventArgs e)
    {
        ColorInputCanvas.IsVisible = false;
        AnnotationRenderer.Focus();
    }

    private void OnColorInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnColorInputApply(sender!, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { OnColorInputCancel(sender!, e); e.Handled = true; }
    }

    private void UpdateAnnotationCountBadge()
    {
        if (AnnotationRenderer.ActiveLayer is { } layer)
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
        FontSizeLabel.Text = $"{AnnotationRenderer.TextFontSize}pt";
        // Also sync the property panel label if it is open
        var propLabel = _propertyFontSizeLabel ??= this.FindControl<TextBlock>("PropertyFontSizeLabel");
        if (propLabel != null) propLabel.Text = $"{AnnotationRenderer.TextFontSize}pt";
    }

    private void UpdateUndoRedoButtons()
    {
        bool canUndo = AnnotationRenderer.CanUndoCurrentPage;
        bool canRedo = AnnotationRenderer.CanRedoCurrentPage;
        if (_undoBtn != null) { _undoBtn.Opacity = canUndo ? 1.0 : 0.35; _undoBtn.IsEnabled = canUndo; }
        if (_redoBtn != null) { _redoBtn.Opacity = canRedo ? 1.0 : 0.35; _redoBtn.IsEnabled = canRedo; }
    }

    private void OnAnnotationChanged()
    {
        MuPDFRenderer.InvalidateVisual();
        if ((pwr.TwopageMode && !pwr.DualFileMode) || pwr.IsUserDualFileMode)
            MuPDFRendererSecondary.InvalidateVisual();
        UpdateAnnotationCountBadge();
        UpdateUndoRedoButtons();
        UpdateActiveLayerLabel();
    }

    /// <summary>
    /// Populates the XAML-declared LayerPickerPanel inside the LayerPickerBtn flyout.
    /// Called each time the flyout opens. The flyout itself handles open/close and
    /// background/border styling via the global FlyoutPresenter styles in App.axaml.
    /// </summary>
    private void RebuildLayerPanel()
    {
        var panel = this.FindControl<StackPanel>("LayerPickerPanel");
        var renderer = AnnotationRenderer;
        if (panel == null || renderer.Layers.Count == 0) return;

        panel.Children.Clear();

        var layerList = renderer.Layers.ToList();
        var allSyncRows = new List<Action>();

        void SyncAllHighlights()
        {
            foreach (var sync in allSyncRows) sync();
        }

        foreach (var layer in layerList)
        {
            var capturedLayer = layer;
            bool canRemove = layerList.Count > 1;

            // Colour accent bar on the left — shows the layer colour and active state at a glance
            var accentBar = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                Background = new SolidColorBrush(capturedLayer.Color)
            };

            var nameBlock = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                MinWidth = 70,
                FontSize = 12
            };

            var countBlock = new TextBlock
            {
                FontSize = 10, Opacity = 0.45,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                MinWidth = 16,
                TextAlignment = Avalonia.Media.TextAlignment.Right,
                Margin = new Thickness(0, 0, 2, 0)
            };

            var visIcon    = new FluentIcons.Avalonia.SymbolIcon { FontSize = 12 };
            var lockIcon   = new FluentIcons.Avalonia.SymbolIcon { FontSize = 12 };
            var clearIcon  = new FluentIcons.Avalonia.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Delete,  FontSize = 12 };
            var removeIcon = new FluentIcons.Avalonia.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Dismiss, FontSize = 12 };

            var visBtn    = MakeIconButton(visIcon);
            var lockBtn   = MakeIconButton(lockIcon);
            var clearBtn  = MakeIconButton(clearIcon);
            var removeBtn = MakeIconButton(removeIcon);

            ToolTip.SetTip(visBtn,    "Toggle visibility");
            ToolTip.SetTip(lockBtn,   "Toggle lock");
            ToolTip.SetTip(clearBtn,  "Clear annotations");
            ToolTip.SetTip(removeBtn, "Remove layer");

            // Active row: layer-coloured tinted background + full-opacity accent bar
            // Inactive row: transparent background + dimmed accent bar
            var activeRowBrush = new SolidColorBrush(
                Avalonia.Media.Color.FromArgb(40,
                    capturedLayer.Color.R, capturedLayer.Color.G, capturedLayer.Color.B));

            Border? rowBorder = null;

            void SyncRow()
            {
                bool active = capturedLayer == renderer.ActiveLayer;
                nameBlock.Text       = capturedLayer.Name;
                nameBlock.FontWeight = active ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
                countBlock.Text      = capturedLayer.TotalCount > 0 ? capturedLayer.TotalCount.ToString() : "";

                accentBar.Opacity    = active ? 1.0 : 0.25;
                if (rowBorder != null)
                    rowBorder.Background = active ? activeRowBrush : Brushes.Transparent;

                visIcon.Symbol       = FluentIcons.Common.Symbol.Eye;
                visIcon.Opacity      = capturedLayer.IsVisible ? 1.0 : 0.3;
                lockIcon.Symbol      = capturedLayer.IsLocked ? FluentIcons.Common.Symbol.LockClosed : FluentIcons.Common.Symbol.LockOpen;
                lockIcon.Opacity     = capturedLayer.IsLocked ? 1.0 : 0.3;
                clearIcon.Opacity    = capturedLayer.TotalCount > 0 ? 0.7 : 0.2;
                removeIcon.Opacity   = canRemove ? 0.7 : 0.2;
            }

            allSyncRows.Add(SyncRow);

            visBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                capturedLayer.IsVisible = !capturedLayer.IsVisible;
                renderer.InvalidateVisual();
                UpdateActiveLayerLabel();
                SyncRow();
            };
            lockBtn.Click += (_, ev) =>
            {
                ev.Handled = true;
                capturedLayer.IsLocked = !capturedLayer.IsLocked;
                renderer.InvalidateVisual();
                UpdateActiveLayerLabel();
                SyncRow();
            };
            clearBtn.Click += async (_, ev) =>
            {
                ev.Handled = true;
                if (capturedLayer.TotalCount > 0)
                {
                    var owner2 = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                    var mainVm2 = owner2 != null
                        ? (this.DataContext as Finn.ViewModels.MainViewModel ?? owner2.DataContext as Finn.ViewModels.MainViewModel)
                        : null;
                    if (mainVm2 != null)
                    {
                        await mainVm2.ConfirmDeleteDia(owner2!);
                        if (!mainVm2.Confirmed) return;
                    }
                    renderer.ClearLayer(capturedLayer);
                    UpdateActiveLayerLabel();
                    SyncRow();
                }
            };
            removeBtn.Click += async (_, ev) =>
            {
                ev.Handled = true;
                if (canRemove)
                {
                    if (capturedLayer.TotalCount > 0)
                    {
                        var owner2 = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                        var mainVm2 = owner2 != null
                            ? (this.DataContext as Finn.ViewModels.MainViewModel ?? owner2.DataContext as Finn.ViewModels.MainViewModel)
                            : null;
                        if (mainVm2 != null)
                        {
                            await mainVm2.ConfirmDeleteDia(owner2!);
                            if (!mainVm2.Confirmed) return;
                        }
                    }
                    renderer.RemoveLayer(capturedLayer);
                    UpdateActiveLayerLabel();
                    RebuildLayerPanel();
                }
            };

            var iconRow = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 0,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children = { visBtn, lockBtn, clearBtn, removeBtn }
            };

            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
            Grid.SetColumn(accentBar,  0);
            Grid.SetColumn(nameBlock,  1);
            Grid.SetColumn(countBlock, 2);
            Grid.SetColumn(iconRow,    3);
            rowGrid.Children.Add(accentBar);
            rowGrid.Children.Add(nameBlock);
            rowGrid.Children.Add(countBlock);
            rowGrid.Children.Add(iconRow);

            rowBorder = new Border
            {
                Child = rowGrid,
                Padding = new Thickness(6, 3),
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            SyncRow(); // now rowBorder is set, initial sync applies background

            rowBorder.PointerPressed += (_, pev) =>
            {
                if (pev.Source is FluentIcons.Avalonia.SymbolIcon || pev.Source is Button)
                    return;
                var prevLayer = renderer.ActiveLayer;
                renderer.ActiveLayer = capturedLayer;
                // Only sync the stroke color to the new layer's default if the user has
                // not overridden it — detected by checking whether the current color still
                // matches the previously active layer's color.
                bool colorWasDefault = prevLayer == null
                    || renderer.StrokeColor == prevLayer.Color;
                if (!renderer.IsHighlighterMode && colorWasDefault)
                    renderer.StrokeColor = capturedLayer.Color;
                UpdateActiveLayerLabel();
                renderer.InvalidateVisual();
                renderer.Focus();
                SyncAllHighlights();
            };

            panel.Children.Add(rowBorder);
        }

        panel.Children.Add(new Separator { Margin = new Thickness(4, 3) });

        var addBtn = new Button
        {
            Classes = { "ghost" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(6, 3),
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new FluentIcons.Avalonia.SymbolIcon { Symbol = FluentIcons.Common.Symbol.Add, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    new TextBlock { Text = "Add Layer", FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                }
            }
        };
        addBtn.Click += (_, _) =>
        {
            OnAddLayer(null, new RoutedEventArgs());
            RebuildLayerPanel();
        };
        panel.Children.Add(addBtn);
    }

    private void InitLayerPickerFlyout()
    {
        var btn = _layerPickerBtn ??= this.FindControl<Button>("LayerPickerBtn");
        if (btn?.Flyout is Flyout flyout)
            flyout.Opened += (_, _) => RebuildLayerPanel();
    }

    /// <summary>Creates a small ghost icon-only button used inside the layer picker rows.</summary>
    private static Button MakeIconButton(Control icon)
    {
        var btn = new Button
        {
            Width = 22, Height = 22,
            Padding = new Thickness(0),
            Content = icon
        };
        btn.Classes.Add("ghost");
        return btn;
    }

    /// <summary>
    /// Called by MainView after it syncs the renderer so the taskbar label
    /// stays current when switching files.
    /// </summary>
    internal void RefreshLayerLabel() => UpdateActiveLayerLabel();

    /// <summary>Syncs the layer label to the current active layer.</summary>
    private void UpdateActiveLayerLabel()
    {
        var layer = AnnotationRenderer.ActiveLayer;
        if (layer == null) return;
        if (ActiveLayerLabel != null)
            ActiveLayerLabel.Text = layer.IsLocked ? $"{layer.Name} \U0001F512" : layer.Name;
    }

    /// <summary>Adds a new annotation layer with an auto-incremented name and makes it active.</summary>
    private void OnAddLayer(object? sender, RoutedEventArgs e)
    {
        int n = AnnotationRenderer.Layers.Count + 1;
        // Pick a cycling accent color for the new layer
        var palette = new[]
        {
            Avalonia.Media.Color.FromRgb(59, 130, 217),   // blue
            Avalonia.Media.Color.FromRgb(61, 163, 95),    // green
            Avalonia.Media.Color.FromRgb(232, 125, 47),   // orange
            Avalonia.Media.Color.FromRgb(139, 92, 246),   // purple
            Avalonia.Media.Color.FromRgb(38, 166, 154),   // teal
        };
        var color = palette[(n - 1) % palette.Length];
        AnnotationRenderer.AddLayer($"Layer {n}", color);
        UpdateActiveLayerLabel();
        AnnotationRenderer.NotifyAnnotationChanged();
        AnnotationRenderer.Focus();
    }

    /// <summary>Removes the active annotation layer after confirmation when it contains annotations.</summary>
    private async void OnRemoveLayer(object? sender, RoutedEventArgs e)
    {
        var layer = AnnotationRenderer.ActiveLayer;
        if (layer == null || AnnotationRenderer.Layers.Count <= 1) return;

        bool hasAnnotations = layer.StrokeCount > 0 || layer.ShapeCount > 0
                           || layer.TextCount > 0 || layer.MeasurementCount > 0;
        if (hasAnnotations)
        {
            var owner = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
            if (owner == null) return;
            // Reuse the app's standard confirm/delete dialog
            var mainVm = this.DataContext as Finn.ViewModels.MainViewModel
                      ?? owner.DataContext as Finn.ViewModels.MainViewModel;
            if (mainVm == null) return;
            await mainVm.ConfirmDeleteDia(owner);
            if (!mainVm.Confirmed) return;
        }

        AnnotationRenderer.RemoveLayer(layer);
        UpdateActiveLayerLabel();
        AnnotationRenderer.Focus();
    }

    private void OnFontSizeDecrease(object sender, RoutedEventArgs e)
        => AdjustFontSize(-2);

    private void OnFontSizeIncrease(object sender, RoutedEventArgs e)
        => AdjustFontSize(+2);

    /// <summary>
    /// Applies a font-size delta and coalesces rapid consecutive taps on the
    /// same annotation into a single undo entry (within <see cref="FontSizeCoalesceMs"/> ms).
    /// Only the snapshot taken at the START of the tap-burst is pushed to the
    /// undo stack, so Ctrl+Z rolls back the whole burst in one step.
    /// </summary>
    private void AdjustFontSize(int delta)
    {
        AnnotationRenderer.TextFontSize = Math.Clamp(AnnotationRenderer.TextFontSize + delta, 6, 72);
        UpdateFontSizeLabel();

        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null) return;
        if (_selectedAnnotation is not TextAnnotation t) return;

        var now = DateTime.UtcNow;
        bool sameTarget = ReferenceEquals(_fontSizeUndoTarget, t);
        bool withinWindow = (now - _fontSizeLastChange).TotalMilliseconds < FontSizeCoalesceMs;

        if (!sameTarget || !withinWindow)
        {
            // Begin a new coalesce window: capture the pre-change snapshot.
            _fontSizeUndoSnapshot = AnnotationRenderer.CapturePropertySnapshot(t);
            _fontSizeUndoTarget = t;
            // Update size AFTER capture so the snapshot holds the old value.
            t.FontSize = AnnotationRenderer.TextFontSize;
            if (_fontSizeUndoSnapshot != null)
                AnnotationRenderer.PushPropertyUndo(_fontSizeUndoSnapshot);
        }
        else
        {
            // Still within the coalesce window — just move the annotation forward,
            // no new undo entry is pushed.
            t.FontSize = AnnotationRenderer.TextFontSize;
        }

        _fontSizeLastChange = now;
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnPropertyFontSizeDecrease(object sender, RoutedEventArgs e) => AdjustFontSize(-2);
    private void OnPropertyFontSizeIncrease(object sender, RoutedEventArgs e) => AdjustFontSize(+2);

    private void OnToggleFill(object sender, RoutedEventArgs e)
    {
        AnnotationRenderer.IsFilledMode = !AnnotationRenderer.IsFilledMode;

        // Apply to all fillable items in the current selection (matches color/width/dash multi-select behaviour)
        var snaps = new List<object>();
        bool anyChanged = false;
        foreach (var selItem in _selectedAnnotations)
        {
            if (selItem is ShapeAnnotation sh
                && sh.ShapeType is InlineAnnotationTool.Rectangle
                                or InlineAnnotationTool.Ellipse
                                or InlineAnnotationTool.RevisionCloud)
            {
                var snap = AnnotationRenderer.CapturePropertySnapshot(sh);
                if (snap != null) snaps.Add(snap);
                sh.IsFilled = AnnotationRenderer.IsFilledMode;
                sh.InvalidatePen();
                anyChanged = true;
            }
            else if (selItem is InkStroke { IsPolyline: true, IsClosed: true } poly)
            {
                var snap = AnnotationRenderer.CapturePropertySnapshot(poly);
                if (snap != null) snaps.Add(snap);
                poly.IsFilled = AnnotationRenderer.IsFilledMode;
                poly.InvalidatePen();
                anyChanged = true;
            }
        }
        if (snaps.Count > 0) AnnotationRenderer.PushGroupPropertyUndo(snaps);
        if (anyChanged) { AnnotationRenderer.InvalidateVisual(); AnnotationRenderer.NotifyAnnotationChanged(); }

        SyncFillToggleButton(AnnotationRenderer.IsFilledMode);
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
        AnnotationRenderer.SnapToGrid = !AnnotationRenderer.SnapToGrid;
        SyncGridToggleButton(AnnotationRenderer.SnapToGrid);
        AnnotationRenderer.InvalidateVisual();
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
    }

    /// <summary>Syncs the grid toggle button visual state to the given value.</summary>
    private void SyncGridToggleButton(bool active) => SyncToggleButton(GridToggleBtn, active);

    private void OnToggleStatusHints(object? sender, RoutedEventArgs e)
    {
        _showAnnotationStatusHints = !_showAnnotationStatusHints;
        SyncStatusHintsToggleButton();
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
    }

    private void OnToggleStickyTool(object? sender, RoutedEventArgs e)
    {
        _stickyToolMode = !_stickyToolMode;
        SyncStickyToolButton();
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
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
        _pendingTextColor = isStickyNote ? Color.FromRgb(255, 235, 59) : AnnotationRenderer.StrokeColor;
        _pendingTextOpacity = AnnotationRenderer.StrokeOpacity;
        _pendingTextHasFrame = true;
        _pendingTextSolidBackground = true;
        _pendingArrowHeadStyle = AnnotationRenderer.CurrentArrowHeadStyle;
        _pendingTextCornerRadius = AnnotationRenderer.TextCornerRadius;

        // Use the property panel with text row visible, but hide non-text rows
        _propertyPanelTarget = null;
        PropertyStrokeRow.IsVisible = false;
        PropertyFillBtn.IsVisible = false;
        _propertyOpacityRow ??= this.FindControl<Grid>("PropertyOpacityRow");
        if (_propertyOpacityRow != null) _propertyOpacityRow.IsVisible = false;
        PropertyCornerRadiusRow.IsVisible = !isStickyNote;
        _propertyFrameToggleBtn ??= this.FindControl<Button>("PropertyFrameToggleBtn");
        if (_propertyFrameToggleBtn != null) _propertyFrameToggleBtn.IsVisible = !isStickyNote;
        _propertySolidBgToggleBtn ??= this.FindControl<Button>("PropertySolidBgToggleBtn");
        if (_propertySolidBgToggleBtn != null) _propertySolidBgToggleBtn.IsVisible = !isStickyNote;
        var arrowHeadRow = this.FindControl<Grid>("PropertyArrowHeadRow");
        if (arrowHeadRow != null) arrowHeadRow.IsVisible = arrowOrigin.HasValue;
        _propertyBoundaryConditionRow ??= this.FindControl<StackPanel>("PropertyBoundaryConditionRow");
        if (_propertyBoundaryConditionRow != null) _propertyBoundaryConditionRow.IsVisible = false;
        if (arrowOrigin.HasValue)
        {
            SyncArrowHeadStyleIndicator(_pendingArrowHeadStyle);
            SyncCornerRadiusIndicator(_pendingTextCornerRadius);
        }
        PropertyActionsRow.IsVisible = false;
        var colorRow = this.FindControl<Grid>("PropertyColorRow");
        if (colorRow != null) colorRow.IsVisible = true;
        var closePolyBtn = this.FindControl<Button>("PropertyClosePolyBtn");
        if (closePolyBtn != null) closePolyBtn.IsVisible = false;
        PropertyTextRow.IsVisible = true;
        PropertyTextBox.Text = "";
        SetActiveColorButton(FindToolbarButtonByTag(MatchColorTag(_pendingTextColor.Value)));
        PropertyOpacitySlider.Value = _pendingTextOpacity;
        _propertyFontSizeLabel ??= this.FindControl<TextBlock>("PropertyFontSizeLabel");
        if (_propertyFontSizeLabel != null) _propertyFontSizeLabel.Text = $"{AnnotationRenderer.TextFontSize}pt";
        SyncFrameToggleButton(_pendingTextHasFrame);
        SyncSolidBgToggleButton(_pendingTextSolidBackground);
        PropertyCornerRadiusRow.IsVisible = !isStickyNote;

        PositionPropertyPanel(screenPos);
        PropertyTextBox.Focus();
        UpdateAnnotationStatusHint();
    }

    private void ShowTextEdit(TextAnnotation existing, Point screenPos)
    {
        _textPlacementPdfPoint = existing.Position;
        _editingTextAnnotation = existing;
        // Position the property panel directly over the annotation for in-place editing
        var da = AnnotationRenderer.DisplayArea;
        var bounds = AnnotationRenderer.Bounds;
        if (da.Width > 0 && bounds.Width > 0)
        {
            double annotX = (existing.Position.X - da.X) / da.Width * bounds.Width;
            double annotY = (existing.Position.Y - da.Y) / da.Height * bounds.Height;
            screenPos = new Point(annotX, annotY);
        }
        // Show the property panel with text editing for the annotation
        ShowPropertyPanel(existing, screenPos);
    }

    private void OnTextInputCommit(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue)
        {
            if (_editingTextAnnotation != null)
            {
                // Allow empty text for edits — treat it as a delete request
                if (string.IsNullOrWhiteSpace(PropertyTextBox.Text))
                {
                    var annotationToDelete = _editingTextAnnotation;
                    CloseTextInput();
                    bool deleted = annotationToDelete != null
                        && AnnotationRenderer.DeleteAnnotation(annotationToDelete);
                    if (deleted)
                    {
                        _selectedAnnotation = null;
                        _selectedAnnotations.Clear();
                        AnnotationRenderer.ClearSelectHighlight();
                    }
                    AnnotationRenderer.InvalidateVisual();
                    UpdateAnnotationStatusHint();
                    if (!deleted) OnAnnotationCreationBlocked();
                    return;
                }
                if (AnnotationRenderer.CannotAnnotateActiveLayer)
                {
                    OnAnnotationCreationBlocked();
                    return;
                }
                var snap = AnnotationRenderer.CapturePropertySnapshot(_editingTextAnnotation);
                _editingTextAnnotation.Text = PropertyTextBox.Text;
                if (snap != null) AnnotationRenderer.PushPropertyUndo(snap);
            }
            else if (!string.IsNullOrWhiteSpace(PropertyTextBox.Text))
            {
                // New placement: ignore empty input
                if (_pendingStickyNote)
                    AnnotationRenderer.PlaceStickyNote(_textPlacementPdfPoint.Value, PropertyTextBox.Text,
                        _pendingTextColor ?? Color.FromRgb(255, 235, 59), _pendingTextOpacity,
                        fontSize: AnnotationRenderer.TextFontSize);
                else if (_pendingArrowOrigin.HasValue)
                    AnnotationRenderer.PlaceArrowText(_pendingArrowOrigin.Value,
                        _textPlacementPdfPoint.Value, PropertyTextBox.Text,
                        _pendingArrowHeadStyle, _pendingTextCornerRadius,
                        _pendingTextColor, _pendingTextOpacity, _pendingTextHasFrame, _pendingTextSolidBackground,
                        AnnotationRenderer.TextFontSize);
                else
                    AnnotationRenderer.PlaceText(_textPlacementPdfPoint.Value, PropertyTextBox.Text,
                        _pendingTextColor, _pendingTextOpacity, _pendingTextHasFrame,
                        _pendingTextSolidBackground, AnnotationRenderer.TextFontSize,
                        _pendingTextCornerRadius);
            }
        }

        // Capture flags before CloseTextInput() nulls them.
        bool isNewPlacement = _editingTextAnnotation == null && _textPlacementPdfPoint.HasValue
                              && !string.IsNullOrWhiteSpace(PropertyTextBox.Text);
        CloseTextInput();
        if (isNewPlacement)
        {
            if (AnnotationRenderer.CannotAnnotateActiveLayer)
                OnAnnotationCreationBlocked();
            CompleteToolPlacement();
        }
        AnnotationRenderer.InvalidateVisual();
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
        if (!double.TryParse(CalibrationValueBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double realMm) || realMm <= 0)
        {
            CalibrationValueBox.BorderBrush = Avalonia.Media.Brushes.OrangeRed;
            CalibrationValueBox.Focus();
            CalibrationValueBox.SelectAll();
            return;
        }
        CalibrationValueBox.BorderBrush = null;

        if (_calibrationReferenceMeasurement != null)
        {
            AnnotationRenderer.CalibrateFromMeasurement(_calibrationReferenceMeasurement, realMm);
            _calibrationReferenceMeasurement = null;
        }
        else
        {
            AnnotationRenderer.CalibrateFromLastMeasurement(realMm);
        }

        if (AnnotationRenderer.HasInconsistentMeasurementScales())
            AnnotationRenderer.NormalizeMeasurementScales();
        CalibrationCanvas.IsVisible = false;
        _calibrationMode = false;
        ApplyToolSwitch(InlineAnnotationTool.Select);
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
    }

    private void OnCalibrationCancel(object sender, RoutedEventArgs e)
    {
        CalibrationCanvas.IsVisible = false;
        _calibrationMode = false;
        _calibrationReferenceMeasurement = null;
        UpdateAnnotationStatusHint();
        AnnotationRenderer.Focus();
    }

    private void OnPropertyCalibrate(object? sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is not MeasurementAnnotation m) return;

        // Pre-fill the input with the measurement's current displayed distance
        // as a starting hint so the user can see what they're overriding.
        double currentMm = m.GetDistance();
        CalibrationPromptText.Text = "Enter the real-world distance for this measurement:";
        CalibrationValueBox.Text = currentMm > 0
            ? currentMm.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            : "";

        // Store the reference measurement so OnCalibrationApply knows which one to use.
        _calibrationReferenceMeasurement = m;

        ClosePropertyPanel(restoreFocus: false);
        CenterCalibrationDialog();
        CalibrationCanvas.IsVisible = true;
        CalibrationValueBox.Focus();
        CalibrationValueBox.SelectAll();
    }

    /// <summary>Centers the calibration dialog overlay in the preview area.</summary>
    private void CenterCalibrationDialog()
    {
        // Use approximate dialog size; Avalonia measures on next layout pass
        const double dialogWidth = 250;
        const double dialogHeight = 110;
        double cx = Math.Max(0, (AnnotationRenderer.Bounds.Width - dialogWidth) / 2);
        double cy = Math.Max(0, (AnnotationRenderer.Bounds.Height - dialogHeight) / 2);
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
        if (_propertyPanelTarget != null && !AnnotationRenderer.IsActiveLayerLocked)
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
            AnnotationRenderer.BringToFront(_propertyPanelTarget);
            if (pwr != null) pwr.StatusMessage = "Annotation moved to front";
        }
        ClosePropertyPanel();
    }

    private void OnPropertySendToBack(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            AnnotationRenderer.SendToBack(_propertyPanelTarget);
            if (pwr != null) pwr.StatusMessage = "Annotation sent to back";
        }
        ClosePropertyPanel();
    }

    private void OnPropertyClosePolyline(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is InkStroke { IsPolyline: true } poly)
            AnnotationRenderer.TogglePolylineClosed(poly);
        ClosePropertyPanel();
    }

    private void OnPropertyDelete(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget != null)
        {
            AnnotationRenderer.DeleteAnnotation(_propertyPanelTarget);
            _selectedAnnotation = null;
            _selectedAnnotations.Clear();
            AnnotationRenderer.ClearSelectHighlight();
        }
        ClosePropertyPanel();
    }

    private void OnAnnotateKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            if (!_middlePanning)
                AnnotationRenderer.Cursor = GetToolCursor(AnnotationRenderer.ActiveTool);
            e.Handled = true;
        }
    }

    /// <summary>
    /// When an annotation is selected, update the toolbar buttons to reflect
    /// its color, width, dash, opacity, font size, and fill state.
    /// This gives Figma-style "inspect on select" feedback.
    /// </summary>
    /// <summary>
    /// Shows the corner-radius flyout only for tools/selections that support it
    /// (Rectangle shape, closed polyline). Hides it otherwise.
    /// </summary>
    private void UpdateCornerRadiusVisibility()
    {
        _cornerRadiusBtn ??= this.FindControl<Button>("CornerRadiusBtn");
        if (_cornerRadiusBtn == null) return;

        bool visible;
        if (_selectedAnnotations.Count > 0)
        {
            // Show only for rectangles and non-area polylines; area-measure polylines don't support corner radius
            visible = _selectedAnnotations.Any(a =>
                a is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle }
                || a is InkStroke { IsPolyline: true, IsAreaMeasure: false, IsRebar: false });
        }
        else
        {
            var tool = AnnotationRenderer.ActiveTool;
            // Area measure tool creates area polylines — exclude it
            visible = tool is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Polyline;
        }
        _cornerRadiusBtn.IsVisible = visible;
        _brushSizeBtn ??= this.FindControl<Button>("BrushSizeBtn");
        _dashPatternBtn ??= this.FindControl<Button>("DashPatternBtn");
        bool hasSelection = _selectedAnnotations.Count > 0;
        bool measureOnly = hasSelection
            ? _selectedAnnotations.All(a => a is MeasurementAnnotation)
            : AnnotationRenderer.ActiveTool is InlineAnnotationTool.MeasureDistance or InlineAnnotationTool.MeasureArea;
        bool supportsDash = hasSelection
            ? _selectedAnnotations.All(a => a is InkStroke { IsRebar: false } or ShapeAnnotation { ShapeType: not (InlineAnnotationTool.BoundaryCondition or InlineAnnotationTool.Dot) })
            : AnnotationRenderer.ActiveTool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight
                or InlineAnnotationTool.Line or InlineAnnotationTool.Arrow or InlineAnnotationTool.Rectangle
                or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud
                or InlineAnnotationTool.Polyline;
        bool supportsWidth = hasSelection
            ? _selectedAnnotations.All(a => a is InkStroke or ShapeAnnotation { ShapeType: not InlineAnnotationTool.BoundaryCondition })
            : AnnotationRenderer.ActiveTool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight
                or InlineAnnotationTool.Line or InlineAnnotationTool.Arrow or InlineAnnotationTool.Rectangle
                or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud
                or InlineAnnotationTool.Polyline or InlineAnnotationTool.Rebar or InlineAnnotationTool.Dot;
        if (_brushSizeBtn != null) _brushSizeBtn.IsVisible = supportsWidth && !measureOnly;
        if (_dashPatternBtn != null) _dashPatternBtn.IsVisible = supportsDash;
        var fillToggleBtn = this.FindControl<Button>("FillToggleBtn");
        bool supportsFill = hasSelection
            ? _selectedAnnotations.All(a => a is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud }
                || a is InkStroke { IsPolyline: true, IsClosed: true })
            : AnnotationRenderer.ActiveTool is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud;
        if (fillToggleBtn != null) fillToggleBtn.IsVisible = supportsFill;
        var opacitySlider = this.FindControl<Slider>("OpacitySlider");
        bool supportsOpacity = hasSelection
            ? _selectedAnnotations.All(a => a switch
            {
                ShapeAnnotation shape => shape.ShapeType is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud,
                InkStroke stroke => stroke.IsPolyline && stroke.IsClosed && !stroke.IsRebar,
                _ => false
            })
            : AnnotationRenderer.ActiveTool is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud;
        if (opacitySlider != null) opacitySlider.IsVisible = supportsOpacity;
        _propertyOpacityRow ??= this.FindControl<Grid>("PropertyOpacityRow");
        if (_propertyOpacityRow != null) _propertyOpacityRow.IsVisible = supportsOpacity || _propertyPanelTarget is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition };
    }

    /// <summary>
    /// Shows the font-size group only when the active tool or selection is text-based.
    /// </summary>
    private void UpdateFontSizeVisibility()
    {
        _fontSizeGroup ??= this.FindControl<StackPanel>("FontSizeGroup");
        if (_fontSizeGroup == null) return;

        bool visible;
        if (_selectedAnnotations.Count > 0)
        {
            visible = _selectedAnnotations.Any(a => a is TextAnnotation);
        }
        else
        {
            var tool = AnnotationRenderer.ActiveTool;
            visible = tool is InlineAnnotationTool.Text
                           or InlineAnnotationTool.ArrowText
                           or InlineAnnotationTool.StickyNote;
        }
        _fontSizeGroup.IsVisible = visible;
    }

    private void SyncToolbarToSelection()
    {
        if (_selectedAnnotation == null) return;

        Color? color = null;
        double? width = null;
        LineDashPattern? dash = null;
        double? opacity = null;
        bool supportsStructuralLinePatterns = false;

        switch (_selectedAnnotation)
        {
            case InkStroke s:
                color = s.Color; width = s.Width; dash = s.DashPattern; opacity = s.Opacity;
                supportsStructuralLinePatterns = AnnotatedPDFRenderer.SupportsStructuralLinePattern(s);
                break;
            case ShapeAnnotation sh:
                color = sh.Color; width = sh.StrokeWidth; dash = sh.DashPattern; opacity = sh.Opacity;
                supportsStructuralLinePatterns = AnnotatedPDFRenderer.SupportsStructuralLinePattern(sh);
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
            AnnotationRenderer.StrokeColor = color.Value;
            UpdateColorIndicator(color.Value);
        }

        // Sync width button highlight
        if (width.HasValue)
        {
            SetActiveWidthButton(FindToolbarButtonByTag(((int)width.Value).ToString()));
            if (!AnnotationRenderer.IsHighlighterMode)
            {
                AnnotationRenderer.StrokeWidth = width.Value;
                _normalStrokeWidth = width.Value;
                _highlighterStrokeWidth = Math.Max(12, _normalStrokeWidth * HighlighterWidthMultiplier);
            }
            UpdateBrushSizeIndicator(width.Value);
        }

        // Sync dash pattern button highlight
        if (dash.HasValue)
        {
            if (!IsStructuralLinePattern(dash.Value) || supportsStructuralLinePatterns)
            {
                SetActiveDashButton(FindToolbarButtonByTag(dash.Value.ToString()));
                AnnotationRenderer.StrokeDashPattern = dash.Value;
                UpdateDashIndicator(dash.Value);
            }
        }

        // Sync opacity slider
        if (opacity.HasValue && OpacitySlider != null)
            OpacitySlider.Value = opacity.Value;

        // Sync font size for text annotations
        if (_selectedAnnotation is TextAnnotation txt)
        {
            AnnotationRenderer.TextFontSize = txt.FontSize;
            UpdateFontSizeLabel();
        }

        // Sync fill toggle for closed shapes and closed polylines
        if (_selectedAnnotation is ShapeAnnotation shape
            && shape.ShapeType is InlineAnnotationTool.Rectangle
                               or InlineAnnotationTool.Ellipse
                               or InlineAnnotationTool.RevisionCloud)
        {
            AnnotationRenderer.IsFilledMode = shape.IsFilled;
            SyncFillToggleButton(shape.IsFilled);
        }
        else if (_selectedAnnotation is InkStroke { IsPolyline: true, IsClosed: true } closedPoly)
        {
            AnnotationRenderer.IsFilledMode = closedPoly.IsFilled;
            SyncFillToggleButton(closedPoly.IsFilled);
        }

        UpdateCornerRadiusVisibility();
        UpdateFontSizeVisibility();
    }

    private static string? MatchColorTag(Color c) =>
        ColorNames.GetValueOrDefault(c);

    // ── Property panel (double-click to edit shape/stroke properties) ──────

    private void ShowPropertyPanel(object item, Point screenPos)
    {
        _propertyPanelTarget = item;
        SelectAnnotation(item);

        bool isDot = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Dot };
        bool isBoundaryCondition = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition };
        bool hasArrowHead = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Arrow }
            or TextAnnotation { ArrowOrigin: not null };
        bool isText = item is TextAnnotation;
        bool isMeasure = item is MeasurementAnnotation;
        bool isPolyline = item is InkStroke { IsPolyline: true, IsAreaMeasure: false, IsRebar: false, Points.Count: >= 2 };
        bool isRebar = item is InkStroke { IsRebar: true };
        bool isDotLine = item is InkStroke { IsDotLine: true };
        bool isAreaPolyline = item is InkStroke { IsPolyline: true, IsAreaMeasure: true };
        bool isGenericStroke = item is InkStroke { IsPolyline: false, IsRebar: false, IsDotLine: false };
        bool isSpringLine = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Line };
        bool isShape = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud or InlineAnnotationTool.Arrow };

        bool hasStroke = (isDot || isDotLine || isPolyline || isAreaPolyline || isGenericStroke || isShape || isSpringLine) && !isBoundaryCondition;
        bool hasDash = (isPolyline || isSpringLine || isAreaPolyline || isGenericStroke || isShape) && !isDot && !isBoundaryCondition;
        bool hasStructuralPatterns = isPolyline || isSpringLine;
        bool hasFill = (item is ShapeAnnotation sf
            && sf.ShapeType is InlineAnnotationTool.Rectangle
                            or InlineAnnotationTool.Ellipse
                            or InlineAnnotationTool.RevisionCloud)
            || item is InkStroke { IsPolyline: true, IsClosed: true };
        bool hasCornerRadius = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle }
            || item is InkStroke { IsPolyline: true, IsAreaMeasure: false, IsRebar: false, IsDotLine: false }
            || item is TextAnnotation { HasFrame: true };
        bool hasOpacity = item is ShapeAnnotation { ShapeType: InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud }
            || item is InkStroke { IsPolyline: true, IsClosed: true, IsRebar: false };

        PropertyStrokeRow.IsVisible = hasStroke || isRebar;
        var colorRow = this.FindControl<Grid>("PropertyColorRow");
        if (colorRow != null) colorRow.IsVisible = true;
        var arrowHeadRow = this.FindControl<Grid>("PropertyArrowHeadRow");
        if (arrowHeadRow != null) arrowHeadRow.IsVisible = hasArrowHead;
        if (hasArrowHead)
        {
            var arrowStyle = item switch
            {
                ShapeAnnotation shape => shape.ArrowHead,
                TextAnnotation text => text.ArrowHead,
                _ => ArrowHeadStyle.Filled
            };
            SyncArrowHeadStyleIndicator(arrowStyle);
        }
        _propertyBoundaryConditionRow ??= this.FindControl<StackPanel>("PropertyBoundaryConditionRow");
        if (_propertyBoundaryConditionRow != null) _propertyBoundaryConditionRow.IsVisible = isBoundaryCondition;
        _propertyDashRow ??= this.FindControl<StackPanel>("PropertyDashRow");
        if (_propertyDashRow != null) _propertyDashRow.IsVisible = hasDash;
        var dashSelector = this.FindControl<Button>("PropertyDashSelector");
        if (dashSelector != null) dashSelector.IsVisible = hasDash;
        foreach (var optionName in new[] { "PropertyContinuousSpringOption", "PropertyRockOption", "PropertySoilOption" })
        {
            var option = this.FindControl<Button>(optionName);
            if (option != null) option.IsVisible = hasStructuralPatterns;
        }

        var structuralPatternIcon = this.FindControl<StructuralLinePatternIcon>("PropertyStructuralPatternIcon");
        var dashIndicator = this.FindControl<Avalonia.Controls.Shapes.Line>("PropertyDashIndicator");
        LineDashPattern selectedPattern = item switch
        {
            ShapeAnnotation shape => shape.DashPattern,
            InkStroke stroke => stroke.DashPattern,
            _ => LineDashPattern.Solid
        };
        bool isStructuralPattern = IsStructuralLinePattern(selectedPattern);
        if (dashIndicator != null)
        {
            dashIndicator.IsVisible = !isStructuralPattern;
            dashIndicator.StrokeDashArray = selectedPattern switch
            {
                LineDashPattern.Dashed => new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                LineDashPattern.Dotted => new Avalonia.Collections.AvaloniaList<double> { 1, 2 },
                LineDashPattern.DashDot => new Avalonia.Collections.AvaloniaList<double> { 4, 2, 1, 2 },
                _ => null
            };
        }
        if (structuralPatternIcon != null)
        {
            structuralPatternIcon.IsVisible = isStructuralPattern;
            if (isStructuralPattern) structuralPatternIcon.Pattern = selectedPattern;
        }
        var patternSideButton = this.FindControl<Button>("PropertyPatternSideButton");
        if (patternSideButton != null)
        {
            bool hasPatternSide = SupportsPatternSide(item) && (item is InkStroke { IsRebar: true } or InkStroke { IsDotLine: true } || item switch
            {
                ShapeAnnotation shape => IsStructuralLinePattern(shape.DashPattern),
                InkStroke stroke => IsStructuralLinePattern(stroke.DashPattern),
                _ => false
            });
            patternSideButton.IsVisible = hasPatternSide;
            bool reversed = item switch
            {
                ShapeAnnotation shape => shape.StructuralPatternReversed,
                InkStroke { IsRebar: true } stroke => stroke.RebarHooksSideReversed ?? stroke.StructuralPatternReversed,
                InkStroke stroke => stroke.StructuralPatternReversed,
                _ => false
            };
            patternSideButton.BorderThickness = reversed ? new Thickness(2) : new Thickness(0);
            patternSideButton.BorderBrush = reversed ? Brushes.White : null;
            ToolTip.SetTip(patternSideButton, isRebar ? "Flip end hooks to the other side" : isDotLine ? "Flip dots to the other side" : "Switch pattern side");
        }
        var rebarBarsSideButton = this.FindControl<Button>("PropertyRebarBarsSideButton");
        if (rebarBarsSideButton != null)
        {
            rebarBarsSideButton.IsVisible = isRebar && item is InkStroke { HasRebarBars: true };
            bool barsReversed = item is InkStroke { IsRebar: true } rebarStroke
                && (rebarStroke.RebarBarsSideReversed ?? rebarStroke.StructuralPatternReversed);
            rebarBarsSideButton.BorderThickness = barsReversed ? new Thickness(2) : new Thickness(0);
            rebarBarsSideButton.BorderBrush = barsReversed ? Brushes.White : null;
        }
        PropertyFillBtn.IsVisible = hasFill;
        PropertyCornerRadiusRow.IsVisible = hasCornerRadius;
        _propertyOpacityRow ??= this.FindControl<Grid>("PropertyOpacityRow");
        if (_propertyOpacityRow != null) _propertyOpacityRow.IsVisible = hasOpacity;
        var dotSpacingRow = this.FindControl<Grid>("PropertyDotSpacingRow");
        var dotSpacingSlider = this.FindControl<Slider>("PropertyDotSpacingSlider");
        if (dotSpacingRow != null) dotSpacingRow.IsVisible = isDotLine;
        if (isDotLine && item is InkStroke dotLine && dotSpacingSlider != null)
        {
            _isUpdatingDotSpacingSlider = true;
            dotSpacingSlider.Value = dotLine.DotSpacing;
            _isUpdatingDotSpacingSlider = false;
            UpdateDotSpacingValue(dotLine.DotSpacing);
        }
        var rebarBarsRow = this.FindControl<Grid>("PropertyRebarBarsRow");
        var rebarBarsToggle = this.FindControl<CheckBox>("PropertyRebarBarsToggle");
        var rebarBarsSpacingSlider = this.FindControl<Slider>("PropertyRebarBarsSpacingSlider");
        if (rebarBarsRow != null) rebarBarsRow.IsVisible = isRebar;
        if (isRebar && item is InkStroke rebar)
        {
            if (rebarBarsToggle != null) rebarBarsToggle.IsChecked = rebar.HasRebarBars;
            if (rebarBarsSpacingSlider != null)
            {
                _isUpdatingRebarBarsSpacingSlider = true;
                rebarBarsSpacingSlider.Value = rebar.RebarBarSpacing;
                _isUpdatingRebarBarsSpacingSlider = false;
            }
            UpdateRebarBarsSpacingValue(rebar.RebarBarSpacing);
        }
        _propertyFrameToggleBtn ??= this.FindControl<Button>("PropertyFrameToggleBtn");
        if (_propertyFrameToggleBtn != null) _propertyFrameToggleBtn.IsVisible = isText && item is not TextAnnotation { IsStickyNote: true };
        _propertySolidBgToggleBtn ??= this.FindControl<Button>("PropertySolidBgToggleBtn");
        if (_propertySolidBgToggleBtn != null) _propertySolidBgToggleBtn.IsVisible = isText && item is not TextAnnotation { IsStickyNote: true };
        _propertyClosePolyBtn ??= this.FindControl<Button>("PropertyClosePolyBtn");
        _propertyClosePolySeparator ??= this.FindControl<Border>("PropertyClosePolySeparator");
        if (_propertyClosePolyBtn != null) _propertyClosePolyBtn.IsVisible = isPolyline || isAreaPolyline;
        if (_propertyClosePolySeparator != null) _propertyClosePolySeparator.IsVisible = isPolyline || isAreaPolyline;

        _propertyCalibrateBtn ??= this.FindControl<Button>("PropertyCalibrateBtn");
        _propertyCalibrateSeparator ??= this.FindControl<Border>("PropertyCalibrateSeperator");
        if (_propertyCalibrateBtn != null) _propertyCalibrateBtn.IsVisible = isMeasure;
        if (_propertyCalibrateSeparator != null) _propertyCalibrateSeparator.IsVisible = isMeasure;

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
        {
            PropertyTextBox.Text = textItem.Text;
            SyncFrameToggleButton(textItem.HasFrame);
            SyncSolidBgToggleButton(textItem.SolidBackground);
        }

        PositionPropertyPanel(screenPos + new Vector(0, 10));

        if (isText)
        {
            PropertyTextBox.Focus();
            // Pre-select the existing text so the user can immediately replace it by typing
            PropertyTextBox.SelectAll();
        }
    }

    private void OnPropertyArrowHead(object sender, RoutedEventArgs e)
    {
        if (_pendingArrowOrigin.HasValue
            && sender is Button { Tag: string pendingStyleName }
            && Enum.TryParse(pendingStyleName, out ArrowHeadStyle pendingStyle))
        {
            _pendingArrowHeadStyle = pendingStyle;
            SyncArrowHeadStyleIndicator(pendingStyle);
            return;
        }
        if (_propertyPanelTarget == null
            || sender is not Button { Tag: string styleName }
            || !Enum.TryParse(styleName, out ArrowHeadStyle style)) return;

        switch (_propertyPanelTarget)
        {
            case ShapeAnnotation { ShapeType: InlineAnnotationTool.Arrow } arrow:
                StagePropertyUndoSnapshot(arrow);
                arrow.ArrowHead = style;
                break;
            case TextAnnotation { ArrowOrigin: not null } text:
                StagePropertyUndoSnapshot(text);
                text.ArrowHead = style;
                break;
            default:
                return;
        }

        AnnotationRenderer.CurrentArrowHeadStyle = style;
        SyncArrowHeadStyleIndicator(style);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void PositionPropertyPanel(Point preferredPosition)
    {
        PropertyPanelCanvas.IsVisible = true;
        PropertyPanelBorder.Measure(AnnotationRenderer.Bounds.Size);
        var panelSize = PropertyPanelBorder.DesiredSize;
        double maxX = Math.Max(0, AnnotationRenderer.Bounds.Width - panelSize.Width);
        double maxY = Math.Max(0, AnnotationRenderer.Bounds.Height - panelSize.Height);
        Canvas.SetLeft(PropertyPanelBorder, Math.Clamp(preferredPosition.X, 0, maxX));
        Canvas.SetTop(PropertyPanelBorder, Math.Clamp(preferredPosition.Y, 0, maxY));
    }

    private void ClosePropertyPanel(bool restoreFocus = true)
    {
        PropertyPanelCanvas.IsVisible = false;
        // Restore the transparent background for normal (non-text-edit) usage
        PropertyPanelCanvas.Background = Avalonia.Media.Brushes.Transparent;
        _propertyPanelTarget = null;
        FlushPropertySessionUndo();
        UpdateAnnotationStatusHint();
        if (restoreFocus) AnnotationRenderer.Focus();
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
        if (_propertyPanelTarget == null)
        {
            if (!_textPlacementPdfPoint.HasValue || _editingTextAnnotation != null
                || sender is not Button { Tag: string textColorName }) return;
            _pendingTextColor = ColorPalette.GetValueOrDefault(textColorName, ColorPalette["Red"]);
            SetActiveColorButton(FindToolbarButtonByTag(textColorName));
            UpdateColorIndicator(_pendingTextColor.Value);
            return;
        }
        if (sender is not Button btn || btn.Tag is not string colorName) return;
        var color = ColorPalette.GetValueOrDefault(colorName, ColorPalette["Red"]);
        foreach (var selItem in _selectedAnnotations)
            StagePropertyUndoSnapshot(selItem);
        ApplyColorToSelection(color);
        AnnotationRenderer.StrokeColor = color;
        SetActiveColorButton(FindToolbarButtonByTag(colorName));
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyWidth(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (_propertyPanelTarget is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition }) return;
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
        AnnotationRenderer.StrokeWidth = w;
        _normalStrokeWidth = w;
        _highlighterStrokeWidth = Math.Max(12, _normalStrokeWidth * HighlighterWidthMultiplier);
        SetActiveWidthButton(FindToolbarButtonByTag(widthStr));
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyPatternSide(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null || !SupportsPatternSide(_propertyPanelTarget)) return;
        StagePropertyUndoSnapshot(_propertyPanelTarget);
        bool reversed;
        switch (_propertyPanelTarget)
        {
            case ShapeAnnotation shape:
                shape.StructuralPatternReversed = !shape.StructuralPatternReversed;
                reversed = shape.StructuralPatternReversed;
                break;
            case InkStroke stroke:
                if (stroke.IsRebar)
                {
                    stroke.RebarHooksSideReversed = !(stroke.RebarHooksSideReversed ?? stroke.StructuralPatternReversed);
                    reversed = stroke.RebarHooksSideReversed.Value;
                }
                else
                {
                    stroke.StructuralPatternReversed = !stroke.StructuralPatternReversed;
                    reversed = stroke.StructuralPatternReversed;
                }
                break;
            default:
                return;
        }
        if (sender is Button button)
        {
            button.BorderThickness = reversed ? new Thickness(2) : new Thickness(0);
            button.BorderBrush = reversed ? Brushes.White : null;
        }
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnPropertyDash(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget == null) return;
        if (_propertyPanelTarget is ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition }) return;
        if (sender is not Button btn || btn.Tag is not string patternName
            || !Enum.TryParse<LineDashPattern>(patternName, out var pattern)) return;
        if (IsStructuralLinePattern(pattern) && !SupportsStructuralLinePattern(_propertyPanelTarget)) return;
        foreach (var selItem in _selectedAnnotations)
        {
            if (IsStructuralLinePattern(pattern) && !SupportsStructuralLinePattern(selItem)) return;
            StagePropertyUndoSnapshot(selItem);
            switch (selItem)
            {
                case InkStroke ink: ink.DashPattern = pattern; ink.InvalidatePen(); break;
                case ShapeAnnotation sh: sh.DashPattern = pattern; sh.InvalidatePen(); break;
            }
        }
        AnnotationRenderer.StrokeDashPattern = pattern;
        SetActiveDashButton(FindToolbarButtonByTag(patternName));
        var structuralPatternIcon = this.FindControl<StructuralLinePatternIcon>("PropertyStructuralPatternIcon");
        var propertyDashIndicator = this.FindControl<Avalonia.Controls.Shapes.Line>("PropertyDashIndicator");
        bool structuralPattern = IsStructuralLinePattern(pattern);
        if (structuralPatternIcon != null)
        {
            structuralPatternIcon.IsVisible = structuralPattern;
            if (structuralPattern) structuralPatternIcon.Pattern = pattern;
        }
        if (propertyDashIndicator != null)
        {
            propertyDashIndicator.IsVisible = !structuralPattern;
            propertyDashIndicator.StrokeDashArray = pattern switch
            {
                LineDashPattern.Dashed => new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                LineDashPattern.Dotted => new Avalonia.Collections.AvaloniaList<double> { 1, 2 },
                LineDashPattern.DashDot => new Avalonia.Collections.AvaloniaList<double> { 4, 2, 1, 2 },
                _ => null
            };
        }
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
        var patternSideButton = this.FindControl<Button>("PropertyPatternSideButton");
        if (patternSideButton != null)
            patternSideButton.IsVisible = SupportsPatternSide(_propertyPanelTarget)
                && (_propertyPanelTarget is InkStroke { IsRebar: true } or InkStroke { IsDotLine: true } || IsStructuralLinePattern(pattern));
    }

    private void SyncFrameToggleButton(bool hasFrame)
    {
        _propertyFrameToggleBtn ??= this.FindControl<Button>("PropertyFrameToggleBtn");
        if (_propertyFrameToggleBtn == null) return;
        _propertyFrameToggleBtn.BorderThickness = hasFrame ? new Thickness(2) : new Thickness(0);
        _propertyFrameToggleBtn.BorderBrush = hasFrame ? Brushes.White : null;
        ToolTip.SetTip(_propertyFrameToggleBtn, hasFrame ? "Remove background & border" : "Add background & border");
    }

    private void OnPropertyFrameToggle(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
        {
            _pendingTextHasFrame = !_pendingTextHasFrame;
            SyncFrameToggleButton(_pendingTextHasFrame);
            return;
        }
        if (_propertyPanelTarget is not TextAnnotation t) return;
        StagePropertyUndoSnapshot(t);
        t.HasFrame = !t.HasFrame;
        SyncFrameToggleButton(t.HasFrame);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void SyncSolidBgToggleButton(bool solidBackground)
    {
        _propertySolidBgToggleBtn ??= this.FindControl<Button>("PropertySolidBgToggleBtn");
        if (_propertySolidBgToggleBtn == null) return;
        _propertySolidBgToggleBtn.BorderThickness = solidBackground ? new Thickness(2) : new Thickness(0);
        _propertySolidBgToggleBtn.BorderBrush = solidBackground ? Brushes.White : null;
        ToolTip.SetTip(_propertySolidBgToggleBtn, solidBackground ? "Use tinted background" : "Use solid white background");
    }

    private void OnPropertySolidBgToggle(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
        {
            _pendingTextSolidBackground = !_pendingTextSolidBackground;
            SyncSolidBgToggleButton(_pendingTextSolidBackground);
            return;
        }
        if (_propertyPanelTarget is not TextAnnotation t) return;
        StagePropertyUndoSnapshot(t);
        t.SolidBackground = !t.SolidBackground;
        SyncSolidBgToggleButton(t.SolidBackground);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
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
            AnnotationRenderer.IsFilledMode = isFilled;
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
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyBoundaryCondition(object sender, RoutedEventArgs e)
    {
        // Enum tag parsing already supports newly added variants by name.
        if (_propertyPanelTarget is not ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition } support
            || sender is not Button { Tag: string typeName }
            || !Enum.TryParse(typeName, out BoundaryConditionType type)) return;
        StagePropertyUndoSnapshot(support);
        support.BoundaryCondition = type;
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnPropertyBoundaryOrientation(object sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is not ShapeAnnotation { ShapeType: InlineAnnotationTool.BoundaryCondition } support
            || sender is not Button { Tag: string orientationName }
            || !Enum.TryParse(orientationName, out BoundaryConditionOrientation orientation)) return;
        StagePropertyUndoSnapshot(support);
        support.BoundaryOrientation = orientation;
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void ClearBoundaryConditionPreview()
    {
        var renderer = _boundaryConditionPreviewRenderer;
        _boundaryConditionPreviewRenderer = null;
        renderer?.SetBoundaryConditionPreview(null);
    }

    private void OnPropertyCornerRadius(object sender, RoutedEventArgs e)
    {
        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null && !_pendingStickyNote
            && sender is Button { Tag: string pendingRadiusText }
            && double.TryParse(pendingRadiusText, out double pendingRadius))
        {
            _pendingTextCornerRadius = pendingRadius;
            SyncCornerRadiusIndicator(pendingRadius);
            return;
        }
        if (_propertyPanelTarget == null) return;
        if (_propertyPanelTarget is TextAnnotation { IsStickyNote: true }) return;
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
                case TextAnnotation t:
                    t.CornerRadius = r; break;
            }
        }
        AnnotationRenderer.ShapeCornerRadius = r;
        SetActiveButton(ref _activeCornerRadiusButton, btn);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyOpacityChanged(object? sender, RoutedEventArgs e)
    {
        if (PropertyOpacitySlider == null) return;
        double val = PropertyOpacitySlider.Value;
        if (_textPlacementPdfPoint.HasValue && _editingTextAnnotation == null)
        {
            _pendingTextOpacity = val;
            return;
        }
        if (_propertyPanelTarget is MeasurementAnnotation) return;
        if (_propertyPanelTarget == null) return;
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
        AnnotationRenderer.StrokeOpacity = val;
        if (OpacitySlider != null) OpacitySlider.Value = val;
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
        UpdateAnnotationStatusHint();
    }

    private void OnPropertyDotSpacingChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingDotSpacingSlider || _propertyPanelTarget is not InkStroke { IsDotLine: true } dotLine)
            return;

        var slider = this.FindControl<Slider>("PropertyDotSpacingSlider");
        if (slider == null) return;
        double spacing = slider.Value;
        StagePropertyUndoSnapshot(dotLine);
        dotLine.DotSpacing = spacing;
        dotLine.InvalidateGeometry();
        UpdateDotSpacingValue(spacing);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void UpdateDotSpacingValue(double spacing)
    {
        var value = this.FindControl<TextBlock>("PropertyDotSpacingValue");
        if (value != null)
            value.Text = spacing.ToString("0");
    }

    private void OnPropertyRebarBarsToggled(object? sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is not InkStroke { IsRebar: true } rebar
            || sender is not CheckBox toggle)
            return;

        StagePropertyUndoSnapshot(rebar);
        rebar.HasRebarBars = toggle.IsChecked == true;
        var patternSideButton = this.FindControl<Button>("PropertyPatternSideButton");
        var barsSideButton = this.FindControl<Button>("PropertyRebarBarsSideButton");
        if (barsSideButton != null)
            barsSideButton.IsVisible = rebar.HasRebarBars;
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnPropertyRebarBarsSide(object? sender, RoutedEventArgs e)
    {
        if (_propertyPanelTarget is not InkStroke { IsRebar: true } rebar)
            return;
        StagePropertyUndoSnapshot(rebar);
        rebar.RebarBarsSideReversed = !(rebar.RebarBarsSideReversed ?? rebar.StructuralPatternReversed);
        if (sender is Button button)
        {
            bool reversed = rebar.RebarBarsSideReversed.Value;
            button.BorderThickness = reversed ? new Thickness(2) : new Thickness(0);
            button.BorderBrush = reversed ? Brushes.White : null;
        }
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void OnPropertyRebarBarsSpacingChanged(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingRebarBarsSpacingSlider || _propertyPanelTarget is not InkStroke { IsRebar: true } rebar)
            return;
        var slider = this.FindControl<Slider>("PropertyRebarBarsSpacingSlider");
        if (slider == null) return;

        StagePropertyUndoSnapshot(rebar);
        rebar.RebarBarSpacing = slider.Value;
        UpdateRebarBarsSpacingValue(slider.Value);
        AnnotationRenderer.InvalidateVisual();
        AnnotationRenderer.NotifyAnnotationChanged();
    }

    private void UpdateRebarBarsSpacingValue(double spacing)
    {
        var value = this.FindControl<TextBlock>("PropertyRebarBarsSpacingValue");
        if (value != null)
            value.Text = spacing.ToString("0");
    }
}

