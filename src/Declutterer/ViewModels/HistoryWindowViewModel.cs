using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;
using Serilog;

namespace Declutterer.ViewModels;

/// <summary>
/// ViewModel for the deletion history window/view.
/// Groups and displays deletion history entries in a user-friendly manner.
/// Handles filtering, empty state management, and navigation.
/// </summary>
public sealed partial class HistoryWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<HistoryEntryGroupViewModel> _groupedEntries = new();

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _hasEntries = false;

    [ObservableProperty]
    private int _totalEntryCount = 0;

    [ObservableProperty]
    private long _totalDeletedSizeBytes = 0;

    [ObservableProperty]
    private string _totalDeletedSizeFormatted = "0B";

    private readonly IDeletionHistoryRepository _historyRepository;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private event Action? _onHideHistory;
    
    public HistoryWindowViewModel(IDeletionHistoryRepository historyRepository, IConfirmationDialogService confirmationDialogService)
    {
        _historyRepository = historyRepository;
        _confirmationDialogService = confirmationDialogService;
    }

    public HistoryWindowViewModel() { } // for designer

    public void SetHideHistoryCallback(Action onHideHistory)
    {
        _onHideHistory = onHideHistory;
    }
    
    public void SetOwnerWindow(Window window)
    {
        _confirmationDialogService.SetOwnerWindow(window);
    }
    /// <summary>
    /// Loads the deletion history and groups entries by date.
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        IsLoading = true;
        try
        {
            var entries = await _historyRepository.GetEntriesAsync();

            if (entries.Count == 0)
            {
                GroupedEntries.Clear();
                HasEntries = false;
                TotalEntryCount = 0;
                TotalDeletedSizeBytes = 0;
                TotalDeletedSizeFormatted = "0B";
                return;
            }

            // Group entries by date (grouped by the date part only, ignoring time)
            var groupedByDate = entries
                .GroupBy(e => e.DeletionDateTime.Date)
                .OrderByDescending(g => g.Key)
                .ToList();

            GroupedEntries.Clear();

            foreach (var group in groupedByDate)
            {
                var groupViewModel = new HistoryEntryGroupViewModel(group.Key, group.ToList());
                GroupedEntries.Add(groupViewModel);
            }

            HasEntries = true;
            TotalEntryCount = entries.Count;
            TotalDeletedSizeBytes = entries.Sum(e => e.SizeBytes);
            TotalDeletedSizeFormatted = ByteConverter.ToReadableString(TotalDeletedSizeBytes);

            Log.Information("Loaded deletion history: {EntryCount} entries grouped into {GroupCount} groups",
                TotalEntryCount, GroupedEntries.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading deletion history");
            HasEntries = false;
            GroupedEntries.Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            // Request user confirmation before clearing
            var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
                "Clear History",
                "Are you sure you want to clear all deletion history? This action cannot be undone.");

            if (!confirmed)
            {
                Log.Information("User cancelled clear history operation");
                return;
            }

            await _historyRepository.ClearHistoryAsync();
            await LoadHistoryAsync();
            Log.Information("Deletion history cleared");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing deletion history");
        }
    }

    [RelayCommand]
    private void HideHistory()
    {
        // Invoke the callback to notify the parent (MainWindowViewModel) to hide history
        _onHideHistory?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(DeletionHistoryEntry entry)
    {
        if (entry is null)
            return;

        try
        {
            await _historyRepository.DeleteEntryAsync(entry.Id);

            // Reload the history to update the display
            await LoadHistoryAsync();

            Log.Information("Deleted history entry: {EntryId} - {Name}", entry.Id, entry.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting history entry: {EntryId}", entry.Id);
        }
    }
}