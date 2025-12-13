using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Finn.Dialog;

public partial class xEditDia : Window
{
    public xEditDia()
    {
        InitializeComponent();

        ProjectCategory.AddHandler(ComboBox.LoadedEvent, SetupCategory);

        KeyDown += CloseKey;

    }

    private void SetupCategory(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        string cat = ctx.CurrentProject.Category;

        ComboBoxItem comboBoxItem = null;

        foreach( ComboBoxItem item in ProjectCategory.Items)
        {
            if(item.Content.ToString() == cat)
            {
                comboBoxItem = item;
            }
        }

        ProjectCategory.SelectedItem = comboBoxItem;

    }

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        if (ProjectName.Text != null)
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;
            ctx.RenameProject(ProjectName.Text.ToString());

            string group = null;

            if (ProjectGroup.Text != null)
            {
                group = ProjectGroup.Text.ToString();
            }

            ctx.SetGroup(group);

            ComboBoxItem selectedCombo = (ComboBoxItem)ProjectCategory.SelectedItem;

            ctx.SetCategory(selectedCombo.Content.ToString());

            ctx.UpdateTreeview();
        }

        this.Close();
    }

    private void ResetColor(object sender, RoutedEventArgs e)
    {
        ColorPickerForeground.Color = Color.Parse("#FFFFFFFF");
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}