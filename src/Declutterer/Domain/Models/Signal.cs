namespace Declutterer.Domain.Models;

/// <summary>
/// Evidence is grouped into independent families. Inside one family only the strongest piece of
/// evidence per direction counts, so two correlated signals (e.g. "is in a Cache folder" and
/// "is named Cache") can never be double counted. Across families evidence is additive.
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
}

/// <summary>
/// One piece of evidence produced by a rule.
/// </summary>
/// <param name="Code">The reason code.</param>
/// <param name="Group">Evidence family, see <see cref="EvidenceGroup"/>.</param>
/// <param name="JunkLlr">
/// Log-likelihood ratio contribution to "this is junk". Positive = disposable evidence,
/// negative = evidence of use/value. A value of +4 multiplies the odds by roughly 55.
/// </param>
/// <param name="Risk">Probability (0..1) that deleting would cause harm because of this signal alone.</param>
/// <param name="Detail">Human readable specifics (what exactly matched).</param>
/// <param name="HardKeep">Veto: the item must never be auto-selected regardless of any other evidence.</param>
public readonly record struct Signal(
    ReasonCode Code,
    EvidenceGroup Group,
    double JunkLlr,
    double Risk,
    string Detail,
    bool HardKeep = false)
{
    public bool IsDisposableEvidence => JunkLlr > 0;
}
