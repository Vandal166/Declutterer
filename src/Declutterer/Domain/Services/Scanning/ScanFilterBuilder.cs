using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Scanning;

public sealed class ScanFilterBuilder
{
    // Func<which takes a FileSystemInfo and returns a bool whether it meets the criteria>
    private readonly List<Func<FileSystemInfoWrapper, bool>> _criteria = new();
    
    public void Clear()
    {
        _criteria.Clear();
    }
    
    public ScanFilterBuilder WithModifiedDateFilter(DateTime modifiedBefore)
    {
        _criteria.Add(entry => entry.Info.LastWriteTime < modifiedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithAccessedDateFilter(DateTime accessedBefore)
    {
        _criteria.Add(entry => entry.Info.LastAccessTime < accessedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithFileSizeFilter(long sizeThresholdInBytes)
    {
        // we are gonna add the FileSystemInfo if its a file and its size is greater than the threshold
        _criteria.Add(entry => 
            (entry.Info is FileInfo fileInfo && fileInfo.Length > sizeThresholdInBytes) 
            || entry.Info is DirectoryInfo); // if its a directory, we include it regardless of the size filter, since the file size filter should NOT exclude directories
        return this;
    }
    
    public ScanFilterBuilder WithDirectorySizeFilter(long sizeThresholdInBytes)
    {
        // we are gonna add the FileSystemInfo if its a directory and its size is greater than the threshold
        _criteria.Add(entry =>
        {
            // Only process directories; files always pass through this filter
            if (entry.Info is not DirectoryInfo dirInfo)
                return entry.Info is FileInfo;
            
            // Calculate and cache size only if not already cached from filter pipeline
            if (!entry.CalculatedSize.HasValue)
            {
                entry.CalculatedSize = DirectoryScanService.CalculateDirectorySize(dirInfo);
            }
            
            return entry.CalculatedSize.Value > sizeThresholdInBytes;
        });
        return this;
    }
    
    public ScanFilterBuilder WithIncludeFiles(bool includeFiles)
    {
        if (!includeFiles)
        {
            // we are gonna add the FileSystemInfo if its a directory, since files are not included
            _criteria.Add(entry => entry.Info is DirectoryInfo);
        }
        return this;
    }
    
    /// <summary>
    /// Returns a function that takes a FileSystemInfo and returns true if it meets ALL the criteria defined in this builder, and false otherwise.
    /// </summary>
    public Func<FileSystemInfoWrapper, bool> Build()
    {
        return entry => _criteria.All(criterion => criterion(entry));
    }
}