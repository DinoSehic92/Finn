using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace Finn.Dialogs;

public partial class xNewGroupDia : Window
{
    public string? ResultName { get; private set; }
    public string? ResultParentGroup { get; private set; }
    public string ResultCategory { get; private set; } = "Project";

    private IReadOnlyList<(string Label, string? GroupName, string Category)> _items
        = System.Array.Empty<(string, string?, string)>();

    public xNewGroupDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
        Opened += (_, _) => GroupName.Focus();
        ParentPicker.AddHandler(ComboBox.LoadedEvent, OnParentPickerLoaded);
    }

    private void OnParentPickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;

        // GetGroupParentPickerItems returns categories + top-level groups — no sentinel to strip
        _items = ctx.GetGroupParentPickerItems();

        ParentPicker.ItemsSource = _items.Select(i => i.Label).ToList();
        // Default to the "▶  Project" category entry
        int idx = _items.ToList().FindIndex(i => i.GroupName == null && i.Category == "Project");
        ParentPicker.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        string? name = GroupName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        ResultName = name;

        int idx = ParentPicker.SelectedIndex;
        if (idx >= 0 && idx < _items.Count)
        {
            var selected = _items[idx];
            ResultParentGroup = selected.GroupName; // null = top-level in category
            ResultCategory = selected.Category;
        }

        this.Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => this.Close();

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) this.Close();
        else if (e.Key == Key.Enter) OnCreate(sender, e);
    }
}
