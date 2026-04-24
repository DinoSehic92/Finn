using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;
using System.Collections.Generic;
using System.Linq;

namespace Finn.Dialogs;

public partial class xNewDia : Window
{
    private IReadOnlyList<(string Label, string? GroupName, string Category)> _groupItems
        = System.Array.Empty<(string, string?, string)>();

    public xNewDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
        ProjectCategory.SelectionChanged += OnCategoryChanged;
        Opened += (_, _) => RefreshGroupPicker();
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e) => RefreshGroupPicker();

    private void RefreshGroupPicker()
    {
        if (DataContext is not MainViewModel ctx) return;
        string cat = (ProjectCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Project";
        _groupItems = ctx.GetProjectGroupPickerItems(cat);
        ProjectGroup.ItemsSource = _groupItems.Select(i => i.Label).ToList();
        ProjectGroup.SelectedIndex = 0; // "— No Group —"
    }

    private void OnAddProject(object sender, RoutedEventArgs e)
    {
        var name = ProjectName.Text;
        if (string.IsNullOrWhiteSpace(name)) { this.Close(); return; }

        MainViewModel ctx = (MainViewModel)this.DataContext;

        string cat = (ProjectCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Project";

        string? group = null;
        int idx = ProjectGroup.SelectedIndex;
        if (idx >= 0 && idx < _groupItems.Count)
            group = _groupItems[idx].GroupName; // null = no group

        ctx.NewProject(name, group, cat);
        ctx.MarkDirty();
        ctx.UpdateTreeview();

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnAddProject(sender, e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

}