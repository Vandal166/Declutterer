using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// Places where deleting something silently destroys secrets, identity or irreplaceable state:
/// SSH/GPG/cloud credentials, the DPAPI master keys, UWP package data, browser profiles, saved games ...
/// Hard entries veto automatic selection no matter what else the evidence says.
/// </summary>
public sealed class SensitiveLocationRule : IAssessmentRule
{
    // anywhere in the path
    private static readonly string[][] HardSequences =
    {
        new[] { ".ssh" }, new[] { ".gnupg" }, new[] { ".aws" }, new[] { ".azure" }, new[] { ".kube" },
        new[] { "$recycle.bin" }, new[] { "system volume information" },
    };

    // relative to an application-data root
    private static readonly string[][] HardAppDataSequences =
    {
        new[] { "microsoft", "protect" }, new[] { "microsoft", "crypto" }, new[] { "microsoft", "credentials" },
        new[] { "microsoft", "vault" }, new[] { "microsoft", "systemcertificates" },
        new[] { "packages" }, // UWP / MSIX per-package state, managed by Windows
    };

    private static readonly string[][] SaveGameSequences =
    {
        new[] { "saved games" }, new[] { "my games" },
    };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        var segs = ctx.Segments;

        foreach (var seq in HardSequences)
        {
            if (PathKey.ContainsSequence(segs, seq))
            {
                sink.Add(ReasonCode.SensitiveLocation, EvidenceGroup.Ownership, -5.0, 0.98,
                    $"'{string.Join("\\", seq)}' holds credentials or OS-managed state", hardKeep: true);
                return;
            }
        }

        var appRel = ctx.AppDataRelative;
        if (appRel is not null)
        {
            foreach (var seq in HardAppDataSequences)
            {
                if (PathKey.ContainsSequence(appRel, seq))
                {
                    sink.Add(ReasonCode.SensitiveLocation, EvidenceGroup.Ownership, -5.0, 0.98,
                        $"'{string.Join("\\", seq)}' holds credentials or OS-managed state", hardKeep: true);
                    return;
                }
            }
        }

        foreach (var seq in SaveGameSequences)
        {
            if (PathKey.ContainsSequence(segs, seq))
            {
                sink.Add(ReasonCode.UserSettingsOrSaves, EvidenceGroup.Ownership, -2.5, 0.85, "saved games / game settings");
                return;
            }
        }

        // Browser profiles: the profile directory itself (bookmarks, passwords, extensions) - its cache folders are
        // handled by KnownLocationRule and are not affected because only the profile root is matched here.
        if (IsBrowserProfileRoot(segs))
        {
            sink.Add(ReasonCode.BrowserProfile, EvidenceGroup.Ownership, -3.0, 0.9, "browser profile (bookmarks, passwords, extensions)");
            return;
        }

        if (sink.InDisposableContext)
            return; // soft OS-managed checks below must not hide explicit cache locations

        // The Roaming/Local "Microsoft" folder and everything below "Microsoft\Windows" is OS managed state.
        if (appRel is { Length: >= 1 } rel && rel[0].Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
            (rel.Length == 1 || rel[1].Equals("windows", StringComparison.OrdinalIgnoreCase)))
        {
            sink.Add(ReasonCode.SensitiveLocation, EvidenceGroup.Ownership, -2.0, 0.85, "Windows-managed application data");
        }
    }

    private static bool IsBrowserProfileRoot(string[] segs)
    {
        // ...\Mozilla\Firefox\Profiles[\<profile>]
        var idx = IndexOf(segs, new[] { "mozilla", "firefox", "profiles" });
        if (idx >= 0 && segs.Length - (idx + 3) <= 1)
            return true;

        // ...\User Data[\Default | \Profile N]   (Chrome, Edge, Brave, Vivaldi ...)
        for (int i = 0; i < segs.Length; i++)
        {
            if (!segs[i].Equals("user data", StringComparison.OrdinalIgnoreCase))
                continue;
            int tail = segs.Length - (i + 1);
            if (tail == 0)
                return true;
            if (tail == 1 && (segs[i + 1].Equals("default", StringComparison.OrdinalIgnoreCase) ||
                              segs[i + 1].StartsWith("profile ", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static int IndexOf(string[] segs, string[] pattern)
    {
        for (int i = 0; i <= segs.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && segs[i + j].Equals(pattern[j], StringComparison.OrdinalIgnoreCase)) j++;
            if (j == pattern.Length) return i;
        }
        return -1;
    }
}
