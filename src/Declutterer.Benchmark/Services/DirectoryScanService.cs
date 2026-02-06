using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading.Tasks;
using Declutterer.Benchmark.Models;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Declutterer.Benchmark.Services;

public sealed class DirectoryEnumerator : FileSystemEnumerator<string>
{
    private readonly ScanFilterService _scanFilterService;
    private readonly Func<TreeNode, bool> _filter;
    
    public DirectoryEnumerator(string root, FileAttributes attributesToSkip, ScanFilterService scanFilterService, Func<TreeNode, bool> filter)
        : base
        (
            root,
            new EnumerationOptions
            {
                IgnoreInaccessible = true, // Skip directories we can't access
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = attributesToSkip,
                BufferSize = 65536, // 64KB buffer size
            }
        )
    {
        _scanFilterService = scanFilterService;
        _filter = filter;
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
    {
        if (!entry.IsDirectory)
            return false; // Only process directories
        
        var node = new TreeNode
        {
            Name = entry.FileName.ToString(),
            FullPath = entry.ToFullPath(),
            IsDirectory = true,
            Size = entry.Length,
            LastModified = entry.LastWriteTimeUtc.DateTime,
            Depth = 0 // Depth will be set later
        };
        return _filter(node); // Apply the provided filter function to determine if this entry should be included
    }

    protected override string TransformEntry(ref FileSystemEntry entry)
        => entry.ToFullPath();
}

public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;
    private readonly ILogger<DirectoryScanService> _logger;
    
    public DirectoryScanService(ScanFilterService scanFilterService, ILogger<DirectoryScanService> logger)
    {
        _scanFilterService = scanFilterService;
        _logger = logger;
    }

    /// <summary>
    /// Creates the root TreeNode for the specified directory path.
    /// </summary>
    /// <param name="directoryPath">The path of the directory to create the root node for.</param>
    /// <returns>The root TreeNode representing the directory.</returns>
    public static TreeNode CreateRootNode(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        var rootNode = new TreeNode
        {
            Name = dirInfo.Name,
            FullPath = dirInfo.FullName,
            IsDirectory = true,
            Size = CalculateDirectorySize(dirInfo),
            LastModified = dirInfo.LastWriteTime,
            Depth = 0, // we start at depth 0 for root nodes
            Parent = null, // w/o parent since this is a root node
            HasChildren = true
        };
        
        Log.Information("Created root node for directory: {DirectoryPath}", directoryPath);
        return rootNode;
    }
    
