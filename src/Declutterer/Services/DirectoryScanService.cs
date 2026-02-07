using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Models;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Declutterer.Services;

public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;
    private readonly ILogger<DirectoryScanService> _logger;
    private readonly Lock _scanLock = new();
    
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
                var filter = _scanFilterService.CreateFilter(scanOptions);
                
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
            foreach (var dir in dirInfo.GetDirectories())
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
                        HasChildren = hasSubDirs,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
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
                        Parent = parentNode,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
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
    

    /// <summary>
    /// Parallelized approach for loading subdirectories from a single directory.
    /// Processes directory entries concurrently across CPU cores.
    /// </summary>
    private void LoadSubdirectoriesParallel(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        try
        {
            var directories = dirInfo.GetDirectories();
            var childNodes = new List<TreeNode>();
            
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
                        HasChildren = hasSubDirs,
                        IsSelected = parentNode.IsSelected // inherit selection state from parent
                    };
                    
                    if (filter != null && !filter(childNode))
                        return;

                    using (_scanLock.EnterScope())
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

    /// <summary>
    /// Asynchronously loads children for multiple root directories in parallel.
    /// This is optimized for the initial scan where multiple directories are processed concurrently.
    /// Returns a dictionary mapping each root node to its children.
    /// </summary>
    public async Task<Dictionary<TreeNode, List<TreeNode>>> LoadChildrenForMultipleRootsAsync(IEnumerable<TreeNode> rootNodes, ScanOptions? scanOptions)
    {
        var childrenByRoot = new Dictionary<TreeNode, List<TreeNode>>();
        
        return await Task.Run(() =>
        {
            var filter = _scanFilterService.CreateFilter(scanOptions);
            
            Parallel.ForEach(rootNodes, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, rootNode =>
            {
                try
                {
                    var dirInfo = new DirectoryInfo(rootNode.FullPath);
                    var children = new List<TreeNode>();
                    
                    LoadSubdirectoriesParallel(rootNode, dirInfo, filter, children);
                    LoadFiles(rootNode, dirInfo, filter, children);
                    
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = children;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we don't have access to
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = new List<TreeNode>(); // set empty list for roots we cant access
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading children for root: {NodePath}", rootNode.FullPath);
                    using (_scanLock.EnterScope())
                    {
                        childrenByRoot[rootNode] = new List<TreeNode>();
                    }
                }
            });
            
            return childrenByRoot;
        });
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