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
    private ObservableCollection<HistoryEntryGroupViewModel> groupedEntries = new();

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool hasEntries = false;

    [ObservableProperty]
    private int totalEntryCount = 0;

    [ObservableProperty]
    private long totalDeletedSizeBytes = 0;

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
        if (entry == null)
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

    public string TotalDeletedSizeFormatted => ByteConverter.ToReadableString(TotalDeletedSizeBytes);
}

/// <summary>
/// Represents a group of deletion history entries for a specific date.
/// Used for grouping entries in the history view by deletion date.
/// </summary>
public sealed partial class HistoryEntryGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private DateTime groupDate;

    [ObservableProperty]
    private ObservableCollection<DeletionHistoryEntryViewModel> entries = new();

    [ObservableProperty]
    private long groupTotalSizeBytes = 0;

    [ObservableProperty]
    private int groupEntryCount = 0;

    [ObservableProperty]
    private bool isExpanded = true;

    public HistoryEntryGroupViewModel(DateTime date, System.Collections.Generic.List<DeletionHistoryEntry> groupEntries)
    {
        GroupDate = date;
        GroupEntryCount = groupEntries.Count;
        GroupTotalSizeBytes = groupEntries.Sum(e => e.SizeBytes);

        foreach (var entry in groupEntries)
        {
            Entries.Add(new DeletionHistoryEntryViewModel(entry));
        }
    }

    public string GroupDateFormatted => GroupDate.ToString("dddd, MMMM d, yyyy");

    public string GroupSizeFormatted => ByteConverter.ToReadableString(GroupTotalSizeBytes);
}

/// <summary>
/// Represents a single deletion history entry in the UI.
/// Provides formatted display properties for the history view.
/// </summary>
public sealed partial class DeletionHistoryEntryViewModel : ViewModelBase
{
    [ObservableProperty]
    private DeletionHistoryEntry entry;

    public DeletionHistoryEntryViewModel(DeletionHistoryEntry entry)
    {
        Entry = entry;
    }

    public string SizeFormatted => ByteConverter.ToReadableString(Entry.SizeBytes);

    public string TypeBadge => Entry.DeletionType switch
    {
        "RecycleBin" => "🗑️ Recycle Bin",
        "Permanent" => "🔴 Permanent",
        _ => Entry.DeletionType
    };

    public string TypeColor => Entry.DeletionType switch
    {
        "RecycleBin" => "#FFA500", // Orange for Recycle Bin
        "Permanent" => "#DC143C",  // Crimson for Permanent
        _ => "#999999"
    };

    public string ItemTypeIcon => Entry.IsDirectory ? "📁" : "📄";

    public string DeletionTimeFormatted => Entry.DeletionDateTime.ToString("HH:mm:ss");

    public string DisplayPath => PathExtensions.GetMiddleEllipsis(Entry.Path, 60);
}
