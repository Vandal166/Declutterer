using System;
using System.Collections.Generic;
using Declutterer.Domain.Models;
using NodeSelectionScore = Declutterer.Domain.Models.NodeSelectionScore;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Domain.Services.Selection;

public sealed class SmartSelectionScorer
{
    // computes scores for every node in the given tree root
    public List<NodeSelectionScore> ComputeScores(TreeNode root, ScanOptions scanOptions, ScorerOptions scorerOptions) 
    {
        // 1) gather normalization data (two-pass model)
        var stats = GatherTreeStats(root);

        // 2) traverse again and compute a score per node
        var results = new List<NodeSelectionScore>();
        Traverse(root, node => 
        {
            var score = ScoreNode(node, scanOptions, scorerOptions, stats);
            results.Add(score);
        });

        return results;
    }

    // collect observed metrics necessary for normalization
    private static (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) GatherTreeStats(TreeNode root) 
    {
        long maxSize = 0;
        DateTime? oldest = null;
        DateTime? newest = null;

        Traverse(root, node => 
        {
            maxSize = Math.Max(maxSize, node.Size);
            var lm = node.LastModified;
            if (oldest == null || lm < oldest) oldest = lm;
            if (newest == null || lm > newest) newest = lm;
        });

        return (maxSize, oldest, newest);
    }

    // a simple tree traversal helper (depth-first)
    private static void Traverse(TreeNode node, Action<TreeNode> action) 
    {
        action(node);
        if (node.Children != null) 
        {
            foreach (var child in node.Children) 
                Traverse(child, action);
        }
    }

    private static NodeSelectionScore ScoreNode(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) 
    {
        var ageScore = ComputeAgeScore(node, scanOptions, scorerOptions, stats);
        var sizeScore = ComputeSizeScore(node, scanOptions, scorerOptions, stats);

        // weighted average (normalize weights)
        double totalWeight = scorerOptions.WeightAge + scorerOptions.WeightSize;
        totalWeight = totalWeight <= 0 ? 1 : totalWeight;
        var combined = (ageScore * scorerOptions.WeightAge + sizeScore * scorerOptions.WeightSize) / totalWeight;

        return new NodeSelectionScore {
            Node = node,
            AgeScore = Clamp01(ageScore),
            SizeScore = Clamp01(sizeScore),
            CombinedScore = Clamp01(combined)
        };
    }

    private static double ComputeAgeScore(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) 
    {
        // If age filter is not enabled or node has no LastModified date, return neutral score
        if (!scanOptions.AgeFilter.UseModifiedDate || !node.LastModified.HasValue) 
            return 0.5;

        // Determine cutoff date from filter settings
        DateTime? cutoff = scanOptions.AgeFilter.ModifiedBefore;
        if (!cutoff.HasValue && scanOptions.AgeFilter.MonthsModifiedValue > 0) 
        {
            cutoff = DateTime.UtcNow.AddMonths(-scanOptions.AgeFilter.MonthsModifiedValue);
        }
        if (!cutoff.HasValue) 
            return 0.5; // neutral

        // compute how far node is beyond cutoff in days
        var deltaDays = (cutoff.Value - node.LastModified.Value).TotalDays; 
        // positive deltaDays => node is older than cutoff (worse/more eligible depending semantics)
        // choose semantic: we want higher score for nodes that are older than the threshold (i.e., match criteria)
        // So normalize based on deltaDays relative to a reasonable 'maxAgeSpan' (use stats.NewestDate - stats.OldestDate or fixed window)
        double maxSpanDays = (stats.NewestDate.HasValue && stats.OldestDate.HasValue)
            ? Math.Max((stats.NewestDate.Value - stats.OldestDate.Value).TotalDays, 1)
            : 365; // fallback

        // normalizedDistance = clamp(deltaDays / maxSpanDays) -> 0..1
        double normalized = Clamp01(deltaDays / maxSpanDays);

        
        // linear: older => higher score
        return normalized;
    }

    private static double ComputeSizeScore(TreeNode node, ScanOptions scanOptions, ScorerOptions scorerOptions, (long MaxSize, DateTime? OldestDate, DateTime? NewestDate) stats) 
    {
        //TODO - consider directory size if node is a directory and DirectorySizeFilter is enabled
        // If size filter is not enabled, return neutral score
        if (!scanOptions.FileSizeFilter.UseSizeFilter || scanOptions.FileSizeFilter.SizeThreshold <= 0 || stats.MaxSize <= 0) 
            return 0.5;

        // Convert threshold from MB to bytes (same as ScanFilterService does)
        long thresholdBytes = scanOptions.FileSizeFilter.SizeThreshold * 1024 * 1024;
        var maxSize = Math.Max(stats.MaxSize, thresholdBytes); // avoid div by zero
        double sizeValue = node.Size;

        // linear interpolation from threshold -> maxSize
        if (sizeValue <= thresholdBytes) 
            return 0.0; // below threshold -> no match
        
        // linear interpolation from threshold -> maxSize
        double normalized = (sizeValue - thresholdBytes) / (maxSize - thresholdBytes);
        normalized = Clamp01(normalized);

        
        return normalized; // linear: larger files get higher score proportionally
    }

    private static double Clamp01(double v) => Math.Max(0.0, Math.Min(1.0, v));
}