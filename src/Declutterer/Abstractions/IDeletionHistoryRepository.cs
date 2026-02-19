using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Abstractions;

/// <summary>
/// Interface for managing deletion history persistence.
/// Provides methods to store and retrieve deletion records.
/// </summary>
public interface IDeletionHistoryRepository
{
    Task AddEntryAsync(DeletionHistoryEntry entry);

    Task<IList<DeletionHistoryEntry>> GetEntriesAsync();

    /// <summary>
    /// Retrieves deletion entries within a date range.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <returns>List of deletion history entries within the date range, ordered by deletion date descending.</returns>
    Task<IList<DeletionHistoryEntry>> GetEntriesBetweenAsync(DateTime from, DateTime to);

    /// <summary>
    /// Retrieves deletion entries by path pattern.
    /// </summary>
    /// <param name="pathPattern">Path pattern to search (supports wildcards).</param>
    /// <returns>List of deletion history entries matching the path pattern.</returns>
    Task<IList<DeletionHistoryEntry>> GetEntriesByPathAsync(string pathPattern);

    Task DeleteEntryAsync(string entryId);

    /// <summary>
    /// Clears all deletion history.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearHistoryAsync();

    Task<int> GetEntryCountAsync();
}
