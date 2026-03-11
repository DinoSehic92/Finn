using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Finn.Dialogs;

public partial class xDeliveryImportDia : Window
{
    public bool Confirmed { get; private set; }

    public xDeliveryImportDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
    }

    public void SetEntries(IEnumerable<DeliveryFolderEntry> entries)
    {
        DeliveryGrid.ItemsSource = new ObservableCollection<DeliveryFolderEntry>(entries);
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

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
