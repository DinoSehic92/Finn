using Avalonia.Controls;
using Avalonia.Styling;
using Finn.Dialog;
using Finn.Model;
using Finn.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
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

            public void OpenWhiteboard(Window mainWindow)
            {
                var window = new xPaintDia();
                ConfigureWindow(window, mainWindow);
                window.Show();
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

            public void OnInfoDia(Window mainWindow)
            {
                var window = new xInfoDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OnIndexDia(Window mainWindow)
            {
                // Load existing indexed content from disk so the inspect dialog
                // shows previously indexed entries immediately.
                try
                {
                    string indexPath = $"{SavePath}\\Content.json";
                    if (System.IO.File.Exists(indexPath))
                    {
                        LoadIndexFile(indexPath);
                    }
                    else
                    {
                        TextContent ??= new ObservableCollection<ContentData>();
                    }
                }
                catch
                {
                    // If loading fails, ensure collection is non-null so the dialog can bind to it.
                    TextContent ??= new ObservableCollection<ContentData>();
                }

                var window = new xContentDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public async Task OpenDiffDia(Window mainWindow)
            {
                if (CurrentFiles == null || CurrentFiles.Count != 2
                    || !CurrentFiles[0].IsValidPdf() || !CurrentFiles[1].IsValidPdf())
                {
                    var msg = new xMessageDia();
                    ConfigureWindow(msg, mainWindow);
                    msg.SetMessage("Select exactly 2 PDF files to compare.");
                    await msg.ShowDialog(mainWindow);
                    return;
                }

                var vm = new DiffViewModel
                {
                    FileNameA = CurrentFiles[0].Namn,
                    FileNameB = CurrentFiles[1].Namn,
                    FilePathA = CurrentFiles[0].Sökväg,
                    FilePathB = CurrentFiles[1].Sökväg
                };

                var window = new xDiffDia
                {
                    DataContext = vm,
                    FontFamily = mainWindow.FontFamily,
                    RequestedThemeVariant = mainWindow.ActualThemeVariant
                };
                await window.ShowDialog(mainWindow);
            }
        }
    }
