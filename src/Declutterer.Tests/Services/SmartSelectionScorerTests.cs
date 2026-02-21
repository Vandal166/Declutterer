using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Selection;

namespace Declutterer.Tests.Services;

public class SmartSelectionScorerTests
{
    private readonly SmartSelectionScorer _scorer;

    public SmartSelectionScorerTests()
    {
        _scorer = new SmartSelectionScorer();
    }

    [Fact]
    public void ComputeScores_SingleNode_ReturnsOneScore()
    {
        var root = CreateTreeNode("root", size: 1000);
        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        Assert.Equal(root, scores[0].Node);
    }

    [Fact]
    public void ComputeScores_MultipleNodes_ReturnsScoreForEach()
    {
        var root = CreateTreeNode("root", size: 5000);
        var child1 = CreateTreeNode("child1", size: 1000);
        var child2 = CreateTreeNode("child2", size: 2000);
        var child3 = CreateTreeNode("child3", size: 3000);
        
        root.Children.Add(child1);
        root.Children.Add(child2);
        root.Children.Add(child3);

        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Equal(4, scores.Count); // root + 3 children
    }

    [Fact]
    public void ComputeScores_WithAgeFilterDisabled_ReturnsNeutralAgeScore()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        Assert.Equal(0.5, scores[0].AgeScore); // Neutral score
    }

    [Fact]
    public void ComputeScores_WithSizeFilterDisabled_ReturnsNeutralSizeScore()
    {
        var root = CreateTreeNode("root", size: 1000);
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        Assert.Equal(0.5, scores[0].SizeScore); // Neutral score
    }

    [Fact]
    public void ComputeScores_OlderFiles_HigherAgeScore()
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-3);
        var root = CreateTreeNode("root", size: 1000);
        var oldFile = CreateTreeNode("old", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        var newFile = CreateTreeNode("new", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-1));
        
        root.Children.Add(oldFile);
        root.Children.Add(newFile);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = cutoffDate
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        var oldScore = scores.First(s => s.Node == oldFile);
        var newScore = scores.First(s => s.Node == newFile);

        Assert.True(oldScore.AgeScore > newScore.AgeScore, 
            "Older file should have higher age score");
    }

    [Fact]
    public void ComputeScores_LargerFiles_HigherSizeScore()
    {
        var thresholdMB = 100; // 100 MB threshold
        var thresholdBytes = thresholdMB * 1024L * 1024;
        
        var root = CreateTreeNode("root", size: 5000L * 1024 * 1024);
        var smallFile = CreateTreeNode("small", size: thresholdBytes + 50L * 1024 * 1024); // Just above threshold
        var mediumFile = CreateTreeNode("medium", size: thresholdBytes + 500L * 1024 * 1024); // Medium above threshold
        var largeFile = CreateTreeNode("large", size: thresholdBytes + 1000L * 1024 * 1024); // Far above threshold
        
        root.Children.Add(smallFile);
        root.Children.Add(mediumFile);
        root.Children.Add(largeFile);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = thresholdMB
            }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        var smallScore = scores.First(s => s.Node == smallFile);
        var mediumScore = scores.First(s => s.Node == mediumFile);
        var largeScore = scores.First(s => s.Node == largeFile);

        Assert.True(largeScore.SizeScore > mediumScore.SizeScore, 
            "Larger file should have higher size score");
        Assert.True(mediumScore.SizeScore > smallScore.SizeScore, 
            "Medium file should have higher size score than small");
    }

    [Fact]
    public void ComputeScores_FileBelowSizeThreshold_ZeroSizeScore()
    {
        var thresholdMB = 10;
        var thresholdBytes = thresholdMB * 1024 * 1024;
        
        var root = CreateTreeNode("root", size: thresholdBytes + 1000);
        var smallFile = CreateTreeNode("small", size: thresholdBytes - 1000); // Below threshold
        
        root.Children.Add(smallFile);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = thresholdMB
            }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        var smallScore = scores.First(s => s.Node == smallFile);
        Assert.Equal(0.0, smallScore.SizeScore);
    }

    [Fact]
    public void ComputeScores_CombinedScore_WeightedAverage()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 1
            }
        };
        
        var scorerOptions = new ScorerOptions
        {
            WeightAge = 0.6,
            WeightSize = 0.4
        };

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        var score = scores[0];
        var expectedCombined = (score.AgeScore * 0.6 + score.SizeScore * 0.4) / 1.0;
        
        Assert.Equal(expectedCombined, score.CombinedScore, precision: 5);
    }

    [Fact]
    public void ComputeScores_EqualWeights_EqualContribution()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 1
            }
        };
        
        var scorerOptions = new ScorerOptions
        {
            WeightAge = 0.5,
            WeightSize = 0.5
        };

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        var score = scores[0];
        var expectedCombined = (score.AgeScore + score.SizeScore) / 2.0;
        
        Assert.Equal(expectedCombined, score.CombinedScore, precision: 5);
    }

    [Fact]
    public void ComputeScores_ZeroWeights_NormalizesToAvoidDivisionByZero()
    {
        var root = CreateTreeNode("root", size: 1000);
        
        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = new ScorerOptions
        {
            WeightAge = 0,
            WeightSize = 0
        };

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        // Should not throw and should handle gracefully
        Assert.InRange(scores[0].CombinedScore, 0.0, 1.0);
    }

    [Fact]
    public void ComputeScores_NestedNodes_ScoresAllLevels()
    {
        var root = CreateTreeNode("root", size: 1000);
        var child1 = CreateTreeNode("child1", size: 500);
        var grandchild1 = CreateTreeNode("grandchild1", size: 200);
        var grandchild2 = CreateTreeNode("grandchild2", size: 300);
        
        root.Children.Add(child1);
        child1.Children.Add(grandchild1);
        child1.Children.Add(grandchild2);

        var scanOptions = CreateDefaultScanOptions();
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Equal(4, scores.Count);
        Assert.Contains(scores, s => s.Node == root);
        Assert.Contains(scores, s => s.Node == child1);
        Assert.Contains(scores, s => s.Node == grandchild1);
        Assert.Contains(scores, s => s.Node == grandchild2);
    }

    [Fact]
    public void ComputeScores_AllScores_WithinValidRange()
    {
        var root = CreateTreeNode("root", size: 5000, lastModified: DateTime.UtcNow.AddMonths(-6));
        var child1 = CreateTreeNode("child1", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-12));
        var child2 = CreateTreeNode("child2", size: 3000, lastModified: DateTime.UtcNow.AddMonths(-3));
        
        root.Children.Add(child1);
        root.Children.Add(child2);

        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter 
            { 
                UseSizeFilter = true,
                SizeThreshold = 1
            }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        foreach (var score in scores)
        {
            Assert.InRange(score.AgeScore, 0.0, 1.0);
            Assert.InRange(score.SizeScore, 0.0, 1.0);
            Assert.InRange(score.CombinedScore, 0.0, 1.0);
        }
    }

    [Fact]
    public void ComputeScores_NodeWithoutLastModified_NeutralAgeScore()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: null);
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = DateTime.UtcNow.AddMonths(-3)
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        Assert.Equal(0.5, scores[0].AgeScore);
    }

    [Fact]
    public void ComputeScores_UsingMonthsModifiedValue_CalculatesCutoffDate()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = null, // No explicit cutoff
                MonthsModifiedValue = 3 // Use months value instead
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        // File is 6 months old, cutoff is 3 months, so it should have a positive age score
        Assert.True(scores[0].AgeScore > 0.5);
    }

    [Fact]
    public void ComputeScores_NoModifiedBeforeAndNoMonthsValue_NeutralAgeScore()
    {
        var root = CreateTreeNode("root", size: 1000, lastModified: DateTime.UtcNow.AddMonths(-6));
        
        var scanOptions = new ScanOptions
        {
            AgeFilter = new AgeFilter 
            { 
                UseModifiedDate = true,
                ModifiedBefore = null,
                MonthsModifiedValue = 0
            },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
        var scorerOptions = CreateDefaultScorerOptions();

        var scores = _scorer.ComputeScores(root, scanOptions, scorerOptions);

        Assert.Single(scores);
        Assert.Equal(0.5, scores[0].AgeScore);
    }

    // Helper methods
    private TreeNode CreateTreeNode(string name, long size, DateTime? lastModified = null)
    {
        return new TreeNode
        {
            Name = name,
            FullPath = $"/test/{name}",
            Size = size,
            LastModified = lastModified,
            IsDirectory = false
        };
    }

    private ScanOptions CreateDefaultScanOptions()
    {
        return new ScanOptions
        {
            AgeFilter = new AgeFilter { UseModifiedDate = false },
            FileSizeFilter = new EntrySizeFilter { UseSizeFilter = false }
        };
    }

    private ScorerOptions CreateDefaultScorerOptions()
    {
        return new ScorerOptions
        {
            WeightAge = 0.5,
            WeightSize = 0.5,
            TopPercentage = 0.4
        };
    }
}
