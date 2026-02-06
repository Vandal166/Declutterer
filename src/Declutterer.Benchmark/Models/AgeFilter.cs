using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Declutterer.Benchmark.Models;

public partial class AgeFilter : ObservableObject
{
    [ObservableProperty]
    private int _monthsModifiedValue = 1; // storing months value

    [ObservableProperty]
    private DateTime? _modifiedBefore = null;

    [ObservableProperty]
    private bool _useModifiedDate = false; // Whether to apply modified date filter
    
    partial void OnMonthsAccessedValueChanged(int value)
    {
        // Auto-enable filter when user changes the months value
        if (value > 0)
        {
            UseAccessedDate = true;
        }
    }
    
    [ObservableProperty]
    private DateTime? _accessedBefore = null;
    
    [ObservableProperty]
    private int _monthsAccessedValue = 1;
    
    [ObservableProperty]
    private bool _useAccessedDate = false; // Whether to apply accessed date filter
    
    partial void OnMonthsModifiedValueChanged(int value)
    {
        // Auto-enable filter when user changes the months value
        if (value > 0)
        {
            UseModifiedDate = true;
        }
    }
}
