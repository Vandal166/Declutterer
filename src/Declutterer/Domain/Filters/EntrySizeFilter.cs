using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.Domain.Models;

// Model representing the size filter for entries
public partial class EntrySizeFilter : ObservableObject
{
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