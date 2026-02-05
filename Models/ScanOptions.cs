using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Common;

namespace Declutterer.Models;

public partial class ScanOptions : ObservableObject
{
    [ObservableProperty]
    private ObservableHashSet<string> _directoriesToScan = new();
    
    [ObservableProperty]
    private AgeFilter _ageFilter = new();
    
    [ObservableProperty]
    private EntrySizeFilter _entrySizeFilter = new();
}