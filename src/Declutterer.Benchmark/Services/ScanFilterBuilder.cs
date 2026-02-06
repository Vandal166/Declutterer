using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Benchmark.Models;

namespace Declutterer.Benchmark.Services;

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
