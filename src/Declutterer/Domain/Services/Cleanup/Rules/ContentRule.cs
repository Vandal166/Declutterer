using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// "What does it actually contain?" - the single most important protection for user data.
/// Credentials veto selection; documents / media / databases raise the deletion risk in proportion to their share.
/// Only inspected directories (stage B) and files are judged; un-inspected directories are never given the benefit of the doubt.
/// </summary>
public sealed class ContentRule : IAssessmentRule
{
    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var c = ctx.Content;
        if (c is null)
            return; // directory that was not inspected -> no content evidence (confidence reflects that)

        bool disposableContext = sink.InDisposableContext;
        bool inAppData = ctx.IsUnderAppData;

        // ── empty directory ────────────────────────────────────────────────
        if (ctx.IsDirectory && c.IsComplete && c.IsEmpty)
        {
            sink.Add(ReasonCode.EmptyDirectory, EvidenceGroup.Content, 3.2, 0.03, "contains no files");
            return;
        }

        if (disposableContext || sink.ContentIsRedundant)
            return; // node_modules is full of source files, a cache may contain anything: the location is the stronger evidence

        // ── credentials / keys: hard veto ──────────────────────────────────
        if (c.HasCredentialFile)
        {
            sink.Add(ReasonCode.ContainsCredentials, EvidenceGroup.Content, -5.0, 0.97, "contains keys, certificates or password stores", hardKeep: true);
            return;
        }

        // ── user documents / media / databases ─────────────────────────────
        double weight = c.ValuableFileWeight(inAppData);
        if (weight > 0)
        {
            double share = c.ValuableShare(inAppData);
            if (share >= 0.60)
                sink.Add(ReasonCode.ContainsUserDocuments, EvidenceGroup.Content, -2.6, 0.80, $"{Percent(share)} of the content is documents, media or databases");
            else if (share >= 0.25)
                sink.Add(ReasonCode.ContainsUserDocuments, EvidenceGroup.Content, -1.8, 0.60, $"{Percent(share)} of the content is documents, media or databases");
            else if (weight >= 0.5)
                sink.Add(ReasonCode.ContainsUserDocuments, EvidenceGroup.Content, -0.8, 0.30, "contains some documents, media or databases");
        }

        // ── executables: possibly a portable app (but in app data they are just a removed app's binaries) ──
        if (!inAppData && ctx.IsDirectory && c.ShareByCount(FileClass.Executable, FileClass.Library) >= 0.3)
            sink.Add(ReasonCode.ContainsExecutables, EvidenceGroup.Content, -0.5, 0.35, "mostly executables (portable program?)");

        // ── only disposable content ────────────────────────────────────────
        if (ctx.IsDirectory && c.FileCount > 0 && weight == 0)
        {
            double d = c.DisposableByteShare();
            if (d >= 0.90)
                sink.Add(ReasonCode.OnlyDisposableContent, EvidenceGroup.Content, 1.6, 0.05, "logs, temp files and dumps only");
            else if (d >= 0.60)
                sink.Add(ReasonCode.OnlyDisposableContent, EvidenceGroup.Content, 0.8, 0.08, "mostly logs, temp files and dumps");
        }
    }

    private static string Percent(double v) => $"{(int)System.Math.Round(v * 100)}%";
}
