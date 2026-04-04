using Finn.ViewModels;
using Finn.Views;

using System;
using System.Threading.Tasks;
using Avalonia;
// Finn.Services removed: UIService moved into App/ViewModel logic
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
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
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = InitializeViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
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
            if (e.PropertyName == "Color1" || e.PropertyName == "Color2" || e.PropertyName == "Color3" || e.PropertyName == "Color4" || e.PropertyName == "DarkMode")
            {
                vm.UI.ApplyTheme();
            }
        };

        return vm;
    }
}
