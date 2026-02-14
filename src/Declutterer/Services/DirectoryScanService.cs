using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Models;
using Microsoft.Extensions.Logging;
using Serilog;
// ReSharper disable SuggestVarOrType_Elsewhere
// ReSharper disable SuggestVarOrType_SimpleTypes

namespace Declutterer.Services;

public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;
    private readonly ILogger<DirectoryScanService> _logger;
    
    // caching sizes to avoid redundant recursive calculations, <fullPath, size>
    private static readonly ConcurrentDictionary<string, long> _sizeCache = new();
    
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
    
    /// <summary>
    /// Loads the child nodes (subdirectories and files) for a given TreeNode representing a directory.
    /// </summary>
    /// <param name="node">The TreeNode for which to load children. Must represent a directory.</param>
    /// <param name="scanOptions">The options to apply when scanning for children, such as filters. Can be null for no filtering.</param>
    /// <returns>A collection of TreeNodes representing the children of the specified node.</returns>
    public Task<List<TreeNode>> LoadChildrenAsync(TreeNode node, ScanOptions? scanOptions)
    {
        var children = new List<TreeNode>();
        
        _logger.LogInformation("Loading children for node: {NodePath}", node.FullPath);
        
        try
        {
            var filter = _scanFilterService.CreateFilter(scanOptions);
            
            var dirInfo = new DirectoryInfo(node.FullPath);
            
            LoadSubdirectories(node, dirInfo, filter, children);
            
            LoadFiles(node, dirInfo, filter, children);
            
            _logger.LogInformation("Loaded {ChildrenCount} children for node: {NodePath}", children.Count, node.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading children for node: {NodePath}", node.FullPath);
        }
        
        return Task.FromResult(children);
    }
    
    private void LoadSubdirectories(TreeNode parentNode, DirectoryInfo dirInfo, Func<TreeNode, bool>? filter, List<TreeNode> children)
    {
        // Get subdirectories
        try
        {
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                BufferSize = 64 * 1024, // 64KB buffer for enumeration
                AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.Temporary | FileAttributes.Offline | FileAttributes.Encrypted,
                ReturnSpecialDirectories = false
            };
            
            // getting subdirectories in the current root directory we are in
            foreach (var dir in dirInfo.GetDirectories("*", enumerationOptions)) 
            {
                try
                {
                    // Check if directory has any subdirectories or files but only if IncludeFiles is enabled
                    bool hasSubDirs = dir.GetDirectories().Length > 0 || (dir.GetFiles().Length > 0 /*&& (_currentScanOptions?.IncludeFiles == true)*/); //TODO this will be added later, for now we dont include files

                    //TODO:
                    // we coudl just change the filter so that it accepts only the necessary info instead of creating TreeNode first and then filtering,
                    // this way we can filter out more efficiently without creating TreeNode and calculating directory shit
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
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected // if parent is selected then disable the checkbox for the child since we dont want to allow unselecting a child when parent is selected, this simplifies the logic and avoids edge cases with selection state
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
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected
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
            var childNodes = new ConcurrentBag<TreeNode>();
            
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
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected
                    };
                    
                    if (filter != null && !filter(childNode))
                        return;

                    childNodes.Add(childNode);
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
        ClearSizeCache();
        
        var childrenByRoot = new ConcurrentDictionary<TreeNode, List<TreeNode>>();
        
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
                    
                    childrenByRoot.TryAdd(rootNode, children);
                }
                catch (UnauthorizedAccessException)
                {
                    childrenByRoot.TryAdd(rootNode, new List<TreeNode>());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading children for root: {NodePath}", rootNode.FullPath);
                    childrenByRoot.TryAdd(rootNode, new List<TreeNode>());
                }
            });
            
            return childrenByRoot.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        });
    }
    
    
    /// <summary>
    /// Recursively calculates the size of a directory with caching to avoid redundant calculations.
    /// </summary>
    private static long CalculateDirectorySize(DirectoryInfo dir) 
    {
        if (_sizeCache.TryGetValue(dir.FullName, out var cachedSize))
            return cachedSize;
        
        long size = 0;
        
        try
        {
            // Add file sizes
            FileInfo[] fis = dir.GetFiles();
            foreach (FileInfo fi in fis) 
            {      
                size += fi.Length;    
            }
            
            // Add subdirectory sizes
            DirectoryInfo[] dis = dir.GetDirectories();
            foreach (DirectoryInfo di in dis) 
            {
                size += CalculateDirectorySize(di);   
            }
        }
        catch (UnauthorizedAccessException) {/* ret 0; when access denied*/}
        
        _sizeCache.TryAdd(dir.FullName, size);
        return size;
    }
    
    private static void ClearSizeCache() => _sizeCache.Clear();
}