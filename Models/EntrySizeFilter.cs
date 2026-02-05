using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.Models;

// not only for directories but also for files, so we can filter out large files if needed(only if include files is checked)
public partial class EntrySizeFilter : ObservableObject
{
    // TODO mby use MinMax slider
    [ObservableProperty]
    private long _sizeThreshold = 1; // in MB
    
    [ObservableProperty]
    private bool _useSizeFilter = false; // Whether to apply size filter
    
    partial void OnSizeThresholdChanged(long value)
    {
        // Auto-enable filter when user changes the threshold
        if (value > 0)
        {
            UseSizeFilter = true;
        }
    }
}