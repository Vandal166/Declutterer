using System;

namespace Declutterer.Models;

/// <summary>
/// Represents a single deletion history entry.
/// Records metadata about a deleted file or directory.
/// </summary>
public sealed class DeletionHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Name of the deleted file or directory.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Size in bytes. For directories, this is the total size.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Date and time when the item was deleted.
    /// </summary>
    public DateTime DeletionDateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// Type of deletion: "RecycleBin" or "Permanent".
    /// </summary>
    public string DeletionType { get; set; } = string.Empty;
    
    public bool IsDirectory { get; set; }

    /// <summary>
    /// Parent directory path (if applicable).
    /// </summary>
    public string? ParentPath { get; set; }
}
