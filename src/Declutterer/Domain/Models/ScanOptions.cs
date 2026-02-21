using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Utilities.Helpers;

namespace Declutterer.Domain.Models;

public partial class ScanOptions : ObservableObject
{
    [ObservableProperty]
    private ObservableHashSet<string> _directoriesToScan = new();
    
    [ObservableProperty]
    private Domain.Models.AgeFilter _ageFilter = new();
    
    [ObservableProperty]
    private EntrySizeFilter _fileSizeFilter = new();
    
    [ObservableProperty]
    private EntrySizeFilter _directorySizeFilter = new();
    
    [ObservableProperty]
    private bool _includeFiles = true;
}