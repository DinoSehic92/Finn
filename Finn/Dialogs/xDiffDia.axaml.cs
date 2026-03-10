using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;

namespace Finn.Dialogs
{
    public partial class xDiffDia : Window
    {
        public xDiffDia()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
            PageSlider.AddHandler(Slider.ValueChangedEvent, OnPageSliderChanged);
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

        private async void OnApplyTolerance(object? sender, RoutedEventArgs e)
        {
            await VM.ApplyToleranceAsync();
        }

        private void OnPageSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (!PageSlider.IsFocused) return;
            int target = (int)PageSlider.Value - 1;
            if (target != VM.CurrentPageIndex)
                VM.CurrentPageIndex = target;
        }

        private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.Delta.Y < 0)
                VM.NextPage();
            else if (e.Delta.Y > 0)
                VM.PreviousPage();
            e.Handled = true;
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
