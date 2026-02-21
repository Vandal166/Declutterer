namespace Declutterer.Domain.Services.Deletion;

public sealed class DeleteProgress
{
    public int ProcessedItemCount { get; set; }
    public int TotalItemCount { get; set; }
    public string CurrentItemPath { get; set; }
    public double ProgressPercentage { get; set; }
}