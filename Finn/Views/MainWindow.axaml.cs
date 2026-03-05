using Finn.ViewModels;
using Avalonia.Controls;
using Finn.Dialog;
using System.ComponentModel;
using System.Diagnostics;


namespace Finn.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{

    public bool confirmLeave = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel ctx)
        {
            ctx.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDirty))
        {
            var ctx = (MainViewModel)DataContext!;
            Title = ctx.IsDirty ? "Finn  ●" : "Finn";
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        ctx.Calendar.SaveStorage(MainViewModel.SavePath);

        if (ctx.IsStorageDifferentFromFile())
        {
            if (confirmLeave)
            {
                e.Cancel = true;
                OpenClosingDia();
            }
            else
            {

                if (ctx.PreviewWindowOpen)
                {
                    ctx.PreviewWindowOpen = false;
                }

                e.Cancel = false;
            }
        }
    }

    public void OpenClosingDia()
    {

        var window = new xCloseDia()
        {
            DataContext = (MainViewModel)this.DataContext
        };

        window.SetMainWindow(this);

        window.RequestedThemeVariant = this.ActualThemeVariant;
        window.ShowDialog(this);
    }


}