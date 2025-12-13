using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialog;

public partial class xNewDia : Window
{
    public xNewDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void OnAddProject(object sender, RoutedEventArgs e)
    {
        var Name = ProjectName.Text;
        if (Name != null)
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;

            ComboBoxItem selectedCombo = (ComboBoxItem)ProjectCategory.SelectedItem;
            string cat = "Project";

            if (selectedCombo != null)
            {
                cat = selectedCombo.Content.ToString();
            }

            string group = null;

            if (ProjectGroup.Text != null && cat == "Project") 
            {
                group = ProjectGroup.Text.ToString();
            }

            ctx.NewProject(Name, group, cat);

            ctx.UpdateTreeview();
        }

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}