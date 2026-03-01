using Avalonia.Controls;
using Avalonia.Interactivity;
using Finn.ViewModels;

namespace Finn.Dialog
{
    public partial class xDiffDia : Window
    {
        public xDiffDia()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private DiffViewModel VM => (DiffViewModel)DataContext!;

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            await VM.RunDiffAsync();
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            VM.Cleanup();
        }

        private void OnSideBySide(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 0;
            UpdateToggles();
        }

        private void OnOverlay(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 1;
            UpdateToggles();
        }

        private void OnDiffOnly(object? sender, RoutedEventArgs e)
        {
            VM.ViewMode = 2;
            UpdateToggles();
        }

        private void UpdateToggles()
        {
            SideBySideBtn.IsChecked = VM.IsSideBySide;
            OverlayBtn.IsChecked = VM.IsOverlay;
            DiffOnlyBtn.IsChecked = VM.IsDiffOnly;
        }
    }
}
