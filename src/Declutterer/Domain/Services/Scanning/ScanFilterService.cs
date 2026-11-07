using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Scanning;

public sealed class ScanFilterService
{
    private readonly ScanFilterBuilder _filterBuilder;
    private readonly object _gate = new(); // the builder is shared (singleton) and not thread safe

    public ScanFilterService(ScanFilterBuilder filterBuilder)
    {
        _filterBuilder = filterBuilder;
    }

    /// <summary>
    /// Creates a filter function based on the provided scan options.
    /// </summary>
    /// <param name="options"></param>
    /// <returns>A function that takes a TreeNode and returns true if it passes the filter criteria, false otherwise.</returns>
    public Func<FileSystemInfoWrapper, bool>? CreateFilter(ScanOptions? options)
    {
        if(options is null)
            return null; // No filter if options are null

        lock (_gate)
            return CreateFilterCore(options);
    }

    private Func<FileSystemInfoWrapper, bool> CreateFilterCore(ScanOptions options)
    {
        _filterBuilder.Clear();
        
        if (options.AgeFilter is { UseModifiedDate: true, ModifiedBefore: not null })
        {
            _filterBuilder.WithModifiedDateFilter(options.AgeFilter.ModifiedBefore.Value);
        }
        
        if (options.AgeFilter is { UseAccessedDate: true, AccessedBefore: not null })
        {
            _filterBuilder.WithAccessedDateFilter(options.AgeFilter.AccessedBefore.Value);
        }
        
        if(options.IncludeFiles) // if files are to be included
        {
            // we only apply the file size filter if files are included, otherwise it would be redundant since all files would be excluded anyway
            if (options.FileSizeFilter is { UseSizeFilter: true, SizeThreshold: > 0 })
            {
                long sizeThresholdInBytes = options.FileSizeFilter.SizeThreshold * 1024 * 1024; // from MB to Bytes
                _filterBuilder.WithFileSizeFilter(sizeThresholdInBytes);
            }
        }
        else // if files are not to be included
        {
            _filterBuilder.WithIncludeFiles(false);
        }
        
        if (options.DirectorySizeFilter is { UseSizeFilter: true, SizeThreshold: > 0 })
        {
            long sizeThresholdInBytes = options.DirectorySizeFilter.SizeThreshold * 1024 * 1024; // from MB to Bytes
            _filterBuilder.WithDirectorySizeFilter(sizeThresholdInBytes);
        }
        
        return _filterBuilder.Build();
    }
}