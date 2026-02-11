namespace Declutterer.Models;

public class ScorerOptions
{
    // scoring config
    public double WeightAge { get; set; } = 0.5;
    public double WeightSize { get; set; } = 0.5;
    
    public double TopPercentage { get; set; } = 0.4; // select top n% of nodes by score
}