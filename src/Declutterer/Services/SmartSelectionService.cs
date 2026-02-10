using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Services;

public sealed class SmartSelectionService
{
    private readonly SmartSelectionScorer _scorer;

    public SmartSelectionService(SmartSelectionScorer scorer)
    {
        _scorer = scorer;
    }
    
    public List<TreeNode> Select(TreeNode root, ScanOptions scanOptions, ScorerOptions scorerOptions) 
    {
        var scored = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        // Filter out the root node - it should never be selected for deletion
        scored = scored.Where(s => s.Node != root).ToList();

        // Sort by CombinedScore descending
        var sorted = scored.OrderByDescending(s => s.CombinedScore).ToList();

        // Select top percentage of nodes by score
        int take = Math.Max(1, (int)(sorted.Count * scorerOptions.TopPercentage));
        var selected = sorted
            .Take(take)
            .Select(s => s.Node)
            .ToList();
        
        // Filter out nested items - if a parent directory is selected, don't also include its children
        // This prevents double-counting sizes and showing inflated item counts
        return TreeNodeHelper.GetTopLevelItems(selected);
    }
}