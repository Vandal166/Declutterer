using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Benchmark.Common;

namespace Declutterer.Benchmark.Models;

public partial class ScanOptions : ObservableObject
{
    [ObservableProperty]
    private ObservableHashSet<string> _directoriesToScan = new();
    
    [ObservableProperty]
    private AgeFilter _ageFilter = new();
    
    [ObservableProperty]
    private EntrySizeFilter _entrySizeFilter = new();
}
