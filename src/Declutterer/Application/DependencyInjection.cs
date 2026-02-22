using Declutterer.Abstractions;
using Declutterer.Domain.Services;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Domain.Services.Scanning;
using Declutterer.Domain.Services.Selection;
using Declutterer.Integration.ExplorerLauncher;
using Declutterer.UI.Services.Clipboard;
using Declutterer.UI.Services.Commands;
using Declutterer.UI.Services.Dialog;
using Declutterer.UI.Services.Dispatch;
using Declutterer.UI.Services.Icons;
using Declutterer.UI.Services.Interaction;
using Declutterer.UI.Services.Navigation;
using Declutterer.UI.Services.Workflow;
using Declutterer.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Runtime.InteropServices;

namespace Declutterer.Application;

public static class DependencyInjection
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<MainWindowViewModel>();
        collection.AddSingleton<ScanOptionsWindowViewModel>();
        collection.AddTransient<CleanupWindowViewModel>(); // Transient since each CleanupWindow gets a new instance with different selected nodes
        collection.AddTransient<HistoryWindowViewModel>(); // Transient for each history window instance
        
        collection.AddLogging(builder => builder.AddSerilog());
        
        collection.AddSingleton<ScanFilterBuilder>();
        collection.AddSingleton<ScanFilterService>();
        collection.AddSingleton<DirectoryScanService>();
        collection.AddSingleton<IIconLoader, IconLoaderService>();
        collection.AddSingleton<IconLoadingScheduler>();
        
        collection.AddSingleton<TreeGridInteractionService>();
        collection.AddTransient<ControlInteractionService>();
     
        collection.AddSingleton<IDispatcher, AvaloniaDispatcher>(); 
        
        collection.AddSingleton<SmartSelectionScorer>();
        collection.AddSingleton<SmartSelectionService>();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            collection.AddSingleton<IExplorerLauncher, WindowsExplorerLauncher>();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            collection.AddSingleton<IExplorerLauncher, MacOSExplorerLauncher>();
        else
            collection.AddSingleton<IExplorerLauncher, LinuxExplorerLauncher>();
        
        collection.AddSingleton<IErrorDialogService, ErrorDialogService>();
        collection.AddSingleton<IConfirmationDialogService, ConfirmationDialogService>();
        collection.AddSingleton<IDeletionHistoryRepository, DeletionHistoryRepository>();
        collection.AddSingleton<IDeleteService, DeleteService>();
        
        collection.AddSingleton<IContextMenuService, TreeGridContextMenuService>();
        collection.AddSingleton<ICommandService, CommandService>();
        collection.AddSingleton<ISelectionManagementService, SelectionManagementService>();
        
        collection.AddSingleton<IScanWorkflowService, ScanWorkflowService>();
        collection.AddSingleton<INavigationService, NavigationService>();
        collection.AddSingleton<ITreeNavigationService, TreeNavigationService>();
        collection.AddSingleton<IClipboardService, AvaloniaClipboardService>();
    }
}