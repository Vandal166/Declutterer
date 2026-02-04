using Declutterer.Services;
using Declutterer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Declutterer;

public static class DependencyInjection
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<MainWindowViewModel>();
        
        collection.AddSingleton<ScanFilterService>();
        collection.AddSingleton<DirectoryScanService>();
    }
}