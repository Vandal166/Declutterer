using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Tests.Cleanup;

public class SmartCleanupSelectorTests
{
    private static readonly SmartCleanupOptions Options = new();

    private static NodeSnapshot Node(string name, NodeSnapshot? parent, long size = 100)
    {
        var n = new NodeSnapshot
        {
            Path = (parent?.Path ?? "/root") + "/" + name,
            Name = name,
            IsDirectory = true,
            Size = size,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Parent = parent,
        };
        parent?.Children.Add(n);
        return n;
    }

    private static JunkAssessment A(CleanupTier tier, double conf = 0.8, bool hard = false,
        bool safetyComplete = true, string? selectionBlock = null) => new()
    {
        JunkScore = tier >= CleanupTier.Review ? 0.9 : 0.1,
        DeletionRisk = hard ? 0.97 : 0.05,
        Confidence = conf,
        Tier = tier,
        SelectionSafetyComplete = safetyComplete,
        SelectionBlockReason = selectionBlock,
        Reasons = hard
            ? new[] { new Signal(ReasonCode.RunningProcess, EvidenceGroup.Ownership, -6, 1, "x", true) }
            : Array.Empty<Signal>(),
    };

    [Theory]
    [InlineData(SmartSelectMode.Conservative, CleanupTier.Safe, 0.8, true)]
    [InlineData(SmartSelectMode.Conservative, CleanupTier.Likely, 0.8, false)]
    [InlineData(SmartSelectMode.Balanced, CleanupTier.Likely, 0.8, true)]
    [InlineData(SmartSelectMode.Balanced, CleanupTier.Review, 0.8, false)]
    [InlineData(SmartSelectMode.Aggressive, CleanupTier.Review, 0.5, true)]
    [InlineData(SmartSelectMode.Aggressive, CleanupTier.Review, 0.2, false)]
    [InlineData(SmartSelectMode.Aggressive, CleanupTier.Keep, 0.99, false)]
    [InlineData(SmartSelectMode.Aggressive, CleanupTier.Protected, 0.99, false)]
    public void Eligibility_FollowsTheMode(SmartSelectMode mode, CleanupTier tier, double confidence, bool expected)
    {
        Assert.Equal(expected, SmartCleanupSelector.IsEligible(A(tier, confidence), mode, Options));
    }

    [Fact]
    public void HardKeepIsNeverEligible_EvenIfTheTierSaysSafe()
    {
        Assert.False(SmartCleanupSelector.IsEligible(A(CleanupTier.Safe, hard: true), SmartSelectMode.Aggressive, Options));
    }

    [Theory]
    [InlineData(SmartSelectMode.Conservative)]
    [InlineData(SmartSelectMode.Balanced)]
    [InlineData(SmartSelectMode.Aggressive)]
    public void IncompleteSelectionSafetyBlocksEveryMode(SmartSelectMode mode)
    {
        Assert.False(SmartCleanupSelector.IsEligible(A(CleanupTier.Safe, safetyComplete: false), mode, Options));
    }

    [Fact]
    public void QualifyingParent_IsTakenWhole_AndItsChildrenAreSkipped()
    {
        var root = Node("root", null);
        var parent = Node("parent", root);
        var child = Node("child", parent);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [parent] = A(CleanupTier.Safe),
            [child] = A(CleanupTier.Safe),
        };

        var plan = new SmartCleanupSelector().Plan(new[] { root }, map, SmartSelectMode.Conservative, Options);

        Assert.Equal(new[] { parent }, plan.Selected);
    }

    [Fact]
    public void NonQualifyingParent_IsOpened_AndQualifyingChildrenAreTaken()
    {
        var root = Node("root", null);
        var parent = Node("parent", root);
        var good = Node("node_modules", parent, 900);
        var other = Node("src", parent);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [parent] = A(CleanupTier.Keep),
            [good] = A(CleanupTier.Safe),
            [other] = A(CleanupTier.Keep),
        };

        var plan = new SmartCleanupSelector().Plan(new[] { root }, map, SmartSelectMode.Conservative, Options);

        Assert.Equal(new[] { good }, plan.Selected);
        Assert.Equal(900, plan.TotalBytes);
        Assert.Equal(1, plan.SafeCount);
    }

    [Fact]
    public void ParentIsNotTaken_WhenAnyLoadedDescendantCarriesAVeto()
    {
        var root = Node("root", null);
        var parent = Node("parent", root);
        var mid = Node("mid", parent);
        var vetoed = Node("running", mid);
        var sibling = Node("fine", parent);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [parent] = A(CleanupTier.Safe),
            [mid] = A(CleanupTier.Keep),
            [vetoed] = A(CleanupTier.Keep, hard: true),
            [sibling] = A(CleanupTier.Safe),
        };

        var plan = new SmartCleanupSelector().Plan(new[] { root }, map, SmartSelectMode.Conservative, Options);

        Assert.DoesNotContain(parent, plan.Selected);
        Assert.Equal(new[] { sibling }, plan.Selected);
    }

    [Fact]
    public void ParentIsNotTaken_WhenLoadedDescendantHasSafetyBlock_AndSiblingRemainsEligible()
    {
        var root = Node("root", null);
        var parent = Node("parent", root);
        var protectedChild = Node("protected", parent);
        var sibling = Node("fine", parent);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [parent] = A(CleanupTier.Safe),
            [protectedChild] = A(CleanupTier.Keep, safetyComplete: false, selectionBlock: "nested sensitive path"),
            [sibling] = A(CleanupTier.Safe),
        };

        var plan = new SmartCleanupSelector().Plan(new[] { root }, map, SmartSelectMode.Conservative, Options);

        Assert.DoesNotContain(parent, plan.Selected);
        Assert.Equal(new[] { sibling }, plan.Selected);
    }

    [Fact]
    public void Roots_AreNeverSelected()
    {
        var root = Node("root", null);
        var map = new Dictionary<NodeSnapshot, JunkAssessment> { [root] = A(CleanupTier.Safe) };

        var plan = new SmartCleanupSelector().Plan(new[] { root }, map, SmartSelectMode.Aggressive, Options);

        Assert.Empty(plan.Selected);
    }

    [Fact]
    public void FromTreeNode_SnapshotsTheLoadedHierarchy_InUtc()
    {
        var root = new TreeNode { Name = "root", FullPath = "/r", IsDirectory = true, LastModified = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var child = new TreeNode { Name = "c", FullPath = "/r/c", Size = 42, LastModified = new DateTime(2023, 5, 5, 0, 0, 0, DateTimeKind.Local) };
        root.Children.Add(child);

        var snap = NodeSnapshot.FromTreeNode(root);

        Assert.True(snap.IsRoot);
        Assert.Single(snap.Children);
        Assert.Equal(1, snap.Children[0].Depth);
        Assert.Same(child, snap.Children[0].Source);
        Assert.Equal(DateTimeKind.Utc, snap.Children[0].LastWriteUtc!.Value.Kind);
        Assert.Equal(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc), snap.LastWriteUtc);
    }
}
