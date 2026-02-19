using System.Collections.Generic;

namespace Declutterer.Common;

public sealed class DeleteResult
{
    public bool Success { get; set; }
    public int DeletedCount { get; set; }
    public int FailedCount { get; set; }
    public List<DeletionError> Errors { get; set; } = new();
    public long TotalBytesFreed { get; set; }
}