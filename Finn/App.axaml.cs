using Finn.ViewModels;
using Finn.Views;

using System;
using System.Threading.Tasks;
using Avalonia;
using Finn.Services;
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
            var vm = new MainViewModel();
            // Initialize UI service to apply theme/resources centrally
            var uiService = new UIService();
            uiService.Initialize(vm);

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var vm = new MainViewModel();
            var uiService = new UIService();
            uiService.Initialize(vm);

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
}
