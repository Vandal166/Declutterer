using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Models;
using Declutterer.Services;

namespace Declutterer.Tests.Integration;

public class SmartSelectionIntegrationTests
{
    private readonly SmartSelectionScorer _scorer;
    private readonly SmartSelectionService _selectionService;

    public SmartSelectionIntegrationTests()
    {
        _scorer = new SmartSelectionScorer();
        _selectionService = new SmartSelectionService(_scorer);
    }

    [Fact]
    public void FullPipeline_ScoreAndSelect_WorksCorrectly()
    {
        var root = CreateTestTree();
        var scanOptions = CreateScanOptionsWithFilters();
        var scorerOptions = new ScorerOptions { TopPercentage = 0.5 };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.NotEmpty(selected);
        Assert.DoesNotContain(root, selected);
        Assert.All(selected, node => Assert.Equal(root, node.Parent));
    }

    [Fact]
    public void ScoringPipeline_WithSizeWeight_PrefersLargeFiles()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var small = CreateTreeNode("small", size: 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        var large = CreateTreeNode("large", size: 100 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        root.Children.Add(small);
        root.Children.Add(large);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var scorerOptions = new ScorerOptions 
        { 
            WeightSize = 1.0, 
            WeightAge = 0.0,
            TopPercentage = 0.5 
        };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.Single(selected);
        Assert.Equal("large", selected[0].Name);
    }

    [Fact]
    public void ScoringPipeline_WithAgeWeight_PrefersOlderFiles()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var recent = CreateTreeNode("recent", size: 10 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddDays(-30));
        var old = CreateTreeNode("old", size: 10 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        root.Children.Add(recent);
        root.Children.Add(old);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-1)
            }
        };
        var scorerOptions = new ScorerOptions 
        { 
            WeightSize = 0.0, 
            WeightAge = 1.0,
            TopPercentage = 0.5 
        };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.Single(selected);
        Assert.Equal("old", selected[0].Name);
    }

    [Fact]
    public void ScoringPipeline_BalancedWeights_ConsidersBoth()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var oldLarge = CreateTreeNode("old_large", size: 100 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        var oldSmall = CreateTreeNode("old_small", size: 1 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-12));
        var recentLarge = CreateTreeNode("recent_large", size: 100 * 1024 * 1024, parent: root, lastModified: DateTime.UtcNow.AddDays(-7));
        root.Children.Add(oldLarge);
        root.Children.Add(oldSmall);
        root.Children.Add(recentLarge);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-1)
            }
        };
        var scorerOptions = new ScorerOptions 
        { 
            WeightSize = 0.5, 
            WeightAge = 0.5,
            TopPercentage = 0.34 // Select top 1 out of 3
        };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.Single(selected);
        Assert.Equal("old_large", selected[0].Name); // Best combination of size and age
    }

    [Fact]
    public void SelectionPipeline_TopPercentage_SelectsCorrectAmount()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        for (int i = 0; i < 10; i++)
        {
            var child = CreateTreeNode($"child{i}", size: (i + 1) * 1024 * 1024, parent: root, 
                lastModified: DateTime.UtcNow.AddMonths(-(i + 1)));
            root.Children.Add(child);
        }

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.3 }; // 30% of 10 = 3

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.Equal(3, selected.Count);
    }

    [Fact]
    public void SelectionPipeline_FiltersNestedItems()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var parent = CreateTreeNode("parent", size: 100 * 1024 * 1024, parent: root, isDir: true, 
            lastModified: DateTime.UtcNow.AddMonths(-6));
        var child = CreateTreeNode("child", size: 50 * 1024 * 1024, parent: parent, 
            lastModified: DateTime.UtcNow.AddMonths(-6));
        root.Children.Add(parent);
        parent.Children.Add(child);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 1.0 }; // Select all

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        // Should only select parent, not child (nested filtering)
        Assert.Single(selected);
        Assert.Equal("parent", selected[0].Name);
    }

    [Fact]
    public void ComputeScores_CompleteTree_ScoresAllNodes()
    {
        var root = CreateTestTree();
        var scanOptions = CreateScanOptionsWithFilters();
        var scorerOptions = new ScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        // Should score root + all children
        Assert.NotEmpty(scores);
        Assert.Contains(scores, s => s.Node == root);
        Assert.All(scores, s => Assert.InRange(s.CombinedScore, 0.0, 1.0));
        Assert.All(scores, s => Assert.InRange(s.AgeScore, 0.0, 1.0));
        Assert.All(scores, s => Assert.InRange(s.SizeScore, 0.0, 1.0));
    }

    [Fact]
    public void ComputeScores_WithoutFilters_ReturnsNeutralScores()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var child = CreateTreeNode("child", size: 1024, parent: root);
        root.Children.Add(child);

        var scanOptions = new ScanOptions(); // No filters enabled
        var scorerOptions = new ScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        // Without filters, scores should be neutral (0.5)
        var childScore = scores.First(s => s.Node == child);
        Assert.Equal(0.5, childScore.AgeScore);
        Assert.Equal(0.5, childScore.SizeScore);
    }

    [Fact]
    public void EndToEndPipeline_RealisticScenario()
    {
        // Create a realistic directory structure
        var root = CreateTreeNode("Documents", size: 1000, isDir: true);
        
        // Old, large project folder
        var oldProject = CreateTreeNode("OldProject", size: 500 * 1024 * 1024, parent: root, isDir: true,
            lastModified: DateTime.UtcNow.AddMonths(-18));
        
        // Recent, small documents
        var recentDoc = CreateTreeNode("Recent.docx", size: 50 * 1024, parent: root,
            lastModified: DateTime.UtcNow.AddDays(-5));
        
        // Old, medium-sized file
        var oldFile = CreateTreeNode("OldBackup.zip", size: 150 * 1024 * 1024, parent: root,
            lastModified: DateTime.UtcNow.AddMonths(-24));
        
        // Recent, large video
        var recentVideo = CreateTreeNode("Video.mp4", size: 1024 * 1024 * 1024, parent: root,
            lastModified: DateTime.UtcNow.AddDays(-10));
        
        root.Children.Add(oldProject);
        root.Children.Add(recentDoc);
        root.Children.Add(oldFile);
        root.Children.Add(recentVideo);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 10 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-6)
            }
        };
        var scorerOptions = new ScorerOptions 
        { 
            WeightSize = 0.6, 
            WeightAge = 0.4,
            TopPercentage = 0.5 // Select top 50%
        };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        // Should select the old, large items (not recent video as it doesn't pass age filter)
        Assert.NotEmpty(selected);
        // The selection should prioritize old, large items
        Assert.Contains(selected, node => node.Name == "OldProject" || node.Name == "OldBackup.zip");
    }

    [Fact]
    public void Select_OnlyDirectChildrenOfRoot_AreConsidered()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var child1 = CreateTreeNode("child1", size: 10 * 1024 * 1024, parent: root, isDir: true,
            lastModified: DateTime.UtcNow.AddMonths(-6));
        var grandchild = CreateTreeNode("grandchild", size: 50 * 1024 * 1024, parent: child1,
            lastModified: DateTime.UtcNow.AddMonths(-12));
        
        root.Children.Add(child1);
        child1.Children.Add(grandchild);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 1.0 };

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        // Only direct children should be selected
        Assert.All(selected, node => Assert.Equal(root, node.Parent));
    }

    [Fact]
    public void Select_MinimumOneItem_AlwaysSelected()
    {
        var root = CreateTreeNode("root", size: 1000, isDir: true);
        var child = CreateTreeNode("child", size: 1024, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        root.Children.Add(child);

        var scanOptions = new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 }
        };
        var scorerOptions = new ScorerOptions { TopPercentage = 0.01 }; // 1% would be 0, but min is 1

        var selected = _selectionService.Select(root, scanOptions, scorerOptions);

        Assert.Single(selected);
    }

    private TreeNode CreateTestTree()
    {
        var root = CreateTreeNode("root", size: 10000, isDir: true);
        var child1 = CreateTreeNode("child1", size: 5000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-6));
        var child2 = CreateTreeNode("child2", size: 3000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-3));
        var child3 = CreateTreeNode("child3", size: 2000, parent: root, lastModified: DateTime.UtcNow.AddMonths(-1));
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);
        
        return root;
    }

    private ScanOptions CreateScanOptionsWithFilters()
    {
        return new ScanOptions
        {
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = true, SizeThreshold = 1 },
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true, 
                ModifiedBefore = DateTime.UtcNow.AddMonths(-1)
            }
        };
    }

    private TreeNode CreateTreeNode(string name, long size, TreeNode? parent = null, 
        DateTime? lastModified = null, bool isDir = false)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = parent != null ? $"{parent.FullPath}/{name}" : $"/{name}",
            Size = size,
            Parent = parent,
            LastModified = lastModified,
            IsDirectory = isDir,
            Depth = parent?.Depth + 1 ?? 0
        };
    }
}
