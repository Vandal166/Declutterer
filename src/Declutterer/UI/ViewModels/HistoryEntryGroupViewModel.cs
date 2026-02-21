using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Declutterer.Domain.Models;
using Declutterer.Utilities.Helpers;

namespace Declutterer.UI.ViewModels;

/// <summary>
/// Represents a group of deletion history entries for a specific date.
/// Used for grouping entries in the history view by deletion date.
/// </summary>
public sealed partial class HistoryEntryGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private DateTime _groupDate;

    [ObservableProperty]
    private ObservableCollection<DeletionHistoryEntryViewModel> _entries = new();

    [ObservableProperty]
    private long _groupTotalSizeBytes = 0;

    [ObservableProperty]
    private int _groupEntryCount = 0;

    [ObservableProperty]
    private bool _isExpanded = true;

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