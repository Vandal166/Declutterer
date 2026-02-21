using System.IO;

namespace Declutterer.Domain.Models;

/// <summary>
/// Lightweight wrapper to pass both FileSystemInfo and pre-calculated size through the filter pipeline.
/// Using a class (not record) for better performance with mutable CalculatedSize field.
/// </summary>
public sealed class FileSystemInfoWrapper
{
    public required FileSystemInfo Info { get; init; }
    public long? CalculatedSize { get; set; } // nullable, since we might not have calculated the size yet, especially for directories, for files this will be set to the file size
}