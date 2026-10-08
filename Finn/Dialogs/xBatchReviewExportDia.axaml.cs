using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Finn.Dialogs;

public sealed class xBatchReviewExportDia : Window
{
    public enum ExportMode
    {
        IndividualReviews,
        MergedReview,
        BindOnly
    }

    public bool Confirmed { get; private set; }
    public ExportMode SelectedMode { get; private set; }
    public string ReviewName { get; private set; } = "Review";

    private readonly RadioButton _individualOption;
    private readonly RadioButton _mergedReviewOption;
    private readonly RadioButton _bindOnlyOption;
    private readonly TextBox _reviewNameBox;

    public xBatchReviewExportDia()
    {
        Title = "Batch Review Export";
        Width = 380;
        Height = 350;
        MinWidth = 380;
        MinHeight = 350;
        MaxWidth = 380;
        MaxHeight = 350;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 30;

        _individualOption = new RadioButton { Content = "Export as individual reviews", GroupName = "ExportMode", IsChecked = true };
        _mergedReviewOption = new RadioButton { Content = "Export as one merged review PDF", GroupName = "ExportMode" };
        _bindOnlyOption = new RadioButton { Content = "Bind PDFs without review annotations", GroupName = "ExportMode" };
        _reviewNameBox = new TextBox { Text = "Review" };

        var header = new TextBlock
        {
            Text = "Batch Review Export",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var description = new TextBlock
        {
            Text = "Choose how to export the selected PDFs.",
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var options = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(16, 8),
            Children = { _individualOption, _mergedReviewOption, _bindOnlyOption }
        };
        var namePanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(16, 4),
            Children = { new TextBlock { Text = "Review name", FontSize = 12 }, _reviewNameBox }
        };
        var acceptButton = new Button { Content = "Continue", Width = 90, Classes = { "accent" } };
        var cancelButton = new Button { Content = "Cancel", Width = 90, Classes = { "normal" } };
        acceptButton.Click += OnAccept;
        cancelButton.Click += OnCancel;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 8),
            Children = { acceptButton, cancelButton }
        };
        Content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { header, description, options, namePanel, buttons }
        };

        KeyDown += CloseKey;
    }

    private void OnAccept(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SelectedMode = _bindOnlyOption.IsChecked == true
            ? ExportMode.BindOnly
            : _mergedReviewOption.IsChecked == true
                ? ExportMode.MergedReview
                : ExportMode.IndividualReviews;
        ReviewName = _reviewNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ReviewName))
            ReviewName = "Review";
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.Enter)
            OnAccept(sender, new Avalonia.Interactivity.RoutedEventArgs());
    }
}
