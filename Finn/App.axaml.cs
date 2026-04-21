using Finn.ViewModels;
using Finn.Views;

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Finn;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = InitializeViewModel();
            var main = new MainWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(-30000, -30000),
                ShowActivated = false
            };
            var splash = new SplashWindow();

            desktop.MainWindow = main;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            // Show splash on top as soon as the main window opens.
            main.Opened += (_, _) => splash.Show(main);

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

        // Global exception handlers to capture unexpected errors from UI or background threads
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Finn.Utils.ErrorLogger.Log(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Finn.Utils.ErrorLogger.Log(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel InitializeViewModel()
    {
        var vm = new MainViewModel();
        // Load persisted UI settings if present (one-time load at startup)
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
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to load UISettings: {ex}"); }

        // Apply theme and subscribe to UI changes to update theme live
        vm.UI.ApplyTheme();

        vm.UI.PropertyChanged += (s, e) =>
        {
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
                    vm.UI.ApplyTheme();
                    break;
            }
        };

        return vm;
    }
}
