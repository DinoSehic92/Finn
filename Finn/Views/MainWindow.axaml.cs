using Finn.ViewModels;
using Avalonia.Controls;
using Finn.Dialogs;
using System.ComponentModel;
using System.Diagnostics;


namespace Finn.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{

    public bool ConfirmLeave { get; set; } = true;

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

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (this.DataContext is not MainViewModel ctx) return;
        await ctx.Calendar.SaveStorageAsync(MainViewModel.SavePath);

        if (ctx.IsStorageDifferentFromFile())
        {
            if (ConfirmLeave)
            {
                e.Cancel = true;
                OpenClosingDia();
            }
            else
            {
                await ctx.PreviewVM.SafeDisposeAsync();

                if (ctx.PreviewWindowOpen)
                {
                    ctx.PreviewWindowOpen = false;
                }

                e.Cancel = false;
            }
        }
        else
        {
            await ctx.PreviewVM.SafeDisposeAsync();
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