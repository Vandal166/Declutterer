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
        collection.AddSingleton<CleanupWindowViewModel>();
        
        collection.AddLogging(builder => builder.AddSerilog());
        
        collection.AddSingleton<ScanFilterBuilder>();
        collection.AddSingleton<ScanFilterService>();
        collection.AddSingleton<DirectoryScanService>();
        collection.AddSingleton<IIconLoader, IconLoaderService>();
        collection.AddSingleton<TreeGridInteractionService>();
        
        collection.AddSingleton<SmartSelectionScorer>();
        collection.AddSingleton<SmartSelectionService>();
    }
}