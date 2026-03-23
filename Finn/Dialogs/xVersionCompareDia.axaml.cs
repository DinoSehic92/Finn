using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Finn.Model;
using Finn.ViewModels;
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

        // Read corner radius from the UI settings if available (set after InitializeComponent).
        var ui = (DataContext as MainViewModel)?.UI;
        var rowCornerRadius = ui?.CornerRadius ?? new CornerRadius(4);

        VersionRows.Children.Clear();
        foreach (var choice in choices)
        {
            // Wrap each row in a Border so it picks up CornerRadius + hover highlight.
            var rowBorder = new Border
            {
                CornerRadius = rowCornerRadius,
                Padding = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent
            };

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
                {
                    _choiceA = c;
                    CompareBtn.Content = "Compare";
                }
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
                {
                    _choiceB = c;
                    CompareBtn.Content = "Compare";
                }
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
            rowBorder.Child = row;
            VersionRows.Children.Add(rowBorder);
        }
    }

    private void OnCompare(object? sender, RoutedEventArgs e)
    {
        if (_choiceA == null || _choiceB == null) return;
        if (string.Equals(_choiceA.Path, _choiceB.Path, System.StringComparison.OrdinalIgnoreCase))
        {
            CompareBtn.Content = "Same version!";
            return;
        }
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }

    private async void OnDiagnose(object? sender, RoutedEventArgs e)
    {
        string? path = _choiceA?.Path;
        if (string.IsNullOrEmpty(path))
        {
            DiagnoseBtn.Content = "⚠ Select an A version first";
            return;
        }

        DiagnoseBtn.Content = "Running…";
        DiagnoseBtn.IsEnabled = false;

        string outPath;
        try
        {
            outPath = await System.Threading.Tasks.Task.Run(
                () => Finn.Services.TextDiffDiagnostics.RunDiagnostics(path));
        }
        catch (System.Exception ex)
        {
            DiagnoseBtn.Content = "🔍 Run Stripping Diagnostics on A";
            DiagnoseBtn.IsEnabled = true;
            ShowInfo($"Diagnostics failed: {ex.Message}");
            return;
        }

        DiagnoseBtn.Content = "🔍 Run Stripping Diagnostics on A";
        DiagnoseBtn.IsEnabled = true;

        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outPath) { UseShellExecute = true }); }
        catch { }

        ShowInfo($"Diagnostics written to:\n{outPath}");
    }

    private void ShowInfo(string message)
    {
        var win = new Window
        {
            Title = "Diagnostics",
            Width = 480,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Width = 80 }
                }
            }
        };
        ((Button)((Avalonia.Controls.StackPanel)win.Content!).Children[1]).Click += (_, _) => win.Close();
        win.ShowDialog(this);
    }
}
