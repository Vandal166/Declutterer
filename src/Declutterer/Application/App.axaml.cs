using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Declutterer.Abstractions;
using Declutterer.UI.Services.Interaction;
using Declutterer.UI.ViewModels;
using Declutterer.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Declutterer.Application;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
#if DEBUG
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
#else
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Fatal()
        .CreateLogger();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Creating the DI container
        var services = new ServiceCollection();
        
        services.AddCommonServices(); // registering our services and viewmodels
        
        var serviceProvider = services.BuildServiceProvider();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) // if running as a desktop application(not mobile or web)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var mainWindow = new MainWindow(serviceProvider.GetRequiredService<TreeGridInteractionService>(), serviceProvider.GetRequiredService<INavigationService>(), 
                serviceProvider.GetRequiredService<IClipboardService>(), serviceProvider.GetRequiredService<IErrorDialogService>())
            {
                DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>()
            };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}