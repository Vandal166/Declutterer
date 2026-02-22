using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Utilities.Helpers;
using Serilog;

namespace Declutterer.UI.ViewModels;

public sealed partial class CleanupWindowViewModel : ViewModelBase, IContextMenuProvider
{
    private const long LargeFileSizeThresholdBytes = 100 * 1024 * 1024; // 100 MB
    private static readonly TimeSpan OldFileThreshold = TimeSpan.FromDays(365 * 2); // 2 years
    
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IDeleteService _deleteService;
    
    private CancellationTokenSource? _deletionCancellationTokenSource;
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _itemsToDelete = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _largeDirectories = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _largeFiles = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _oldFiles = new();
    
    [ObservableProperty]
    private ObservableCollection<TreeNode> _otherItems = new();
    
    [ObservableProperty]
    private string _largeDirectoriesSize = "0 B";
    
    [ObservableProperty]
    private string _largeFilesSize = "0 B";
    
    [ObservableProperty]
    private string _oldFilesSize = "0 B";
    
    [ObservableProperty]
    private string _otherItemsSize = "0 B";
    
    [ObservableProperty]
    private bool _sendToRecycleBin = true; // Default to safer option
    
    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";
    
    [ObservableProperty]
    private bool _isDeletionInProgress = false;
    
    [ObservableProperty]
    private bool _isCancellationRequested = false;
    
    [ObservableProperty]
    private bool _canCancelDeletion = false;
    
    [ObservableProperty]
    private double _deletionProgress = 0; // 0 to 100
    
    [ObservableProperty]
    private string _deletionStatus = string.Empty;
    
    [ObservableProperty]
    private TreeNode? _selectedItem = null;
    
    private TopLevel? _topLevel; // Reference to the TopLevel window for folder picker
    
    [ObservableProperty]
    private bool _canDelete;
    
    public event Action<DeleteResult?>? RequestClose;
    
    public CleanupWindowViewModel(ObservableCollection<TreeNode> itemsToDelete, IExplorerLauncher explorerLauncher, IErrorDialogService errorDialogService, IConfirmationDialogService confirmationDialogService, IDeleteService deleteService)
    {
        _explorerLauncher = explorerLauncher;
        _errorDialogService = errorDialogService;
        _confirmationDialogService = confirmationDialogService;
        _deleteService = deleteService;

        ItemsToDelete.CollectionChanged += ItemsToDeleteOnCollectionChanged;
        
        ItemsToDelete.Clear();
        foreach (var item in itemsToDelete)
        {
            ItemsToDelete.Add(item);
        }
        
        CalculateTotalSize();
        BuildGroupedItems();
    }

    public CleanupWindowViewModel() { } // for designer

    [RelayCommand]
    private async Task Delete()
    {
        if (SendToRecycleBin)
        {
            await MoveToRecycleBinAsync();
        }
        else
        {
            await DeletePermanentlyAsync();
        }
    }
    
    [RelayCommand]
    private void CancelDeletion()
    {
        if (_deletionCancellationTokenSource is not null && !_deletionCancellationTokenSource.IsCancellationRequested)
        {
            IsCancellationRequested = true;
            _deletionCancellationTokenSource.Cancel();
        }
    }
    
    [RelayCommand]
    private void OnCancel()    
    {
        ItemsToDelete.Clear();
        RequestClose?.Invoke(null);
    }

    [RelayCommand]
    private async Task MoveToRecycleBinAsync()
    {
        try
        {
            // Show confirmation dialog
            var confirmationMessage = BuildConfirmationMessage("move to Recycle Bin");
            var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
                "Confirm Deletion",
                confirmationMessage);

            if (!confirmed)
                return;

            // Create and store cancellation token source
            _deletionCancellationTokenSource = new CancellationTokenSource();
            IsCancellationRequested = false;

            // Start deletion
            IsDeletionInProgress = true;
            DeletionProgress = 0;
            DeletionStatus = "Preparing deletion...";

            var moveToBinResult = await _deleteService.MoveToRecycleBinAsync(ItemsToDelete, new Progress<DeleteProgress>(progress =>
            {
                DeletionProgress = progress.ProgressPercentage;
                DeletionStatus = $"Deleting: {progress.CurrentItemPath} ({progress.ProcessedItemCount}/{progress.TotalItemCount})";
            }), _deletionCancellationTokenSource.Token);

            // Show summary dialog
            await ShowDeletionSummaryAsync(moveToBinResult);

            // If deletion was successful, close the window
            if (moveToBinResult.Success && ItemsToDelete.Count == 0)
            {
                RequestClose?.Invoke(moveToBinResult);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Move to recycle bin operation was cancelled by user");
            DeletionStatus = "Deletion cancelled.";
            await Task.Delay(2000); // Show cancellation message briefly
        }
        catch (Exception e)
        {
            Log.Error(e, "Error during move to recycle bin operation");
            await _errorDialogService.ShowErrorAsync(
                "Deletion Failed",
                "An unexpected error occurred during deletion.",
                e);
        }
        finally
        {
            IsDeletionInProgress = false;
            IsCancellationRequested = false;
            DeletionProgress = 0;
            DeletionStatus = string.Empty;
            _deletionCancellationTokenSource?.Dispose();
            _deletionCancellationTokenSource = null;
            CalculateTotalSize();
            BuildGroupedItems();
        }
    }

