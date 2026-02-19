using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Models;
using Serilog;

namespace Declutterer.Services;

/// <summary>
/// JSON file-based implementation of the deletion history repository.
/// Stores deletion records in %APPDATA%\Declutterer\history\deletion_history.json
/// </summary>
public sealed class DeletionHistoryRepository : IDeletionHistoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _historyFilePath;
    private readonly string _historyDirectory;
    private readonly Lock _fileLock = new();

    public DeletionHistoryRepository()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _historyDirectory = Path.Combine(appDataPath, "Declutterer", "history");
        _historyFilePath = Path.Combine(_historyDirectory, "deletion_history.json");

        try
        {
            // Ensure directory exists
            if (!Directory.Exists(_historyDirectory))
            {
                Directory.CreateDirectory(_historyDirectory);
                Log.Information("Created deletion history directory: {HistoryDirectory}", _historyDirectory);
            }

            // Initialize history file if it doesn't exist
            if (!File.Exists(_historyFilePath))
            {
                InitializeHistoryFile();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing deletion history repository at {HistoryFilePath}", _historyFilePath);
            throw;
        }
    }

    private void InitializeHistoryFile()
    {
        try
        {
            var initialData = new DeletionHistoryData
            {
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                Entries = new List<DeletionHistoryEntry>()
            };

            var json = JsonSerializer.Serialize(initialData, JsonOptions);
            lock (_fileLock)
            {
                File.WriteAllText(_historyFilePath, json, Encoding.UTF8);
            }

            Log.Information("Initialized deletion history file: {HistoryFilePath}", _historyFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing deletion history file");
            throw;
        }
    }

    public async Task AddEntryAsync(DeletionHistoryEntry entry)
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            data.Entries.Add(entry);
            await SaveHistoryDataAsync(data);

            Log.Information(
                "Added deletion history entry: {Name} ({SizeBytes} bytes) - {DeletionType}",
                entry.Name, entry.SizeBytes, entry.DeletionType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding deletion history entry for {Path}", entry.Path);
            throw;
        }
    }

    public async Task<IList<DeletionHistoryEntry>> GetEntriesAsync()
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            return data.Entries.OrderByDescending(e => e.DeletionDateTime).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving deletion history entries");
            return new List<DeletionHistoryEntry>();
        }
    }

    public async Task<IList<DeletionHistoryEntry>> GetEntriesBetweenAsync(DateTime from, DateTime to)
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            return data.Entries
                .Where(e => e.DeletionDateTime >= from && e.DeletionDateTime <= to)
                .OrderByDescending(e => e.DeletionDateTime)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving deletion history entries between {From} and {To}", from, to);
            return new List<DeletionHistoryEntry>();
        }
    }

    public async Task<IList<DeletionHistoryEntry>> GetEntriesByPathAsync(string pathPattern)
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            var pattern = pathPattern.Replace("*", ".*").Replace("?", ".");
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return data.Entries
                .Where(e => regex.IsMatch(e.Path))
                .OrderByDescending(e => e.DeletionDateTime)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving deletion history entries for pattern {PathPattern}", pathPattern);
            return new List<DeletionHistoryEntry>();
        }
    }

    public async Task DeleteEntryAsync(string entryId)
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            var entry = data.Entries.FirstOrDefault(e => e.Id == entryId);

            if (entry != null)
            {
                data.Entries.Remove(entry);
                await SaveHistoryDataAsync(data);
                Log.Information("Deleted history entry with ID: {EntryId}", entryId);
            }
            else
            {
                Log.Warning("History entry not found: {EntryId}", entryId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting history entry: {EntryId}", entryId);
            throw;
        }
    }

    public async Task ClearHistoryAsync()
    {
        try
        {
            InitializeHistoryFile();
            Log.Information("Cleared all deletion history");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing deletion history");
            throw;
        }
    }

    public async Task<int> GetEntryCountAsync()
    {
        try
        {
            var data = await LoadHistoryDataAsync();
            return data.Entries.Count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting deletion history entry count");
            return 0;
        }
    }

    private async Task<DeletionHistoryData> LoadHistoryDataAsync()
    {
        try
        {
            lock (_fileLock)
            {
                if (!File.Exists(_historyFilePath))
                {
                    InitializeHistoryFile();
                }

                var json = File.ReadAllText(_historyFilePath, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<DeletionHistoryData>(json, JsonOptions);
                return data ?? new DeletionHistoryData { Version = "1.0", Entries = new List<DeletionHistoryEntry>() };
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading deletion history data from {HistoryFilePath}", _historyFilePath);
            throw;
        }
    }

    private async Task SaveHistoryDataAsync(DeletionHistoryData data)
    {
        try
        {
            data.LastModifiedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(data, JsonOptions);
            
            lock (_fileLock)
            {
                // Write to temporary file first for atomic operation
                var tempFilePath = _historyFilePath + ".tmp";
                File.WriteAllText(tempFilePath, json, Encoding.UTF8);

                // Replace the original file
                if (File.Exists(_historyFilePath))
                {
                    File.Delete(_historyFilePath);
                }

                File.Move(tempFilePath, _historyFilePath);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving deletion history data to {HistoryFilePath}", _historyFilePath);
            throw;
        }
    }

    /// <summary>
    /// Internal data structure for serialization/deserialization.
    /// </summary>
    private sealed class DeletionHistoryData
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("lastModifiedAt")]
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("entries")]
        public List<DeletionHistoryEntry> Entries { get; set; } = new();
    }
}
