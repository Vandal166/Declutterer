using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Cleanup.Rules;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Tests.Observation;

/// <summary>How observation evidence is allowed to move an assessment (combiner level, hand-built signals).</summary>
public class ObservationEvidenceTests
{
    private static JunkAssessment Combine(SmartCleanupOptions? options, params Signal[] signals)
    {
        options ??= new SmartCleanupOptions();
        return new AssessmentCombiner(options).Combine(Context(options), signals);
    }

    // A file node: a directory without disk facts is capped at 0.6 coverage on purpose (Safe unreachable), which is not what is tested here.
    private static AssessmentContext Context(SmartCleanupOptions options)
    {
        var node = new NodeSnapshot { Path = "/x/file.dat", Name = "file.dat", IsDirectory = false, Size = 100, Depth = 1 };
        return new AssessmentContext(node, new SystemSnapshot(new SystemSnapshotData()), options, new MarkerCache(), null, false);
    }

    private static Signal S(EvidenceGroup g, double llr, double risk = 0, bool hard = false, ReasonCode code = ReasonCode.LongUntouched) =>
        new(code, g, llr, risk, "test", hard);

    private static Signal Quiet(double llr = 1.4, double relief = 0.5) =>
        new(ReasonCode.ObservedInactive, EvidenceGroup.Observation, llr, 0, "quiet", RiskRelief: relief);

    private static Signal Orphan(double risk = 0.45) => S(EvidenceGroup.Ownership, 2.0, risk, code: ReasonCode.OrphanedAppData);

    [Fact]
    public void QuietObservationAlone_StaysKeep()
    {
        var a = Combine(null, Quiet());

        Assert.Equal(CleanupTier.Keep, a.Tier);
        Assert.True(a.JunkScore < 0.5);
    }

    [Fact]
    public void AStaleKeepItemPlusQuiet_CanReachReview_AndNeverLikely()
    {
        var a = Combine(null, S(EvidenceGroup.Staleness, 1.9), Quiet());

        Assert.Equal(CleanupTier.Review, a.Tier);
        Assert.True(a.JunkScore > Combine(null, S(EvidenceGroup.Staleness, 1.9)).JunkScore);
    }

    [Fact]
    public void QuietEvidenceDoesNotPromoteACloudSyncedKeep()
    {
        var a = Combine(null, S(EvidenceGroup.Staleness, 1.9),
            S(EvidenceGroup.Location, 0, 0.30, code: ReasonCode.SyncedFolder), Quiet());

        Assert.Equal(CleanupTier.Keep, a.Tier);
        Assert.Contains(a.Reasons, r => r.Code == ReasonCode.ObservedInactive && r.JunkLlr == 0 && r.Detail.Contains("not applied"));
    }

    [Fact]
    public void QuietNeverMakesAnAlreadySafeItemAnyDifferent()
    {
        var plain = Combine(null, S(EvidenceGroup.Location, 4.6, 0.03), S(EvidenceGroup.Staleness, 1.5));
        var withQuiet = Combine(null, S(EvidenceGroup.Location, 4.6, 0.03), S(EvidenceGroup.Staleness, 1.5), Quiet());

        Assert.Equal(CleanupTier.Safe, plain.Tier);
        Assert.Equal(CleanupTier.Safe, withQuiet.Tier);
        Assert.Equal(plain.JunkScore, withQuiet.JunkScore, 6);
        Assert.Equal(plain.Confidence, withQuiet.Confidence, 6);
    }

    [Fact]
    public void AReviewItemWithModerateRisk_BecomesLikely_NotSafe_WhenRelievedByQuiet()
    {
        // D1: orphaned Roaming app data. Risk 0.45 is relieved to 0.225: Likely allows 0.35, Safe only 0.15.
        var baseline = Combine(null, Orphan(), S(EvidenceGroup.Staleness, 1.3));
        var withQuiet = Combine(null, Orphan(), S(EvidenceGroup.Staleness, 1.3), Quiet());

        Assert.Equal(CleanupTier.Review, baseline.Tier);
        Assert.Equal(CleanupTier.Likely, withQuiet.Tier);
        Assert.Equal(0.45 * 0.5, withQuiet.DeletionRisk, 6);
        Assert.True(withQuiet.JunkScore > baseline.JunkScore);
    }

    [Fact]
    public void ALikelySingleFamilyItemWithLowRisk_CanBeLiftedToSafe_ByQuietAsTheSecondFamily()
    {
        var baseline = Combine(null, S(EvidenceGroup.Location, 4.0, 0.03, code: ReasonCode.KnownCacheLocation));
        var withQuiet = Combine(null, S(EvidenceGroup.Location, 4.0, 0.03, code: ReasonCode.KnownCacheLocation), Quiet());

        Assert.Equal(CleanupTier.Likely, baseline.Tier);
        Assert.Equal(CleanupTier.Safe, withQuiet.Tier);
    }

    [Fact]
    public void QuietLiftsByOneStepAtMost()
    {
        // thresholds under which Safe would be reachable straight from Review once a second family is present
        var options = new SmartCleanupOptions { Likely = new TierThresholds(0.999, 0.001, 0.999), Safe = new TierThresholds(0.6, 0.9, 0.0) };
        var location = S(EvidenceGroup.Location, 2.6, 0.03, code: ReasonCode.KnownCacheLocation);

        var baseline = Combine(options, location);
        var withQuiet = Combine(options, location, Quiet());

        Assert.Equal(CleanupTier.Review, baseline.Tier);
        Assert.Equal(CleanupTier.Likely, withQuiet.Tier); // the numbers say Safe, the cap says one step
    }

