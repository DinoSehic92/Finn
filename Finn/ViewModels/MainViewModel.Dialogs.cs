using Avalonia.Controls;
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
using Newtonsoft.Json;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            // Helper method for common window setup
            private void ConfigureWindow(Window window, Window mainWindow)
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
                    RequestedThemeVariant = theme
                };
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
                // Let the dialog inherit the app-level theme so toggling
                // DarkMode inside the dialog updates it in real-time.
                window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                window.FontCombo.SelectionChanged += SignalFontChanged;
                window.FontSizeCombo.SelectionChanged += SignalFontChanged;
                window.ShowDialog(mainWindow);
            }

            private void SignalFontChanged(object sender, SelectionChangedEventArgs e)
            {
                OnPropertyChanged("FontChanged");
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
                var window = new xTagDia();
                ConfigureWindow(window, mainWindow);
                window.TagMenuInput.Text = CurrentFile.Tagg;
                window.TagMenuInput.CaretIndex = window.TagMenuInput.Text.Length;
                window.ShowDialog(mainWindow);
                window.TagMenuInput.Focus();
            }

            public void OpenNewPlaceholderFile(Window mainWindow)
            {
                var window = new xPlaceholderDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
                window.NewFileName.Focus();
            }

            public void TryOpenRenameDia(Window mainWindow)
            {
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

            /// <summary>
            /// Compares the last two versions of a single selected file, or the last two
            /// versions of all selected files treated as a combined multi-page document.
            /// </summary>
            public async Task CompareLastTwoVersions(Window mainWindow)
            {
                if (CurrentFiles == null || CurrentFiles.Count == 0) { await ShowVersionError(mainWindow); return; }

                if (CurrentFiles.Count == 1)
                {
                    var file = CurrentFiles[0];
                    if (file.Versions.Count < 2) { await ShowVersionError(mainWindow); return; }
                    var v1 = file.Versions[^2];
                    var v2 = file.Versions[^1];
                    await RunDiffInPreviewer(v1.Sökväg, v2.Sökväg);
                    return;
                }

                // Multiple files — gather those with at least two versions.
                var eligible = CurrentFiles.Where(f => f.Versions.Count >= 2).ToList();

                if (eligible.Count == 0)
                {
                    // No version history — fall back to comparing latest path of first two files.
                    var fa = CurrentFiles[0];
                    var fb = CurrentFiles[1];
                    string pa = fa.Versions.Count > 0 ? fa.Versions[^1].Sökväg : fa.Sökväg;
                    string pb = fb.Versions.Count > 0 ? fb.Versions[^1].Sökväg : fb.Sökväg;
                    await RunDiffInPreviewer(pa, pb);
                    return;
                }

                if (eligible.Count == 1)
                {
                    var v1 = eligible[0].Versions[^2];
                    var v2 = eligible[0].Versions[^1];
                    await RunDiffInPreviewer(v1.Sökväg, v2.Sökväg, eligible[0]);
                    return;
                }

                // Two or more files with version history — multi-page diff.
                var pathsA = eligible.Select(f => f.Versions[^2].Sökväg).ToList();
                var pathsB = eligible.Select(f => f.Versions[^1].Sökväg).ToList();
                await RunDiffInPreviewer(pathsA, pathsB);
            }

            private async Task ShowVersionError(Window mainWindow)
            {
                var msg = new xMessageDia();
                ConfigureWindow(msg, mainWindow);
                msg.SetMessage("Select a file with at least two versions, or select two files to compare.");
                await msg.ShowDialog(mainWindow);
            }

            /// <summary>
            /// Runs a diff comparison directly in the previewer.
            /// Ensures the preview is visible before starting.
            /// </summary>
            public async Task RunDiffInPreviewer(string pathA, string pathB, FileData? sourceFile = null)
            {
                if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                    UI.PreviewEmbeddedOpen = true;

                await PreviewVM.RunDiffAsync(pathA, pathB, pathA, sourceFile ?? CurrentFile);
            }

            /// <summary>
            /// Runs a multi-file diff comparison directly in the previewer.
            /// </summary>
            public async Task RunDiffInPreviewer(IReadOnlyList<string> pathsA, IReadOnlyList<string> pathsB)
            {
                if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                    UI.PreviewEmbeddedOpen = true;

                await PreviewVM.RunDiffAsync(pathsA, pathsB);
            }
        }
    }
