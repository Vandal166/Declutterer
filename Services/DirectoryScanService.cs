using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Services;

public sealed class DirectoryScanService
{
    private readonly ScanFilterService _scanFilterService;

    public DirectoryScanService(ScanFilterService scanFilterService)
    {
        _scanFilterService = scanFilterService;
    }

    public static TreeNode CreateRootNode(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        var rootNode = new TreeNode
        {
            Name = dirInfo.Name,
            FullPath = dirInfo.FullName,
            IsDirectory = true,
            Size = DirSize(dirInfo),
            LastModified = dirInfo.LastWriteTime,
            Depth = 0,
            Parent = null,
            HasChildren = true
        };
        return rootNode;
    }
    
    public async Task<List<TreeNode>> LoadChildrenAsync(TreeNode node, ScanOptions? scanOptions)
    {
        var children = new List<TreeNode>();
        node.IsLoading = true;
        try
        {
            await Task.Run(() =>
            {
                var filter = scanOptions != null 
                    ? _scanFilterService.CreateFilter(scanOptions)
                    : null;
                
                var dirInfo = new DirectoryInfo(node.FullPath);
                
                // Get subdirectories
                try
                {
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        try
                        {
                            // Check if directory has any subdirectories
                            bool hasSubDirs = dir.GetDirectories().Length > 0;
                            
                            var childNode = new TreeNode
                            {
                                Name = dir.Name,
                                FullPath = dir.FullName,
                                IsDirectory = true,
                                Size = DirSize(dir),
                                LastModified = dir.LastWriteTime,
                                Depth = node.Depth + 1,
                                Parent = node,
                                HasChildren = hasSubDirs
                            };
                            // Apply filter
                            if (filter != null && !filter(childNode))
                                continue;
                            
                            children.Add(childNode);
                            
                            //Avalonia.Threading.Dispatcher.UIThread.Post(() => node.Children.Add(childNode));
                        }
                        catch { /* Ignore directories we can't access */ }
                    }
                }
                catch { /* Ignore access errors */ }

                // Get files if IncludeFiles is enabled
                //if (_currentScanOptions?.IncludeFiles == true) //TODO this will be added later, for now we dont include files
                if (true)
                {
                    foreach (var file in dirInfo.GetFiles())
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
                                Depth = node.Depth + 1,
                                Parent = node
                            };

                            // Apply filter
                            if (filter != null && !filter(childNode))
                                continue;

                            children.Add(childNode);
                            //Avalonia.Threading.Dispatcher.UIThread.Post(() => node.Children.Add(childNode));
                        }
                        catch
                        {
                            /* Ignore files we can't access */
                        }
                    }
                }
            });
        }
        finally
        {
            node.IsLoading = false;
        }
        return children;
    }
    
    private static long DirSize(DirectoryInfo d) 
    {    
        long size = 0;    
        // Add file sizes.
        FileInfo[] fis = d.GetFiles();
        foreach (FileInfo fi in fis) 
        {      
            size += fi.Length;    
        }
        // Add subdirectory sizes.
        DirectoryInfo[] dis = d.GetDirectories();
        foreach (DirectoryInfo di in dis) 
        {
            size += DirSize(di);   
        }
        return size;  
    }
}