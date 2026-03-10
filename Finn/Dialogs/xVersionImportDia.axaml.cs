using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Finn.Dialogs;

public partial class xVersionImportDia : Window
{
    public bool Confirmed { get; private set; }

    public xVersionImportDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
    }

    public void SetEntries(IEnumerable<VersionImportEntry> entries)
    {
        VersionGrid.ItemsSource = new ObservableCollection<VersionImportEntry>(entries);
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnBulkLabelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BulkLabelCombo.SelectedItem is not string label) return;
        if (VersionGrid.ItemsSource is ObservableCollection<VersionImportEntry> entries)
        {
            foreach (var entry in entries)
                entry.SelectedLabel = label;
        }
    }

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
