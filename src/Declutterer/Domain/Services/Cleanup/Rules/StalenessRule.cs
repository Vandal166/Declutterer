using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// Time evidence. Deliberately weak and bounded: age on its own can never make something "safe", it only
/// corroborates other evidence, while recent activity is strong evidence that something is alive.
/// "Last touched" is the newest of write time, creation time (a copied file keeps its old write time!),
/// the newest activity anywhere inside a directory and - only when the OS maintains it - last access time.
/// </summary>
public sealed class StalenessRule : IAssessmentRule
{
    // (age in days, log-likelihood ratio). Linear interpolation in between; flat beyond the ends.
    private static readonly (double Days, double Llr)[] Curve =
    {
        (0, -2.6), (14, -2.3), (60, -1.3), (180, -0.3), (365, 0.7), (730, 1.3), (1460, 1.7), (3650, 1.9),
    };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var last = ctx.Node.LastWriteUtc;
        var f = ctx.Facts;

        if (f is not null && f.NewestActivityUtc > DateTime.MinValue && (last is null || f.NewestActivityUtc > last))
            last = f.NewestActivityUtc;

        // Check creation time only for files already old enough that a copied timestamp could affect staleness.
        if (ctx.Options.UseCreationTime && !ctx.IsDirectory && last is not null && (ctx.NowUtc - last.Value).TotalDays >= 180)
        {
            // A copied file may have old write time but recent creation time, so use its newest timestamp.
            var created = ctx.CreationUtc;
            if (created is not null && created > last)
                last = created;
        }

        bool accessUsed = false;
        if (ctx.AccessTimeReliable)
        {
            DateTime? access = ctx.IsDirectory
                ? (f is not null && f.NewestAccessUtc > DateTime.MinValue ? f.NewestAccessUtc : null)
                : ctx.Node.LastAccessUtc;
            if (access is not null && (last is null || access > last))
            {
                last = access;
                accessUsed = true;
            }
        }

        if (last is null || last.Value.Year < 1700)
            return;

        var ageDays = Math.Max(0, (ctx.NowUtc - last.Value).TotalDays);
        var llr = Interpolate(ageDays);
        var risk = ageDays < 30 ? 0.55 : ageDays < 90 ? 0.25 : 0.0;

        if (llr < 0 && sink.InDisposableContext)
        {
            // Caches and build output are rewritten constantly; recent activity there says nothing about value.
            llr *= 0.25;
            risk *= 0.2;
        }

        var weakNote = ctx.IsDirectory && f is null ? " (directory timestamp only)" : string.Empty;
        if (llr >= 0.5)
        {
            sink.Add(ReasonCode.LongUntouched, EvidenceGroup.Staleness, llr, risk,
                $"not touched for {OwnershipRule.Ago(ageDays).Replace(" ago", string.Empty)}{weakNote}");
        }
        else if (llr <= -0.5)
        {
            sink.Add(accessUsed ? ReasonCode.RecentlyAccessed : ReasonCode.RecentlyModified, EvidenceGroup.Staleness, llr, risk,
                $"{(accessUsed ? "accessed" : "changed")} {OwnershipRule.Ago(ageDays)}{weakNote}");
        }
        // |llr| < 0.5: ambiguous age -> no signal at all (so it can not inflate confidence)
    }

    public static double Interpolate(double ageDays)
    {
        if (ageDays <= Curve[0].Days) return Curve[0].Llr;
        for (int i = 1; i < Curve.Length; i++)
        {
            if (ageDays <= Curve[i].Days)
            {
                var (d0, v0) = Curve[i - 1];
                var (d1, v1) = Curve[i];
                return v0 + (v1 - v0) * (ageDays - d0) / (d1 - d0);
            }
        }
        return Curve[^1].Llr;
    }
}
