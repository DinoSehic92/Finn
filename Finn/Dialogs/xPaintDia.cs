using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Skia;
using DotNetCampus.Inking;
using DotNetCampus.Inking.StrokeRenderers.WpfForSkiaInkStrokeRenderers;
using iText.Kernel.Pdf;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;

namespace Finn.Dialog;

public partial class xPaintDia : Window
{
    public xPaintDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

        ThicknessSlider.AddHandler(Slider.ValueChangedEvent, SetPenThickness);
        OpacitySlider.AddHandler(Slider.ValueChangedEvent, SetPenOpacity);

        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer = new WpfForSkiaInkStrokeRenderer();

    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

    private void ToggleStroke(object sender, RoutedEventArgs e)
    {
        if (ToggleStrokeButton.IsChecked == false)
        {
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer = null;
        }
        else
        {
            InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkStrokeRenderer = new WpfForSkiaInkStrokeRenderer();
        }
    }

    private void SetPenColor(object sender, RoutedEventArgs e)
    {
        Button button = sender as Button;
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = GetColor(button.Tag.ToString());
        InkCanvas.EditingMode = DotNetCampus.Inking.InkCanvasEditingMode.Ink;
    }

    private void SetPenThickness(object sender, RoutedEventArgs e)
    {
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkThickness = (float)ThicknessSlider.Value;
    }

    private void SetPenOpacity(object sender, RoutedEventArgs e)
    {
        byte opacity = (byte)OpacitySlider.Value;
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor.WithAlpha(opacity);
    }

    private void SetWhiteboardColor(object sender, RoutedEventArgs e)
    {
        Button button = sender as Button;
        string color = button.Tag.ToString();

        if (color == "White") { Whiteboard.Background = new SolidColorBrush(Colors.White); }
        if (color == "Antique") { Whiteboard.Background = new SolidColorBrush(Colors.AntiqueWhite); }
        if (color == "Black") { Whiteboard.Background = new SolidColorBrush(Colors.Black); }

    }

    private void UndoLine(object sender, RoutedEventArgs e)
    {
        if (InkCanvas.Strokes.Count > 0)
        {
            InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(InkCanvas.Strokes.Last());
        }
    }

    private void CleanWhiteboard(object sender, RoutedEventArgs e)
    {
        foreach (SkiaStroke stroke in InkCanvas.Strokes.ToList())
        {
            InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(stroke);
        }
    }

    private void SetEraser(object sender, RoutedEventArgs e)
    {
        InkCanvas.EditingMode = DotNetCampus.Inking.InkCanvasEditingMode.EraseByPoint;
    }

    private static SKColor GetColor(string colorName) 
    { 
        var skColor = typeof(SKColors).GetField(colorName); 

        if (skColor != null) 
        { 
            return (SKColor)skColor.GetValue(null)!; 
        } 
        return SKColors.Black; }


    private void SaveImage(object sender, RoutedEventArgs e)
    {

        string path = "C:\\FIlePathManager\\Sketches\\test.pdf";

        //Directory.CreateDirectory(path);

        using var skPaint = new SKPaint();
        {
            skPaint.IsAntialias = true;

            skPaint.Style = SKPaintStyle.Fill;

            SKRect bounds = InkCanvas.Bounds.ToSKRect();

            using SKWStream stream = SKFileWStream.OpenStream(path);
            {
                var document = SKDocument.CreatePdf(stream);

                using SKCanvas skCanvas = document.BeginPage(bounds.Width, bounds.Height);
                {
                    for (var i = 0; i < InkCanvas.Strokes.Count; i++)
                    {
                        var stroke = InkCanvas.Strokes[i];

                        skPaint.Color = stroke.Color;
                        skCanvas.DrawPath(stroke.Path, skPaint);
                    }

                    document.EndPage();
                    document.Close();

                }
                
            }
        }



    }
}