namespace Declutterer.Domain.Models;

/// <summary>
/// Evidence is grouped into configured families. Inside one family only the strongest piece of
/// evidence per direction counts, so two correlated signals (e.g. "is in a Cache folder" and
/// "is named Cache") can never be double counted. Contributions across families are additive; family
/// membership does not prove statistical independence.
/// </summary>
public enum EvidenceGroup
{
    /// <summary>What kind of place this is (cache dir, temp dir, build output ...).</summary>
    Location,
    /// <summary>File name / extension patterns (.tmp, "(1)" duplicates ...).</summary>
    Pattern,
    /// <summary>What the item actually contains.</summary>
    Content,
    /// <summary>When the item was last touched.</summary>
    Staleness,
    /// <summary>Who owns / uses it (installed app, running process, recent files ...).</summary>
    Ownership,
    /// <summary>Project structure (git, solution files ...).</summary>
    Structure,
    /// <summary>How complete the inspection was.</summary>
    Coverage,
    /// <summary>What watching the item over time showed (in use / unused / inconclusive).</summary>
    Observation,
}

/// <summary>
/// One piece of evidence produced by a rule.
/// </summary>
/// <param name="Code">The reason code.</param>
/// <param name="Group">Configured evidence family, see <see cref="EvidenceGroup"/>.</param>
/// <param name="JunkLlr">
/// Heuristic log-odds contribution to the junk score. Positive = disposable evidence,
/// negative = evidence of use/value. It is not an empirically calibrated likelihood ratio.
/// </param>
/// <param name="Risk">Heuristic 0..1 estimate of deletion harm contributed by this signal.</param>
/// <param name="Detail">Human readable specifics (what exactly matched).</param>
/// <param name="HardKeep">Veto: the item must never be auto-selected regardless of any other evidence.</param>
/// <param name="RiskRelief">
/// Only set by completed quiet observation: the share (0..1) of the "this is merely stale" risk of other signals it may remove.
/// The combiner applies it solely to OrphanedAppData, ProjectRoot, RecentlyOpened and RecentlyModified, caps it, and never applies it
/// next to a veto, cloud sync or user documents.
/// </param>
public readonly record struct Signal(
    ReasonCode Code,
    EvidenceGroup Group,
    double JunkLlr,
    double Risk,
    string Detail,
    bool HardKeep = false,
    double RiskRelief = 0)
{
    public bool IsDisposableEvidence => JunkLlr > 0;
}
