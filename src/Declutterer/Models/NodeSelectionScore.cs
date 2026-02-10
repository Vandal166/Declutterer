using Declutterer.Models;

namespace Declutterer.Models;

public class NodeSelectionScore 
{
    public required TreeNode Node { get; init; }
    public double AgeScore { get; set; }        // 0.0 - 1.0
    public double SizeScore { get; set; }       // 0.0 - 1.0
    public double CombinedScore { get; init; }   // weighted average 0.0 - 1.0
}