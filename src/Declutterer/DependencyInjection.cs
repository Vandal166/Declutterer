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
        
        collection.AddLogging(builder => builder.AddSerilog());
        
        collection.AddSingleton<ScanFilterService>();
        collection.AddSingleton<DirectoryScanService>();
    }
}