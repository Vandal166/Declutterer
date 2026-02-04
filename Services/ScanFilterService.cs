using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Models;

namespace Declutterer.Services;

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

public sealed class ScanFilterBuilder
{
    private readonly List<Func<TreeNode, bool>> _criteria = new();
    
    public ScanFilterBuilder WithModifiedDateFilter(DateTime modifiedBefore)
    {
        _criteria.Add(node => node.LastModified < modifiedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithAccessedDateFilter(DateTime accessedBefore)
    {
        _criteria.Add(node => node.LastAccessed < accessedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithSizeFilter(long sizeThresholdInBytes)
    {
        _criteria.Add(node => node.Size <= sizeThresholdInBytes);
        return this;
    }
    
    public Func<TreeNode, bool> Build()
    {
        // combine all criteria using AND logic and return the resulting function
        return node => _criteria.All(criterion => criterion(node));
    }
}