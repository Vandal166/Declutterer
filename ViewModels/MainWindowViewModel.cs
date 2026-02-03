using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Models;
using Declutterer.Views;

namespace Declutterer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    private string _greeting = "Add directories to scan";
    
    private TopLevel? _topLevel;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    // TreeDataGrid will automatically handle hierarchical display using the Children collection
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    [ObservableProperty]
    private bool _includeFiles = false; // if we should include files in the scan
    
    public MainWindowViewModel()
    {
        // Set up the lazy loading callback for when nodes are expanded
        TreeNode.OnExpandRequested = async (node) =>
        {
            if (node.Children.Count > 0 && node.Children[0].Name == "Loading...")
            {
                node.Children.Clear();
            }
            await LoadChildrenAsync(node);
        };
    }
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;

    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
        if (_topLevel == null) 
            return;
        
        var scanOptionsWindow = new ScanOptionsWindow
        {
            DataContext = new ScanOptionsWindowViewModel()
        };

        if(_topLevel is Window window)
            await scanOptionsWindow.ShowDialog(window);
    }
    
    [RelayCommand]
    private async Task AddDirectoryAsync()
    {
        var storageProvider = _topLevel?.StorageProvider;
        if (storageProvider == null) 
            return;
        
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select directories to scan"
        });

        foreach (var folder in folders)
        {
            var rootNode = new TreeNode
            {
                Name = folder.Name,
                FullPath = folder.Path.LocalPath,
                IsDirectory = true,
                Size = DirSize(new DirectoryInfo(folder.Path.LocalPath)),
                LastModified = Directory.GetLastWriteTime(folder.Path.LocalPath),
                Depth = 0,
                Parent = null
            };

            // Add a dummy child to indicate it can be expanded
            rootNode.Children.Add(new TreeNode { Name = "Loading...", Depth = 1, Parent = rootNode });

            Roots.Add(rootNode);
        }
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

    [RelayCommand]
    private Task ToggleExpand(TreeNode node)
    {
        // Simply toggle the expanded state
        // The OnIsExpandedChanged partial method in TreeNode will handle lazy loading
        node.IsExpanded = !node.IsExpanded;
        return Task.CompletedTask;
    }

    private async Task LoadChildrenAsync(TreeNode node)
    {
        node.IsLoading = true;

        try
        {
            await Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(node.FullPath);
                
                // Get subdirectories
                try
                {
                    foreach (var dir in dirInfo.GetDirectories())
                    {
                        try
                        {
                            var childNode = new TreeNode
                            {
                                Name = dir.Name,
                                FullPath = dir.FullName,
                                IsDirectory = true,
                                Size = DirSize(dir),
                                LastModified = dir.LastWriteTime,
                                Depth = node.Depth + 1,
                                Parent = node
                            };
                            
                            // Check if directory has subdirectories or files
                            try
                            {
                                if (dir.GetDirectories().Length > 0 || (IncludeFiles && dir.GetFiles().Length > 0))
                                {
                                    // Add a dummy child to indicate it can be expanded
                                    childNode.Children.Add(new TreeNode { Name = "Loading...", Depth = childNode.Depth + 1, Parent = childNode });
                                }
                            }
                            catch { /* Ignore access errors for nested directories */ }

                            Avalonia.Threading.Dispatcher.UIThread.Post(() => node.Children.Add(childNode));
                        }
                        catch { /* Ignore directories we can't access */ }
                    }
                }
                catch { /* Ignore access errors */ }

                // Get files if IncludeFiles is enabled
                if (IncludeFiles)
                {
                    try
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

                                Avalonia.Threading.Dispatcher.UIThread.Post(() => node.Children.Add(childNode));
                            }
                            catch { /* Ignore files we can't access */ }
                        }
                    }
                    catch { /* Ignore access errors */ }
                }
            });
        }
        catch (Exception ex)
        {
            // Handle access denied or other errors
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                node.Children.Add(new TreeNode 
                { 
                    Name = $"Error: {ex.Message}", 
                    Depth = node.Depth + 1, 
                    Parent = node,
                    IsDirectory = false
                });
            });
        }
        finally
        {
            node.IsLoading = false;
        }
    }
}