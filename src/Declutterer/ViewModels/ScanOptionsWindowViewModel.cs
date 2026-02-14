using System;
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
    private ScanOptions scanOptions = new();
    
    [ObservableProperty]
    private string? directorySelectionError;
    
    private TopLevel? _topLevel;
    public event Action<ScanOptions>? RequestClose;
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;
    
    [RelayCommand]
    private async Task OnAddDirectoryAsync()
    {
        var storageProvider = _topLevel?.StorageProvider;
        if (storageProvider == null)
            return;

        //TODO guard against user picking an Disk or Network Drive
        // Unhandled exception. System.InvalidOperationException: This operation is not supported for a relative URI.
        //     at System.Uri.get_LocalPath()

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select directories to scan"
        });

        foreach (var folder in folders)
        {
            ScanOptions.DirectoriesToScan.Add(folder.Path.LocalPath);
        }
        
        // Clear error message if directories were added
        if (ScanOptions.DirectoriesToScan.Count > 0)
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
        if (ScanOptions.AgeFilter.UseModifiedDate && ScanOptions.AgeFilter.MonthsModifiedValue > 0)
        {
            ScanOptions.AgeFilter.ModifiedBefore = DateTime.Now.AddMonths(-ScanOptions.AgeFilter.MonthsModifiedValue);
        }

        if (ScanOptions.AgeFilter.UseAccessedDate && ScanOptions.AgeFilter.MonthsAccessedValue > 0)
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
}