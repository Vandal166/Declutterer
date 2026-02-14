using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Declutterer.Services;

public sealed class ScanFilterBuilder
{
    private readonly List<Func<FileSystemInfo, bool>> _dateCriteria = new();
    private Func<FileSystemInfo, bool>? _fileSizeFilter;
    private Func<FileSystemInfo, bool>? _directorySizeFilter;
    private bool _includeFiles = true; // Track whether files should be included
    
    public void Clear()
    {
        _dateCriteria.Clear();
        _fileSizeFilter = null;
        _directorySizeFilter = null;
        _includeFiles = true;
    }
    
    public ScanFilterBuilder WithModifiedDateFilter(DateTime modifiedBefore)
    {
        _dateCriteria.Add(entry => entry.LastWriteTime < modifiedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithAccessedDateFilter(DateTime accessedBefore)
    {
        _dateCriteria.Add(entry => entry.LastAccessTime < accessedBefore);
        return this;
    }
    
    public ScanFilterBuilder WithFileSizeFilter(long sizeThresholdInBytes)
    {
        _fileSizeFilter = entry => entry is FileInfo fileInfo && fileInfo.Length > sizeThresholdInBytes;
        return this;
    }
    
    public ScanFilterBuilder WithDirectorySizeFilter(long sizeThresholdInBytes)
    {
        _directorySizeFilter = entry => entry is DirectoryInfo dirInfo && DirectoryScanService.CalculateDirectorySize(dirInfo) > sizeThresholdInBytes;
        return this;
    }
    
    public ScanFilterBuilder WithIncludeFiles(bool includeFiles)
    {
        _includeFiles = includeFiles;
        return this;
    }
    
    //TODO i dont like this at all, need to refactor ts. no one knows what the f is goin on here
    
    public Func<FileSystemInfo, bool> Build()
    {
        // If no criteria exist, accept everything (unless files are explicitly excluded)
        if (_dateCriteria.Count == 0 && _fileSizeFilter == null && _directorySizeFilter == null)
        {
            if (!_includeFiles)
            {
                // Only accept directories
                return entry => entry is DirectoryInfo;
            }
            return _ => true;
        }
        
        // Combine date criteria using AND logic
        bool CombineDate(FileSystemInfo entry) => _dateCriteria.All(criterion => criterion(entry));

        // Combine size filters using OR logic (mutually exclusive - a file can't be a directory)
        Func<FileSystemInfo, bool>? sizeFilter = null;
        if (_fileSizeFilter != null && _directorySizeFilter != null)
        {
            sizeFilter = entry => _fileSizeFilter(entry) || _directorySizeFilter(entry);
        }
        else if (_fileSizeFilter != null)
        {
            sizeFilter = _fileSizeFilter;
        }
        else if (_directorySizeFilter != null)
        {
            sizeFilter = _directorySizeFilter;
        }
        
        // Apply file inclusion filter
        Func<FileSystemInfo, bool> fileInclusionFilter = !_includeFiles
            ? entry => entry is DirectoryInfo
            : _ => true;
        
        // Combine date filter, size filter, and file inclusion filter using AND logic
        if (sizeFilter != null)
        {
            return entry => fileInclusionFilter(entry) && CombineDate(entry) && sizeFilter(entry);
        }
        else
        {
            return entry => fileInclusionFilter(entry) && CombineDate(entry);
        }
    }
}