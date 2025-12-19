using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Org.BouncyCastle.Asn1.BC;
using SkiaSharp;
using System.Diagnostics;

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