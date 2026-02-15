using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Models;

namespace Declutterer.ViewModels;

public sealed partial class ScanOptionsWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ScanOptions _scanOptions = new();
    
    [ObservableProperty]
    private string? _directorySelectionError;
    
    [ObservableProperty]
    private bool _isDesktopSelected;
    
    [ObservableProperty]
    private bool _isDownloadsSelected;
    
    [ObservableProperty]
    private bool _isDocumentsSelected;
    
    private TopLevel? _topLevel;
    public event Action<ScanOptions>? RequestClose;
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;
    
    [RelayCommand]
    private async Task OnAddDirectoryAsync()
    {
        var storageProvider = _topLevel?.StorageProvider;
        if (storageProvider is null)
            return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select directories to scan"
        });

        bool hasErrors = false;
        bool addedAnyDirectory = false;

        foreach (var folderPath in folders.Select(f => f.Path))
        {
            if (folderPath.IsAbsoluteUri)
            {
                var normalizedPath = Path.TrimEndingDirectorySeparator(folderPath.LocalPath);
                
                // Check if this path is nested within any existing directory
                bool isNested = ScanOptions.DirectoriesToScan.Any(existingPath =>
                    normalizedPath.StartsWith(existingPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                
                if (isNested)
                {
                    DirectorySelectionError = $"The directory '{Path.GetFileName(normalizedPath)}' is already included in a parent directory being scanned.";
                    hasErrors = true;
                    continue;
                }
                
                // Check if any existing directories are nested within this new path
                var nestedPaths = ScanOptions.DirectoriesToScan
                    .Where(existingPath => existingPath.StartsWith(normalizedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                // Remove nested directories since they're redundant now
                foreach (var nestedPath in nestedPaths)
                {
                    ScanOptions.DirectoriesToScan.Remove(nestedPath);
                }
                
                ScanOptions.DirectoriesToScan.Add(normalizedPath);
                addedAnyDirectory = true;
            }
            else
            {
                DirectorySelectionError = "Selected path is not a valid directory.";
                hasErrors = true;
            }
        }
        
        // Clear error message if directories were added successfully without errors
        if (addedAnyDirectory && !hasErrors)
        {
            DirectorySelectionError = null;
        }
    }
    
    [RelayCommand]
    private void OnScan() // trigger Scan
    {
        // Validate that at least one directory is selected
        if (ScanOptions.DirectoriesToScan.Count == 0)
        {
            DirectorySelectionError = "Please select at least one directory to scan";
            return;
        }
        
        // Calculate DateTime values before returning
        if (ScanOptions.AgeFilter is { UseModifiedDate: true, MonthsModifiedValue: > 0 })
        {
            ScanOptions.AgeFilter.ModifiedBefore = DateTime.Now.AddMonths(-ScanOptions.AgeFilter.MonthsModifiedValue);
        }

        if (ScanOptions.AgeFilter is { UseAccessedDate: true, MonthsAccessedValue: > 0 })
        {
            ScanOptions.AgeFilter.AccessedBefore = DateTime.Now.AddMonths(-ScanOptions.AgeFilter.MonthsAccessedValue);
        }
        RequestClose?.Invoke(ScanOptions); // Notify subscribers with the selected scan options
    }

    [RelayCommand]
    private void OnCancel() // Cancel and close the window
    {
        RequestClose?.Invoke(null); // Notify subscribers with null to indicate cancellation
    }

    [RelayCommand]
    private void OnRemoveDirectory(string directoryPath)
    {
        ScanOptions.DirectoriesToScan.Remove(directoryPath);
    }

    partial void OnIsDesktopSelectedChanged(bool value)
    {
        var desktopPath = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (value)
        {
            // Remove any nested directories that are under Desktop
            var nestedPaths = ScanOptions.DirectoriesToScan
                .Where(existingPath => existingPath.StartsWith(desktopPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var nestedPath in nestedPaths)
            {
                ScanOptions.DirectoriesToScan.Remove(nestedPath);
            }
            
            ScanOptions.DirectoriesToScan.Add(desktopPath);
        }
        else
        {
            ScanOptions.DirectoriesToScan.Remove(desktopPath);
        }
    }

    partial void OnIsDownloadsSelectedChanged(bool value)
    {
        var downloadsPath = Path.TrimEndingDirectorySeparator(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        if (value)
        {
            // Remove any nested directories that are under Downloads
            var nestedPaths = ScanOptions.DirectoriesToScan
                .Where(existingPath => existingPath.StartsWith(downloadsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var nestedPath in nestedPaths)
            {
                ScanOptions.DirectoriesToScan.Remove(nestedPath);
            }
            
            ScanOptions.DirectoriesToScan.Add(downloadsPath);
        }
        else
        {
            ScanOptions.DirectoriesToScan.Remove(downloadsPath);
        }
    }

    partial void OnIsDocumentsSelectedChanged(bool value)
    {
        var documentsPath = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (value)
        {
            // Remove any nested directories that are under Documents
            var nestedPaths = ScanOptions.DirectoriesToScan
                .Where(existingPath => existingPath.StartsWith(documentsPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var nestedPath in nestedPaths)
            {
                ScanOptions.DirectoriesToScan.Remove(nestedPath);
            }
            
            ScanOptions.DirectoriesToScan.Add(documentsPath);
        }
        else
        {
            ScanOptions.DirectoriesToScan.Remove(documentsPath);
        }
    }
}