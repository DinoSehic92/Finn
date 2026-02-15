using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Views;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Finn.Dialog;

public partial class xReinDia : TemplateWindow
{
    private int _factorA1 = 16;
    private int _factorB1 = 200;
    private int _factorA2 = 0;
    private int _factorB2 = 200;

    public xReinDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;

        foreach (var rb in GroupA1.Children.OfType<RadioButton>())
            rb.IsCheckedChanged += (s, e) => OnFactorChanged(s, ref _factorA1, () => UpdateResult(ResultText2, _factorA1, _factorB1, _factorA2, _factorB2));

        foreach (var rb in GroupB1.Children.OfType<RadioButton>())
            rb.IsCheckedChanged += (s, e) => OnFactorChanged(s, ref _factorB1, () => UpdateResult(ResultText2, _factorA1, _factorB1, _factorA2, _factorB2));

        foreach (var rb in GroupA2.Children.OfType<RadioButton>())
            rb.IsCheckedChanged += (s, e) => OnFactorChanged(s, ref _factorA2, () => UpdateResult(ResultText2, _factorA1, _factorB1, _factorA2, _factorB2));

        foreach (var rb in GroupB2.Children.OfType<RadioButton>())
            rb.IsCheckedChanged += (s, e) => OnFactorChanged(s, ref _factorB2, () => UpdateResult(ResultText2, _factorA1, _factorB1, _factorA2, _factorB2));
    }

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private static void OnFactorChanged(object? sender, ref int factor, Action update)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tag } && int.TryParse(tag, out int value))
        {
            factor = value;
            update();
        }
    }

    private static void UpdateResult(TextBlock target, int factorA1, int factorB1, int factorA2, int factorB2)
    {
        double As1 = 10 * Math.PI * (factorA1 / 2.0) * (factorA1 / 2.0) / factorB1;
        double As2 = 10 * Math.PI * (factorA2 / 2.0) * (factorA2 / 2.0) / factorB2;

        target.Text = Math.Round(As1 + As2, 2).ToString();
    }
}
