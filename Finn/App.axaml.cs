using Finn.ViewModels;
using Finn.Views;

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Finn;

public partial class App : Application
{
    private static bool _themeApplyQueued;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register global handlers first so any exception thrown during
        // initialization (window creation, viewmodel setup, etc.) is captured.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Finn.Utils.ErrorLogger.Log(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Finn.Utils.ErrorLogger.Log(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = InitializeViewModel();
            var splash = new SplashWindow();
            var main = new MainWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(-30000, -30000),
                ShowActivated = false,
                Opacity = 0
            };

            desktop.MainWindow = main;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            // Show splash first, then open the hidden main window so its
            // controls load and InitStartup can run. InitStartup will move
            // the window to center and close the splash when data is ready.
            splash.Show();
            main.Show();

            vm.SplashWindow = splash;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var vm = InitializeViewModel();
            singleViewPlatform.MainView = new MainView
            {
                DataContext = vm
            };
        }


        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel InitializeViewModel()
    {
        var vm = new MainViewModel();
        // Load persisted UI settings synchronously — file is small and must be
        // applied before the first frame renders to avoid a flash of default theme.
        try
        {
            var file = System.IO.Path.Combine(MainViewModel.SavePath, "UISettings.json");
            if (System.IO.File.Exists(file))
            {
                var json = System.IO.File.ReadAllText(file);
                var ui = Finn.Utils.JsonHelper.Deserialize<Finn.Storage.UIStorage>(json);
                if (ui != null)
                {
                    vm.UI.FromStorage(ui);
                    // Re-write UISettings.json immediately to strip any legacy fields
                    // (e.g. tray flags that were moved to UIState.json). This is safe
                    // because ToStorage() only serialises the current schema.
                    try
                    {
                        string clean = Finn.Utils.JsonHelper.Serialize(vm.UI.ToStorage());
                        System.IO.File.WriteAllText(file, clean);
                    }
                    catch { /* best-effort */ }
                }
            }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "Failed to load UISettings"); }

        // Load UIState.json (panel/tray visibility + per-project state).
        // Applied before the first frame to avoid a flash of default layout.
        try
        {
            var uiState = Finn.ViewModels.MainViewModel.LoadUIState();
            if (uiState != null)
            {
                vm.CurrentUIState = uiState;
                vm.ApplyUIStatePanels(uiState);
            }
        }
        catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, "Failed to load UIState"); }

        // All persisted state has been applied — allow UIState writes from here on.
        vm.MarkUIStateReady();

        // Suppress individual property-changed theme updates while we apply the
        // full theme once — avoids N redundant ApplyTheme calls during FromStorage.
        vm.UI.ApplyTheme();

        vm.UI.PropertyChanged += (s, e) =>
        {
            if (vm.UI.SuppressThemeUpdates) return;
            switch (e.PropertyName)
            {
                case "Color1" or "Color2" or "Color3" or "Color4" or "DarkMode"
                    or "CornerRadius" or "Shadow" or "BorderThickness"
                    or "DarkTextColorEnabled"  or "DarkTextColor"
                    or "DarkPanelColorEnabled" or "DarkPanelColor"
                    or "DarkBorderColorEnabled" or "DarkBorderColor"
                    or "LightTextColorEnabled"  or "LightTextColor"
                    or "LightPanelColorEnabled" or "LightPanelColor"
                    or "LightBorderColorEnabled" or "LightBorderColor":
                    QueueThemeApply(vm.UI);
                    break;
            }
        };

        return vm;
    }

    private static void QueueThemeApply(UISettingsViewModel ui)
    {
        if (_themeApplyQueued)
            return;

        _themeApplyQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _themeApplyQueued = false;
            ui.ApplyTheme();
        }, DispatcherPriority.Background);
    }
}
