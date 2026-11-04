using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>The outcome of re-running the full engine on one watched item.</summary>
public sealed record ReassessmentResult(CleanupTier Tier, double JunkScore, double DeletionRisk, string Reasons, JunkAssessment Assessment);
