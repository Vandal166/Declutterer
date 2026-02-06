using System;
using Declutterer.Benchmark.Models;

namespace Declutterer.Benchmark.Services;

public sealed class ScanFilterService
{
    /// <summary>
    /// Creates a filter function based on the provided scan options.
    /// </summary>
    /// <param name="options"></param>
    /// <returns>A function that takes a TreeNode and returns true if it passes the filter criteria, false otherwise.</returns>
    public Func<TreeNode, bool> CreateFilter(ScanOptions options)
    {
        var builder = new ScanFilterBuilder();
        
        if (options.AgeFilter is { UseModifiedDate: true, ModifiedBefore: not null })
        {
            builder.WithModifiedDateFilter(options.AgeFilter.ModifiedBefore.Value);
        }
        
        if (options.AgeFilter is { UseAccessedDate: true, AccessedBefore: not null })
        {
            builder.WithAccessedDateFilter(options.AgeFilter.AccessedBefore.Value);
        }
        
        if (options.EntrySizeFilter is { UseSizeFilter: true, SizeThreshold: > 0 })
        {
            long sizeThresholdInBytes = options.EntrySizeFilter.SizeThreshold * 1024 * 1024; // from MB to Bytes
            builder.WithSizeFilter(sizeThresholdInBytes);
        }
        
        return builder.Build();
    }
}
