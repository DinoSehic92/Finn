using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Finn.Dialogs;
using Finn.Model;
using Finn.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            // Helper method for common window setup
            internal void ConfigureWindow(Window window, Window mainWindow)
            {
                window.DataContext = this;
                window.FontFamily = mainWindow.FontFamily;
                window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
                window.Focusable = true;
            }

            public async void OpenWhiteboard(Window mainWindow)
            {
                // Ensure preview is visible
                if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                    UI.PreviewEmbeddedOpen = true;

                await PreviewVM.OpenWhiteboardAsync();
            }

            public void OpenReinforcementCalculator(Window mainWindow)
            {
                var window = new xReinDia();
                ConfigureWindow(window, mainWindow);
                window.Show();
            }

            public void OpenPreviewWindow(ThemeVariant theme)
            {
                PreviewWindow = new PreWindow()
                {
                    DataContext = this,
                    // Use Default so the window inherits the app-level theme and
                    // stays in sync when the user toggles dark mode at runtime.
                    RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default
                };
                // Sync font so disabled buttons and all controls
                // look the same as in the main window.
                if (Avalonia.Application.Current?.Resources?.TryGetResource(UI.Font, theme, out var fontRes) == true
                    && fontRes is FontFamily fontFamily)
                {
                    PreviewWindow.FontFamily = fontFamily;
                }
                PreviewWindow.FontSize = UI.FontSize;
                PreviewWindow.Show();
            }

            public void OpenInfoDia(Window mainWindow)
            {
                var window = new xProgDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenColorDia(Window mainWindow)
            {
                var window = new xColorDia();
                ConfigureWindow(window, mainWindow);
                window.FontCombo.SelectionChanged += OnFontComboChanged;
                window.FontSizeCombo.SelectionChanged += OnFontComboChanged;
                window.ShowDialog(mainWindow);
            }

            private void OnFontComboChanged(object? sender, SelectionChangedEventArgs e)
            {
                SignalFontChanged();
            }

            public void OpenMetaEditDia(Window mainWindow)
            {
                var window = new xMetaDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenProjectEditDia(Window mainWindow)
            {
                var window = new xEditDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenProjectNewDia(Window mainWindow)
            {
                var window = new xNewDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
                window.ProjectName.Focus();
            }

            public void OpenTagDia(Window mainWindow)
            {
                if (CurrentFile == null) return;
                var window = new xTagDia();
                ConfigureWindow(window, mainWindow);
                window.TagMenuInput.Text = CurrentFile.Tagg;
                window.TagMenuInput.CaretIndex = window.TagMenuInput.Text.Length;
                window.ShowDialog(mainWindow);
                window.TagMenuInput.Focus();
            }

            public void TryOpenRenameDia(Window mainWindow)
            {
                if (CurrentFile == null) return;
                if (!CurrentFile.IsLocal())
                {
                    OpenMessageDia(mainWindow);
                }
                else
                {
                    OpenRenameDia(mainWindow);
                }
            }

            public void OpenRenameDia(Window mainWindow)
            {
                if (CurrentFile == null) return;
                var window = new xRenameDia();
                ConfigureWindow(window, mainWindow);
                window.SetCurrentName(CurrentFile.Namn);
                window.NewNameInput.CaretIndex = window.NewNameInput.Text.Length;
                window.ShowDialog(mainWindow);
                window.NewNameInput.Focus();
            }

            public async Task OpenReplaceDia(Window mainWindow)
            {
                if (CurrentFile == null) return;

                var window = new xReplaceDia();
                ConfigureWindow(window, mainWindow);
                window.SetCurrentPath(CurrentFile.Sökväg);
                await window.ShowDialog(mainWindow);

                if (window.Accepted && !string.IsNullOrWhiteSpace(window.NewPath))
                {
                    ReplaceFilePath(window.NewPath, window.FileExists);
                }
            }

            public async Task OpenMessageDia(Window mainWindow)
            {
                var window = new xMessageDia();
                ConfigureWindow(window, mainWindow);
                window.SetMessage("Only available for files stored on C:\\");
                await window.ShowDialog(mainWindow);
            }

            public async Task OpenMessageDia(Window mainWindow, string message)
            {
                var window = new xMessageDia();
                ConfigureWindow(window, mainWindow);
                window.SetMessage(message);
                await window.ShowDialog(mainWindow);
            }

            public async Task ConfirmDeleteDia(Window mainWindow)
            {
                var window = new xDeleteDia();
                ConfigureWindow(window, mainWindow);
                await window.ShowDialog(mainWindow);
            }

            public async Task<bool> ShowVersionImportDialogAsync(Window mainWindow, List<VersionImportEntry> entries)
            {
                var window = new xVersionImportDia();
                ConfigureWindow(window, mainWindow);
                window.SetEntries(entries);
                await window.ShowDialog(mainWindow);
                return window.Confirmed;
            }

            public async Task<(bool confirmed, string? path, string label)> ShowManualVersionDialogAsync(Window mainWindow)
            {
                var window = new xManualVersionDia();
                ConfigureWindow(window, mainWindow);
                await window.ShowDialog(mainWindow);
                return (window.Confirmed, window.SelectedFilePath, window.SelectedLabel);
            }

            public async Task<bool> ShowDeliveryImportDialogAsync(Window mainWindow, List<DeliveryFolderEntry> entries)
            {
                var window = new xDeliveryImportDia();
                ConfigureWindow(window, mainWindow);
                window.SetEntries(entries);
                await window.ShowDialog(mainWindow);
                return window.Confirmed;
            }

            public void OnInfoDia(Window mainWindow)
            {
                var window = new xInfoDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public async Task OnIndexDia(Window mainWindow)
            {
                try
                {
                    string indexPath = $"{SavePath}\\Content.json";
                    if (System.IO.File.Exists(indexPath))
                    {
                        await Data.LoadIndexFileAsync(indexPath);
                    }
                    else
                    {
                        Data.TextContent ??= new ObservableCollection<ContentData>();
                    }
                }
                catch
                {
                    Data.TextContent ??= new ObservableCollection<ContentData>();
                }

                var window = new xContentDia();
                ConfigureWindow(window, mainWindow);
                await window.ShowDialog(mainWindow);
            }

            public async Task ShowIntegrityReportAsync(Window mainWindow)
            {
                var issues = ValidateCurrentProjectIntegrity();
                string message = BuildIntegrityReportMessage(CurrentProject?.Namn, issues);

                var window = new xMessageDia();
                ConfigureWindow(window, mainWindow);
                window.SetMessage(message);
                await window.ShowDialog(mainWindow);

                int errorCount = issues.Count(i => i.Severity == IntegrityIssueSeverity.Error);
                PreviewVM.StatusMessage = errorCount > 0
                    ? "Integrity check found blocking issues"
                    : issues.Count > 0
                        ? "Integrity check found warnings"
                        : "Integrity check passed";
            }

            /// <summary>
            /// Opens diff mode in the previewer with the two selected paths.
            /// Enters dual view immediately (SideBySide) so the user can see
            /// both files right away. The slow pixel comparison can be triggered
            /// from the toolbar's Compare button.
            /// </summary>
            public void RunDiffInPreviewer(string pathA, string pathB, FileData? sourceFile = null)
            {
                if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                    UI.PreviewEmbeddedOpen = true;

                var src = sourceFile ?? CurrentFile;
                PreviewVM.DiffChoiceA = new DiffPathChoice(Path.GetFileNameWithoutExtension(pathA), pathA);
                PreviewVM.DiffChoiceB = new DiffPathChoice(Path.GetFileNameWithoutExtension(pathB), pathB);
                PreviewVM.DiffSourceFile = src;
                if (PreviewVM.DiffViewMode == DiffViewMode.Overlay)
                    PreviewVM.DiffViewMode = DiffViewMode.SideBySide;
                PreviewVM.EnterDiffView(pathA, pathB, src);
            }
        }
    }
