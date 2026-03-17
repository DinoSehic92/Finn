using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Finn.Model;
using System.Collections.Generic;

namespace Finn.Dialogs;

public partial class xVersionCompareDia : Window
{
    private DiffPathChoice? _choiceA;
    private DiffPathChoice? _choiceB;

    public DiffPathChoice? ChoiceA => _choiceA;
    public DiffPathChoice? ChoiceB => _choiceB;
    public bool Confirmed { get; private set; }

    public xVersionCompareDia()
    {
        InitializeComponent();
    }

    public void Populate(List<DiffPathChoice> choices, DiffPathChoice? preselA, DiffPathChoice? preselB)
    {
        _choiceA = preselA ?? (choices.Count > 0 ? choices[0] : null);
        _choiceB = preselB ?? (choices.Count > 1 ? choices[1] : null);

        VersionRows.Children.Clear();
        foreach (var choice in choices)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("28,28,*"),
                MinHeight = 26
            };

            var radioA = new RadioButton
            {
                GroupName = "VerA",
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Tag = choice,
                IsChecked = choice == _choiceA
            };
            radioA.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { Tag: DiffPathChoice c, IsChecked: true })
                    _choiceA = c;
            };
            Grid.SetColumn(radioA, 0);

            var radioB = new RadioButton
            {
                GroupName = "VerB",
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Tag = choice,
                IsChecked = choice == _choiceB
            };
            radioB.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { Tag: DiffPathChoice c, IsChecked: true })
                    _choiceB = c;
            };
            Grid.SetColumn(radioB, 1);

            var label = new TextBlock
            {
                Text = choice.Label,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(label, 2);

            row.Children.Add(radioA);
            row.Children.Add(radioB);
            row.Children.Add(label);
            VersionRows.Children.Add(row);
        }
    }

    private void OnCompare(object? sender, RoutedEventArgs e)
    {
        if (_choiceA == null || _choiceB == null) return;
        if (string.Equals(_choiceA.Path, _choiceB.Path, System.StringComparison.OrdinalIgnoreCase)) return;
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
