using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Threading.Tasks;
using Declutterer.Models;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Declutterer.Services;

public sealed class DirectoryEnumerator : FileSystemEnumerator<string>
{
    //private readonly IFileSystemEntry _directoryFilter;
    private readonly ScanFilterService _scanFilterService;
    private readonly Func<TreeNode, bool> _filter;
    public DirectoryEnumerator(string root, /*IFileSystemEntry directoryFilter,*/ FileAttributes attributesToSkip, ScanFilterService scanFilterService, Func<TreeNode, bool> filter)
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
        //_directoryFilter = directoryFilter;
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
            Size = 0, // Size will be calculated later if needed
            LastModified = entry.LastWriteTimeUtc.DateTime,
            Depth = 0 // Depth will be set later
        };
        return _filter(node);
        
        //return !_directoryFilter.ShouldSkip(ref entry); // Apply the directory filter
    }

    protected override string TransformEntry(ref FileSystemEntry entry)
        => entry.FileName.ToString();
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
                
                LoadSubdirectories(node, dirInfo, filter, children);
                
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
    
    private void LoadSubdirectories(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get subdirectories
        try
        {
            // using (var enumerator = new DirectoryEnumerator(dirInfo.FullName, FileAttributes.Hidden | FileAttributes.System,
            //            _scanFilterService, filter ?? (_ => true))) // if no filter provided, include all
            // {
            //     while (enumerator.MoveNext())
            //     {
            //         
            //     }
            // }
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
    private static long CalculateDirectorySize(DirectoryInfo dir) 
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
}