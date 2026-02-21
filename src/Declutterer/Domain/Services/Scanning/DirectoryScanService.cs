using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
using Microsoft.Extensions.Logging;
using Serilog;

// ReSharper disable SuggestVarOrType_Elsewhere
// ReSharper disable SuggestVarOrType_SimpleTypes

namespace Declutterer.Domain.Services.Scanning;

public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;
    private readonly ILogger<DirectoryScanService> _logger;
    
    // caching sizes to avoid redundant recursive calculations, <fullPath, size>, OrdinalIgnoreCase for path case-insensitivity on lookups
    private static readonly ConcurrentDictionary<string, long> _sizeCache = new(StringComparer.OrdinalIgnoreCase);
    
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
            LastAccessed = dirInfo.LastAccessTime,
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
        //Small, bounded work (single directory) therefore no need to offload to background thread
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
    
    private void LoadSubdirectories(TreeNode parentNode, DirectoryInfo dirInfo, Func<FileSystemInfoWrapper, bool>? filter, List<TreeNode> children)
    {
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
            
            // Enumerate subdirectories once and cache the result
            var subdirectories = dirInfo.GetDirectories("*", enumerationOptions);
            
            // getting subdirectories in the current root directory we are in
            foreach (var dir in subdirectories) 
            {
                try
                {
                    var wrapper = new FileSystemInfoWrapper { Info = dir };
                    
                    // Apply filter(filtering out nodes that dont match the criteria)
                    if (filter != null && !filter(wrapper))
                        continue;
                    
                    // using cached size if available from the filter, otherwise calculate it
                    long size = wrapper.CalculatedSize ?? CalculateDirectorySize(dir);
                    
                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = size,
                        LastModified = dir.LastWriteTime,
                        LastAccessed = dir.LastAccessTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = HasAnyChildren(dir, enumerationOptions, filter), // check if the directory has any children to determine if it can be expanded
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected // if parent is selected then disable the checkbox for the child since we dont want to allow unselecting a child when parent is selected, this simplifies the logic and avoids edge cases with selection state
                    };
                    
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
    
    private void LoadFiles(TreeNode parentNode, DirectoryInfo dirInfo, Func<FileSystemInfoWrapper, bool>? filter, List<TreeNode> children)
    {
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
            foreach (FileInfo file in dirInfo.GetFiles("*", enumerationOptions))
            {
                try
                {
                    var wrapper = new FileSystemInfoWrapper { Info = file, CalculatedSize = file.Length };
                    
                    if (filter != null && !filter(wrapper))
                        continue;
                    
                    var childNode = new TreeNode
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        LastAccessed = file.LastAccessTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected
                    };
                    
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
    private void LoadSubdirectoriesParallel(TreeNode parentNode, DirectoryInfo dirInfo, Func<FileSystemInfoWrapper, bool>? filter, List<TreeNode> children)
    {
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
            
            // Enumerate directories once before parallel processing
            DirectoryInfo[] directories = dirInfo.GetDirectories("*", enumerationOptions);
            var childNodes = new ConcurrentBag<TreeNode>();
            
            Parallel.ForEach(directories, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, dir =>
            {
                try
                {
                    var wrapper = new FileSystemInfoWrapper { Info = dir };
                    
                    if (filter != null && !filter(wrapper))
                        return;
                   
                    // using cached size if available from the filter, otherwise calculate it
                    long size = wrapper.CalculatedSize ?? CalculateDirectorySize(dir);
                    
                    var childNode = new TreeNode
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = size,
                        LastModified = dir.LastWriteTime,
                        LastAccessed = dir.LastAccessTime,
                        Depth = parentNode.Depth + 1,
                        Parent = parentNode,
                        HasChildren = HasAnyChildren(dir, enumerationOptions, filter), // check if the directory has any children to determine if it can be expanded
                        IsCheckboxSelected = parentNode.IsCheckboxSelected, // inherit selection state from parent
                        IsCheckboxEnabled = !parentNode.IsCheckboxSelected
                    };
                    
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
        
        //Large, unbounded work (multiple directories), must run on the background thread to avoid blocking the UI
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
    /// <returns>The total size of the directory in bytes.</returns>
    public static long CalculateDirectorySize(DirectoryInfo dir) 
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
    
    private static bool HasAnyChildren(DirectoryInfo dirInfo, EnumerationOptions enumerationOptions, 
        Func<FileSystemInfoWrapper, bool>? filter)
    {
        try
        {
            return dirInfo.EnumerateFileSystemInfos("*", enumerationOptions)
                .Any(fsi => filter == null || filter(new FileSystemInfoWrapper { Info = fsi}));
        }
        catch
        {
            return false;
        }
    }
}