    [RelayCommand]
    private async Task DeletePermanentlyAsync()
    {
        try
        {
            // Show confirmation dialog with warning
            var confirmationMessage = BuildConfirmationMessage("permanently delete (this cannot be undone)");
            
            var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
                "Confirm Permanent Deletion",
                confirmationMessage);

            if (!confirmed)
                return;

            // Create and store cancellation token source
            _deletionCancellationTokenSource = new CancellationTokenSource();
            IsCancellationRequested = false;

            // Start deletion
            IsDeletionInProgress = true;
            DeletionProgress = 0;
            DeletionStatus = "Preparing deletion...";
            
            var deleteResult = await _deleteService.DeletePermanentlyAsync(ItemsToDelete, new Progress<DeleteProgress>(progress =>
            {
                DeletionProgress = progress.ProgressPercentage;
                DeletionStatus = $"Deleting: {progress.CurrentItemPath} ({progress.ProcessedItemCount}/{progress.TotalItemCount})";
            }), _deletionCancellationTokenSource.Token);

            // Show summary dialog
            await ShowDeletionSummaryAsync(deleteResult);

            // If deletion was successful, close the window
            if (deleteResult.Success && ItemsToDelete.Count == 0)
            {
                RequestClose?.Invoke(deleteResult);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Permanent delete operation was cancelled by user");
            DeletionStatus = "Deletion cancelled.";
            await Task.Delay(2000); // Show cancellation message briefly
        }
        catch (Exception e)
        {
            Log.Error(e, "Error during permanent delete operation");
            await _errorDialogService.ShowErrorAsync(
                "Deletion Failed",
                "An unexpected error occurred during deletion.",
                e);
        }
        finally
        {
            IsDeletionInProgress = false;
            IsCancellationRequested = false;
            DeletionProgress = 0;
            DeletionStatus = string.Empty;
            _deletionCancellationTokenSource?.Dispose();
            _deletionCancellationTokenSource = null;
            CalculateTotalSize();
            BuildGroupedItems();
        }
    }

    /// <summary>
    /// Builds a confirmation message showing the count of items and total size to be deleted.
    /// </summary>
    private string BuildConfirmationMessage(string action)
    {
        var itemCount = ItemsToDelete.Count;
        var totalSize = TotalSizeFormatted;
        
        return $"You are about to {action}:\n\n" +
               $"Items: {itemCount}\n" +
               $"Total Size: {totalSize}\n\n" +
               $"Are you sure you want to proceed?";
    }

    /// <summary>
    /// Shows a summary dialog of the deletion result.
    /// </summary>
    private async Task ShowDeletionSummaryAsync(DeleteResult result)
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine("Deletion Complete\n");
        summary.AppendLine($"Deleted Items: {result.DeletedCount}");
        summary.AppendLine($"Failed Items: {result.FailedCount}");
        summary.AppendLine($"Storage Freed: {ByteConverter.ToReadableString(result.TotalBytesFreed)}");

        if (result.FailedCount > 0 && result.Errors.Count > 0)
        {
            summary.AppendLine("\nFailed Deletions:");
            foreach (var error in result.Errors.Take(5)) // Show first 5 errors
            {
                summary.AppendLine($"• {System.IO.Path.GetFileName(error.ItemPath)}: {error.ErrorMessage}");
            }

            if (result.Errors.Count > 5)
            {
                summary.AppendLine($"... and {result.Errors.Count - 5} more errors");
            }
        }

