using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Tests.Cleanup;

public class DeepeningPlannerTests
{
    private static readonly SmartCleanupOptions Options = new();
    private const long Big = 100L * 1024 * 1024;

    private static NodeSnapshot Dir(string name, NodeSnapshot? parent, long size = Big, bool unloaded = true)
    {
        var n = new NodeSnapshot
        {
            Path = (parent?.Path ?? "/r") + "/" + name,
            Name = name,
            IsDirectory = true,
            Size = size,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Parent = parent,
            HasUnloadedChildren = unloaded,
            Source = new TreeNode { FullPath = "/x/" + name },
        };
        parent?.Children.Add(n);
        return n;
    }

    private static JunkAssessment A(CleanupTier tier, params Signal[] reasons) => new()
    {
        JunkScore = 0.2, DeletionRisk = 0.3, Confidence = 0.5, Tier = tier, Reasons = reasons,
    };

    private static Signal Hard(ReasonCode code) => new(code, EvidenceGroup.Ownership, -5, 1, "x", true);

    [Fact]
    public void BigUnloadedNonJunkFolders_AreDiveTargets_LargestFirst()
    {
        var root = Dir("root", null, unloaded: false);
        var small = Dir("small", root, 1024);
        var medium = Dir("medium", root, Big);
        var large = Dir("large", root, Big * 5);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [small] = A(CleanupTier.Keep), [medium] = A(CleanupTier.Keep), [large] = A(CleanupTier.Review),
        };

        var targets = DeepeningPlanner.SelectTargets(new[] { root }, map, Options);

        Assert.Equal(new[] { large, medium }, targets);
    }

    [Fact]
    public void JunkFolders_AlreadyLoadedFolders_AndUnassessedNodes_AreSkipped()
    {
        var root = Dir("root", null, unloaded: false);
        var junk = Dir("junk", root);
        var loaded = Dir("loaded", root, unloaded: false);
        var unassessed = Dir("unassessed", root);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [junk] = A(CleanupTier.Likely), [loaded] = A(CleanupTier.Keep),
        };

        Assert.Empty(DeepeningPlanner.SelectTargets(new[] { root }, map, Options));
    }

    [Fact]
    public void VetoesThatMakeLookingInsidePointless_AreRespected_ButProjectsAndContainersAreAllowed()
    {
        var root = Dir("root", null, unloaded: false);
        var running = Dir("running", root);
        var installed = Dir("installed", root);
        var creds = Dir("creds", root);
        var repo = Dir("repo", root);
        var container = Dir("downloads", root);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>
        {
            [running] = A(CleanupTier.Keep, Hard(ReasonCode.RunningProcess)),
            [installed] = A(CleanupTier.Keep, Hard(ReasonCode.InstalledApplication)),
            [creds] = A(CleanupTier.Keep, Hard(ReasonCode.ContainsCredentials)),
            [repo] = A(CleanupTier.Keep, Hard(ReasonCode.VersionControlRepo)),
            [container] = A(CleanupTier.Keep, Hard(ReasonCode.ContainerFolder)),
        };

        var targets = DeepeningPlanner.SelectTargets(new[] { root }, map, Options);

        Assert.Equal(2, targets.Count);
        Assert.Contains(repo, targets);
        Assert.Contains(container, targets);
    }

    [Fact]
    public void DepthAndCountLimits_ArePerRound()
    {
        var root = Dir("root", null, unloaded: false);
        var map = new Dictionary<NodeSnapshot, JunkAssessment>();
        var opts = new SmartCleanupOptions { DeepenMaxTargetsPerRound = 3, DeepenMaxDepth = 2 };
        for (int i = 0; i < 10; i++)
            map[Dir("d" + i, root, Big + i)] = A(CleanupTier.Keep);
        var deep = Dir("deep", Dir("a", root, unloaded: false), Big * 9);
        var deeper = Dir("deeper", deep, Big * 9);
        map[deep] = A(CleanupTier.Keep);
        map[deeper] = A(CleanupTier.Keep);

        var targets = DeepeningPlanner.SelectTargets(new[] { root }, map, opts);

        Assert.Equal(3, targets.Count);
        Assert.DoesNotContain(deeper, targets); // depth 3 > DeepenMaxDepth 2
    }
}
