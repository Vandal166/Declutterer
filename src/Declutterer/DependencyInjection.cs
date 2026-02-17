using Declutterer.Abstractions;
using Declutterer.Launchers;
using Declutterer.Services;
using Declutterer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Declutterer;

public static class DependencyInjection
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<MainWindowViewModel>();
        collection.AddSingleton<ScanOptionsWindowViewModel>();
        collection.AddTransient<CleanupWindowViewModel>(); // Transient since each CleanupWindow gets a new instance with different selected nodes
        
        collection.AddLogging(builder => builder.AddSerilog());
        
        collection.AddSingleton<ScanFilterBuilder>();
        collection.AddSingleton<ScanFilterService>();
        collection.AddSingleton<DirectoryScanService>();
        collection.AddSingleton<IIconLoader, IconLoaderService>();
        collection.AddSingleton<IconLoadingService>();
        
        collection.AddSingleton<TreeGridInteractionService>();
        collection.AddTransient<ControlInteractionService>();
     
        collection.AddSingleton<IDispatcher, AvaloniaDispatcher>(); 
        
        collection.AddSingleton<SmartSelectionScorer>();
        collection.AddSingleton<SmartSelectionService>();
        
        collection.AddSingleton<IExplorerLauncher, WindowsExplorerLauncher>();
        collection.AddSingleton<IErrorDialogService, ErrorDialogService>();
        
        collection.AddSingleton<IContextMenuService, TreeGridContextMenuService>();
        collection.AddSingleton<ICommandService, CommandService>();
        
        collection.AddSingleton<IScanWorkflowService, ScanWorkflowService>();
        collection.AddSingleton<INavigationService, NavigationService>();
        collection.AddSingleton<ITreeNavigationService, TreeNavigationService>();
        collection.AddSingleton<IClipboardService, AvaloniaClipboardService>();
    }
}