        var title = result.Success ? "Deletion Successful" : "Deletion Completed with Errors";
        await _errorDialogService.ShowErrorAsync(title, summary.ToString());
    }

    private void UpdateCanDelete()
    {
        CanDelete = ItemsToDelete.Count > 0 && !IsDeletionInProgress;
        CanCancelDeletion = IsDeletionInProgress && !IsCancellationRequested;
    }

    private void ItemsToDeleteOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCanDelete();

    partial void OnIsDeletionInProgressChanged(bool value) => UpdateCanDelete();

    partial void OnIsCancellationRequestedChanged(bool value) => UpdateCanDelete();

    private void CalculateTotalSize()
    {
        // Filter to only top-level items (exclude items nested within other items)
        var topLevelItems = TreeNodeHelper.GetTopLevelItems(ItemsToDelete);
        long totalBytes = topLevelItems.Sum(item => item.Size);
        TotalSizeFormatted = ByteConverter.ToReadableString(totalBytes);
    }
    
    private void BuildGroupedItems()
    {
        LargeDirectories.Clear();
        LargeFiles.Clear();
        OldFiles.Clear();
        OtherItems.Clear();
        
        if(ItemsToDelete.Count == 0)
            return;
        
        // Filter to only top-level items (exclude items nested within other items).
        // This prevents double-counting sizes when both a parent and child are selected.
        // Items are then categorised by IsDirectory so that a large directory never
        // appears in the 'large files' group and vice-versa.
        var topLevelItems = TreeNodeHelper.GetTopLevelItems(ItemsToDelete);
        
        var now = DateTime.Now;
        var largeFilesList = new List<TreeNode>();
        var largeDirectoriesList = new List<TreeNode>();
        var oldFilesList = new List<TreeNode>();
        
        foreach (var item in topLevelItems)
        {
            if (item.Size >= LargeFileSizeThresholdBytes)
            {
                if (item.IsDirectory)
                {
                    largeDirectoriesList.Add(item);
                }
                else
                {
                    largeFilesList.Add(item);
                }
            }
            
            if (item.LastModified.HasValue && (now - item.LastModified.Value) > OldFileThreshold)
            {
                oldFilesList.Add(item);
            }
        }
        
        // Populate Large Directories collection
        foreach (var item in largeDirectoriesList.OrderByDescending(d => d.Size))
        {
            LargeDirectories.Add(item);
        }
        LargeDirectoriesSize = ByteConverter.ToReadableString(largeDirectoriesList.Sum(d => d.Size));
        
        // Populate Large Files collection
        foreach (var item in largeFilesList.OrderByDescending(f => f.Size))
        {
            LargeFiles.Add(item);
        }
        LargeFilesSize = ByteConverter.ToReadableString(largeFilesList.Sum(f => f.Size));
        
        // Populate Old Files collection
        foreach (var item in oldFilesList.OrderBy(f => f.LastModified)) // Oldest first
        {
            OldFiles.Add(item);
        }
        OldFilesSize = ByteConverter.ToReadableString(oldFilesList.Sum(f => f.Size));
        
        // Populate Other Items collection
        var categorizedItems = new HashSet<TreeNode>(largeFilesList.Concat(largeDirectoriesList).Concat(oldFilesList));
        var otherItemsList = topLevelItems.Where(item => !categorizedItems.Contains(item)).ToList();
        
        foreach (var item in otherItemsList)
        {
            OtherItems.Add(item);
        }
        OtherItemsSize = ByteConverter.ToReadableString(otherItemsList.Sum(i => i.Size));
    }
    
    public void SetTopLevel(TopLevel topLevel) => _topLevel = topLevel;
    
    /// <summary>
    /// Cleanup method to be called when the window is closing.
    /// Cancels any in-progress deletion operation.
    /// </summary>
    public void OnWindowClosing()
    {
        // If deletion is in progress, cancel it
        if (IsDeletionInProgress && _deletionCancellationTokenSource is not null && 
            !_deletionCancellationTokenSource.IsCancellationRequested)
        {
            Log.Information("Window closing during deletion - cancelling deletion operation");
            _deletionCancellationTokenSource.Cancel();
        }
    }
    
    [RelayCommand]
    private void RemoveFromCleanup(TreeNode? item)
    {
        if (item is null) 
            return;
        
        // Remove the item from _itemsToDelete
        ItemsToDelete.Remove(item);
      
        // Recalculate total size
        CalculateTotalSize();
        
        BuildGroupedItems();
        
        // Clear selection
        SelectedItem = null;
    }

     [RelayCommand]
     private async Task ContextMenuSelect(TreeNode? node)
     {
         // For cleanup window, "select" means removing from cleanup or marking differently
         // For now, we'll just toggle it or you can adapt this as needed
         if (node is not null && CanDelete)
         {
             RemoveFromCleanup(node);
         }
         await Task.CompletedTask;
     }

     [RelayCommand]
     private async Task ContextMenuOpenInExplorer(TreeNode? node)
     {
         try
         {
             if (node is null)
                 return;

             _explorerLauncher.OpenInExplorer(node.FullPath);
         }
         catch (Exception e)
         {
             Log.Error(e, "Failed to open node in explorer: {NodePath}", node?.FullPath);
             await _errorDialogService.ShowErrorAsync(
                 "Failed to Open in Explorer",
                 $"Could not open the path in File Explorer:\n{node?.FullPath}",
                 e);
         }
     }

     [RelayCommand]
     private async Task ContextMenuCopyPath(TreeNode? node)
     {
         try
         {
             if (node is null)
                 return;

             if (_topLevel?.Clipboard is IClipboard clipboard)
             {
                 await clipboard.SetTextAsync(node.FullPath);
             }
         }
         catch (Exception e)
         {
             Log.Error(e, "Failed to copy path to clipboard: {NodePath}", node?.FullPath);
             await _errorDialogService.ShowErrorAsync(
                 "Failed to Copy Path",
                 $"Could not copy the path to clipboard:\n{node?.FullPath}",
                 e);
         }
     }
}
