using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// Source repositories and project folders are somebody's work. A git repository vetoes selection (unpushed
/// commits live there); a plain project folder is strongly down-weighted. Their build output is still picked up by
/// <see cref="RegenerableArtifactRule"/> on the child nodes.
/// </summary>
public sealed class ProjectRule : IAssessmentRule
{
    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        if (!ctx.IsDirectory || sink.InDisposableContext)
            return;

        var own = ctx.Markers.GetOwn(ctx.Path);
        var subtree = own | (ctx.Facts?.SubtreeMarkers ?? ProjectMarkers.None);

        if ((subtree & ProjectMarkers.Git) != 0)
        {
            sink.Add(ReasonCode.VersionControlRepo, EvidenceGroup.Structure, -3.5, 0.92,
                "contains a git repository (may hold unpushed work)", hardKeep: true);
            return;
        }

        if ((own & ProjectMarkerProbe.ProjectLike) != 0)
            sink.Add(ReasonCode.ProjectRoot, EvidenceGroup.Structure, -1.8, 0.55, "contains project files (package.json, *.csproj, ...)");
    }
}
