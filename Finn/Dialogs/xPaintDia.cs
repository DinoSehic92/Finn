using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DotNetCampus.Inking;
using Finn.ViewModels;
using Org.BouncyCastle.Asn1.BC;
using SkiaSharp;
using System.Diagnostics;
using System.Linq;

namespace Finn.Dialog;

public partial class xPaintDia : Window
{
    public xPaintDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

    private void SetPenColor(object sender, RoutedEventArgs e)
    {
        Button button = sender as Button;
        InkCanvas.AvaloniaSkiaInkCanvas.Settings.InkColor = GetColor(button.Tag.ToString());
        InkCanvas.EditingMode = DotNetCampus.Inking.InkCanvasEditingMode.Ink;
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
        InkCanvas.AvaloniaSkiaInkCanvas.RemoveStaticStroke(InkCanvas.Strokes.Last());
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

}