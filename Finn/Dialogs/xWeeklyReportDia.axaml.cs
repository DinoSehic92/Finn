using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Finn.Model;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.Dialogs;

public partial class xWeeklyReportDia : Window
{
    public xWeeklyReportDia()
    {
        InitializeComponent();
    }

    private async void OnCopyDiaryEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is WeekDiaryEntry entry)
            await CopyTextAsync(entry.Diary);
    }

    private async void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Finn.ViewModels.MainViewModel viewModel)
            return;

        var text = string.Join(System.Environment.NewLine,
            viewModel.Calendar.WeekDiaryEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Diary))
                .Select(entry => $"{entry.Day}:{System.Environment.NewLine}{entry.Diary.Trim()}"));

        await CopyTextAsync(text);
    }

    private async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
            await topLevel.Clipboard.SetTextAsync(text);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}