    public async Task<List<TreeNode>> LoadChildrenAsync(TreeNode node, ScanOptions? scanOptions)
    {
        var children = new List<TreeNode>();
        
        _logger.LogInformation("Loading children for node: {NodePath}", node.FullPath);
        
        try
        {
             // Run the directory scanning on a background thread to avoid blocking the UI
            await Task.Run(() =>
            {
                var filter = scanOptions != null 
                    ? _scanFilterService.CreateFilter(scanOptions)
                    : null;
                
                var dirInfo = new DirectoryInfo(node.FullPath);
                
                LoadSubdirectoriesWithEnumerator(node, dirInfo, filter, children);
                
                LoadFiles(node, dirInfo, filter, children);
            });
            
            _logger.LogInformation("Loaded {ChildrenCount} children for node: {NodePath}", children.Count, node.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading children for node: {NodePath}", node.FullPath);
        }
        
        return children;
    }
    
    public void LoadSubdirectoriesWithEnumerator(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get subdirectories
        try
        {
            using (var enumerator = new DirectoryEnumerator(dirInfo.FullName, FileAttributes.Hidden | FileAttributes.System,
                       _scanFilterService, filter ?? (_ => true))) // if no filter provided, include all
            {
                while (enumerator.MoveNext()) // for each entry, the ShouldIncludeEntry method in DirectoryEnumerator will be called to determine if it should be included based on the filter criteria
                {
                    // if yes, then we create a TreeNode for it and add it to the children list
                    var entryPath = enumerator.Current;
                    var entryInfo = new DirectoryInfo(entryPath);
                    try
                    {
                        var childNode = new TreeNode
                        {
                            Name = entryInfo.Name,
                            FullPath = entryInfo.FullName,
                            IsDirectory = true,
                            Size = CalculateDirectorySize(entryInfo),
                            LastModified = entryInfo.LastWriteTime,
                            Depth = parentNode.Depth + 1,
                            Parent = parentNode,
                            HasChildren = true // Assume children exist; load them lazily when expanded
                        };
                        // no filter since we already applied it in the DirectoryEnumerator
                        children.Add(childNode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", entryInfo.FullName);
                    }
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }
    
    public void LoadSubdirectories(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get subdirectories
        try
        {
            foreach (var dir in dirInfo.GetDirectories()) //TODO GetDirectories vs EnumerateDirectories? Benchmark?
            {
                try
                {
                    // Check if directory has any subdirectories or files but only if IncludeFiles is enabled
                    bool hasSubDirs = dir.GetDirectories().Length > 0 || (dir.GetFiles().Length > 0 /*&& (_currentScanOptions?.IncludeFiles == true)*/); //TODO this will be added later, for now we dont include files

                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = CalculateDirectorySize(dir),
                        LastModified = dir.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = hasSubDirs
                    };
                            
                    // Apply filter(filtering out nodes that dont match the criteria)
                    if (filter != null && !filter(childNode))
                        continue;

                    children.Add(childNode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", dir.FullName);
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }
    
    private void LoadFiles(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get files if IncludeFiles is enabled
        //if (_currentScanOptions?.IncludeFiles == true) //TODO this will be added later, for now we dont include files
        try
        {
            foreach (var file in dirInfo.GetFiles()) // getting files in the current directory we are in(only the current one, not recursive so we dont get too many files)
            {
                try
                {
                    var childNode = new TreeNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode
                    };

                    if (filter != null && !filter(childNode))
                        continue;

                    children.Add(childNode);
                }
                catch(Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access file: {FileName}", file.FullName);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error reading files from: {DirectoryPath}", dirInfo.FullName);
        }
    }

    
    // Recursively calculates the size of a directory and returns it in bytes
    public static long CalculateDirectorySize(DirectoryInfo dir) 
    {    
        long size = 0;    
        // Add file sizes.
        FileInfo[] fis = dir.GetFiles();
        foreach (FileInfo fi in fis) 
        {      
            size += fi.Length;    
        }
        // Add subdirectory sizes.
        DirectoryInfo[] dis = dir.GetDirectories();
        foreach (DirectoryInfo di in dis) 
        {
            size += CalculateDirectorySize(di);   
        }
        return size;  
    }
    
    // Parallelized version with concurrent directory processing
    public void LoadSubdirectoriesWithEnumeratorParallel(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        try
        {
            var entries = new List<string>();
            
            // First pass: collect all paths
            using (var enumerator = new DirectoryEnumerator(dirInfo.FullName, FileAttributes.Hidden | FileAttributes.System,
                       _scanFilterService, filter ?? (_ => true)))
            {
                while (enumerator.MoveNext())
                {
                    entries.Add(enumerator.Current);
                }
            }
            
            // Second pass: process in parallel
            var childNodes = new List<TreeNode>();
            var lockObj = new object();
            
            Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entryPath =>
            {
                try
                {
                    var entryInfo = new DirectoryInfo(entryPath);
                    var childNode = new TreeNode
                    {
                        Name = entryInfo.Name,
                        FullPath = entryInfo.FullName,
                        IsDirectory = true,
                        Size = CalculateDirectorySize(entryInfo),
                        LastModified = entryInfo.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = true
                    };
                    
                    lock (lockObj)
                    {
                        childNodes.Add(childNode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", entryPath);
                }
            });
            
            children.AddRange(childNodes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }
    
    public void LoadSubdirectoriesParallel(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        try
        {
            var directories = dirInfo.GetDirectories();
            var childNodes = new List<TreeNode>();
            var lockObj = new object();
            
            Parallel.ForEach(directories, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dir =>
            {
                try
                {
                    bool hasSubDirs = dir.GetDirectories().Length > 0 || (dir.GetFiles().Length > 0);

                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = CalculateDirectorySize(dir),
                        LastModified = dir.LastWriteTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = hasSubDirs
                    };
                    
                    if (filter != null && !filter(childNode))
                        return;

                    lock (lockObj)
                    {
                        childNodes.Add(childNode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not access subdirectory: {DirectoryName}", dir.FullName);
                }
            });
            
            children.AddRange(childNodes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading subdirectories from: {DirectoryPath}", dirInfo.FullName);
        }
        
        _logger.LogInformation("Loaded {ChildrenCount} subdirectories for node: {NodePath}", children.Count, parentNode.FullPath);
    }
    
    // Parallelized directory size calculation
    public static long CalculateDirectorySizeParallel(DirectoryInfo dir)
    {
        long size = 0;
        
        try
        {
            // Add file sizes
            FileInfo[] fis = dir.GetFiles();
            size += fis.AsParallel().Sum(fi => fi.Length);
            
            // Add subdirectory sizes in parallel
            DirectoryInfo[] dis = dir.GetDirectories();
            size += dis.AsParallel().Sum(di => CalculateDirectorySizeParallel(di));
        }
        catch
        {
            // Silently fail if directory is inaccessible
        }
        
        return size;
    }
}
