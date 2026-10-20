using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Tests.Cleanup;

public class AssessmentCombinerTests
{
    private static readonly SmartCleanupOptions Options = new();

    private static JunkAssessment Combine(params Signal[] signals)
    {
        var node = new NodeSnapshot { Path = "/x/file.dat", Name = "file.dat", IsDirectory = false, Size = 100, Depth = 1 };
        var ctx = new AssessmentContext(node, new SystemSnapshot(new SystemSnapshotData()), Options, new MarkerCache(), null, false);
        return new AssessmentCombiner(Options).Combine(ctx, signals);
    }

    private static Signal S(EvidenceGroup g, double llr, double risk = 0, bool hard = false, ReasonCode code = ReasonCode.LongUntouched) =>
        new(code, g, llr, risk, "test", hard);

    [Fact]
    public void NoEvidence_FallsBackToThePrior_AndIsKept()
    {
        var a = Combine();

        Assert.Equal(Options.PriorJunk, a.JunkScore, 3);
        Assert.Equal(CleanupTier.Keep, a.Tier);
    }

    [Fact]
    public void AgeAlone_CanNeverMakeSomethingJunk()
    {
        // The strongest staleness evidence there is (> 10 years untouched)
        var a = Combine(S(EvidenceGroup.Staleness, 1.9));

        Assert.True(a.JunkScore < 0.5);
        Assert.Equal(CleanupTier.Keep, a.Tier);
        Assert.True(a.Confidence < 0.4);
    }

    [Fact]
    public void CorrelatedSignalsInOneGroup_AreNotDoubleCounted()
    {
        var one = Combine(S(EvidenceGroup.Location, 4.0));
        var two = Combine(S(EvidenceGroup.Location, 4.0), S(EvidenceGroup.Location, 3.5), S(EvidenceGroup.Location, 2.0));

        Assert.Equal(one.JunkScore, two.JunkScore, 6);
        Assert.Equal(one.Confidence, two.Confidence, 6);
    }

    [Fact]
    public void IndependentGroups_Add_AndRaiseConfidence()
    {
        var one = Combine(S(EvidenceGroup.Location, 4.0));
        var two = Combine(S(EvidenceGroup.Location, 4.0), S(EvidenceGroup.Staleness, 1.5));

        Assert.True(two.JunkScore > one.JunkScore);
        Assert.True(two.Confidence > one.Confidence);
    }

    [Fact]
    public void SingleFamilyOfEvidence_CanNeverBeSafe_EvenWhenOverwhelming()
    {
        var a = Combine(S(EvidenceGroup.Location, 6.0, 0.01));

        Assert.True(a.JunkScore > 0.95);
        Assert.Equal(CleanupTier.Likely, a.Tier);
    }

    [Fact]
    public void OneFamilyCannotReachSafe_EvenWhenItsConfidenceWouldExceedTheThreshold()
    {
        var a = Combine(S(EvidenceGroup.Location, 100.0, 0.01));

        Assert.True(a.Confidence >= Options.Safe.MinConfidence);
        Assert.Equal(CleanupTier.Likely, a.Tier);
    }

    [Fact]
    public void TwoFamilies_WithLowRisk_ReachSafe()
    {
        var a = Combine(S(EvidenceGroup.Location, 4.6, 0.03), S(EvidenceGroup.Staleness, 1.5));

        Assert.Equal(CleanupTier.Safe, a.Tier);
    }

    [Fact]
    public void ConflictingEvidence_LowersConfidence_AndRisk_BlocksTiers()
    {
        var agree = Combine(S(EvidenceGroup.Location, 4.0), S(EvidenceGroup.Staleness, 1.5));
        var conflict = Combine(S(EvidenceGroup.Location, 4.0), S(EvidenceGroup.Staleness, 1.5), S(EvidenceGroup.Ownership, -3.0, 0.6));

        Assert.True(conflict.Confidence < agree.Confidence);
        Assert.True(conflict.JunkScore < agree.JunkScore);
        Assert.True(conflict.Tier < agree.Tier);
    }

    [Fact]
    public void Risk_IsANoisyOr_OverFamilies_ButOnlyTheMaximumWithinOne()
    {
        var a = Combine(S(EvidenceGroup.Content, 0, 0.5), S(EvidenceGroup.Ownership, 0, 0.5));
        var b = Combine(S(EvidenceGroup.Content, 0, 0.5), S(EvidenceGroup.Content, 0, 0.4));

        Assert.Equal(0.75, a.DeletionRisk, 6);
        Assert.Equal(0.5, b.DeletionRisk, 6);
    }

    [Fact]
    public void HardKeep_ForcesHighRisk_LowJunk_AndKeep_NoMatterTheOtherEvidence()
    {
        var a = Combine(S(EvidenceGroup.Location, 6.0), S(EvidenceGroup.Staleness, 1.9), S(EvidenceGroup.Content, 3.0),
            S(EvidenceGroup.Ownership, -1.0, 0.1, hard: true, code: ReasonCode.RunningProcess));

        Assert.True(a.HasHardKeep);
        Assert.True(a.DeletionRisk >= 0.95);
        Assert.True(a.JunkScore <= 0.10);
        Assert.Equal(CleanupTier.Keep, a.Tier);
    }

    [Fact]
    public void SystemProtected_IsProtected_WithZeroJunkAndFullRisk()
    {
        var a = Combine(S(EvidenceGroup.Location, 6.0),
            S(EvidenceGroup.Ownership, -8, 1, hard: true, code: ReasonCode.SystemProtected));

        Assert.Equal(CleanupTier.Protected, a.Tier);
        Assert.Equal(0.0, a.JunkScore);
        Assert.Equal(1.0, a.DeletionRisk);
    }

    [Fact]
    public void ReasonsAreOrderedByImportance_AndExplainMentionsThem()
    {
        var a = Combine(S(EvidenceGroup.Staleness, 0.7), S(EvidenceGroup.Location, 4.0, code: ReasonCode.KnownCacheLocation));

        Assert.Equal(ReasonCode.KnownCacheLocation, a.Reasons[0].Code);
        Assert.Contains("Known cache location", a.Explain());
    }

    [Fact]
    public void StaleCurve_IsMonotonic_AndBounded()
    {
        double previous = double.NegativeInfinity;
        foreach (var days in new[] { 0, 1, 7, 14, 30, 60, 120, 180, 365, 500, 730, 1460, 3650, 20000 })
        {
            var v = Declutterer.Domain.Services.Cleanup.Rules.StalenessRule.Interpolate(days);
            Assert.True(v >= previous, $"curve decreases at {days} days");
            Assert.InRange(v, -2.6, 1.9);
            previous = v;
        }
    }
}
