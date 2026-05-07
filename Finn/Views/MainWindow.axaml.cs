using Finn.ViewModels;
using Avalonia.Controls;
using Finn.Dialogs;
using System.ComponentModel;
using System.Diagnostics;


namespace Finn.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{

    public bool ConfirmLeave { get; set; } = true;
    private bool _closeCheckInProgress;

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
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (this.DataContext is not MainViewModel ctx) return;

        // When the close dialog has already made its decision (Save / Leave),
        // it sets ConfirmLeave = false and calls Close() again — let it through.
        if (!ConfirmLeave)
        {
            ctx.StopFolderWatchers();
            await ctx.PreviewVM.SafeDisposeAsync();
            if (ctx.PreviewWindowOpen)
                ctx.PreviewWindowOpen = false;
            return;
        }

        if (_closeCheckInProgress)
        {
            e.Cancel = true;
            return;
        }

        // Cancel immediately so the window stays open while we do async work.
        // Without this, the first await yields back to the framework which
        // sees e.Cancel == false and closes the window before we get a chance
        // to show the save prompt.
        e.Cancel = true;
        _closeCheckInProgress = true;

        try
        {
            await ctx.Calendar.SaveStorageAsync(MainViewModel.SavePath);

            if (await ctx.IsStorageDifferentFromFileAsync())
            {
                OpenClosingDia();
            }
            else
            {
                // No unsaved project changes — close for real.
                await ctx.PreviewVM.SafeDisposeAsync();
                if (ctx.PreviewWindowOpen)
                    ctx.PreviewWindowOpen = false;
                ConfirmLeave = false;
                _closeCheckInProgress = false;
                Close();
            }
        }
        finally
        {
            if (ConfirmLeave)
                _closeCheckInProgress = false;
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