    [Fact]
    public void AHardKeep_StillWins_NoMatterHowQuiet()
    {
        var a = Combine(null, Orphan(), S(EvidenceGroup.Staleness, 1.3), Quiet(),
            S(EvidenceGroup.Ownership, -4.0, 0.9, hard: true, code: ReasonCode.RunningProcess));

        Assert.Equal(CleanupTier.Keep, a.Tier);
        Assert.True(a.HasHardKeep);
        Assert.True(a.DeletionRisk >= 0.95);
    }

    [Theory]
    [InlineData(ReasonCode.ContainsUserDocuments)]
    [InlineData(ReasonCode.ContainsCredentials)]
    [InlineData(ReasonCode.BrowserProfile)]
    [InlineData(ReasonCode.UserSettingsOrSaves)]
    public void QuietIsIgnored_NextToUserDataOrSecrets(ReasonCode blocker)
    {
        var without = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3), S(EvidenceGroup.Content, -0.3, 0.1, code: blocker));
        var with = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3), S(EvidenceGroup.Content, -0.3, 0.1, code: blocker), Quiet());

        Assert.Equal(without.JunkScore, with.JunkScore, 6);
        Assert.Equal(without.DeletionRisk, with.DeletionRisk, 6);
        Assert.Equal(without.Tier, with.Tier);
    }

    [Fact]
    public void CloudSyncKeepsItsRisk_AndGetsNoRelief()
    {
        var a = Combine(null, Orphan(0.3), S(EvidenceGroup.Staleness, 1.5), S(EvidenceGroup.Location, 0, 0.30, code: ReasonCode.SyncedFolder), Quiet());

        Assert.True(a.DeletionRisk >= 0.30 - 1e-9);
        Assert.NotEqual(CleanupTier.Safe, a.Tier);
    }

    [Fact]
    public void ReliefOnlyTouchesTheStaleRisks_NotAnyOtherCode()
    {
        // a risk of an unrelated kind (here: content) must be left exactly as it is
        var other = Combine(null, Orphan(0.0), S(EvidenceGroup.Staleness, 1.3), S(EvidenceGroup.Content, 0, 0.5, code: ReasonCode.OnlyDisposableContent), Quiet());

        Assert.True(other.DeletionRisk >= 0.5 - 1e-9);
    }

    [Fact]
    public void ReliefIsCappedByTheOptions()
    {
        var options = new SmartCleanupOptions();
        var ctx = Context(options);
        var combiner = new AssessmentCombiner(options, new ObservationOptions { MaxRiskRelief = 0.2 });

        var a = combiner.Combine(ctx, new[] { Orphan(0.5), S(EvidenceGroup.Staleness, 1.3), Quiet(relief: 0.9) });

        Assert.Equal(0.5 * 0.8, a.DeletionRisk, 6);
    }

    [Fact]
    public void ObservedUse_DemotesAnItemBelowReview()
    {
        var baseline = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3));
        var used = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3),
            new Signal(ReasonCode.ObservedActive, EvidenceGroup.Observation, -3.0, 0.5, "observed in use"));

        Assert.Equal(CleanupTier.Review, baseline.Tier);
        Assert.Equal(CleanupTier.Keep, used.Tier);
        Assert.True(used.DeletionRisk >= 0.5 - 1e-9);
    }

    [Fact]
    public void AnInconclusiveObservation_AddsOnlyASmallRisk_AndNoJunkEvidence()
    {
        var a = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3));
        var b = Combine(null, Orphan(0.2), S(EvidenceGroup.Staleness, 1.3),
            new Signal(ReasonCode.ObservationInconclusive, EvidenceGroup.Observation, 0, 0.05, "x"));

        Assert.Equal(a.JunkScore, b.JunkScore, 6);
        Assert.True(b.DeletionRisk >= a.DeletionRisk);
        Assert.True(b.DeletionRisk - a.DeletionRisk < 0.06);
    }

    [Fact]
    public void ObservationReasons_ReadWellInTheTooltip()
    {
        var a = Combine(null, Orphan(), S(EvidenceGroup.Staleness, 1.3),
            new Signal(ReasonCode.ObservedInactive, EvidenceGroup.Observation, 1.4, 0, "untouched for 28 covered days (of 28); valid until 2026-12-01", RiskRelief: 0.5),
            new Signal(ReasonCode.ObservationInconclusive, EvidenceGroup.Observation, 0, 0.05, "1 check-in(s) in a row could not be compared reliably"));

        var text = a.Explain();

        Assert.Contains("Observed unused: untouched for 28 covered days", text);
        Assert.Contains("Observation inconclusive", text); // visible although it carries no score
    }

    [Fact]
    public void TheNewReasonCodes_HaveTitles()
    {
        foreach (var code in new[] { ReasonCode.ObservedActive, ReasonCode.ObservedInactive, ReasonCode.ObservationInconclusive, ReasonCode.ObservedMoved, ReasonCode.PinnedByUser })
            Assert.NotEqual(code.ToString(), code.ToTitle());
    }
}
