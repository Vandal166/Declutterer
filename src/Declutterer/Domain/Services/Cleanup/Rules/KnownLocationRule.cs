using System;
using System.Collections.Generic;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// "What kind of place is this?" - temp folders, cache directories, crash dumps, package-manager caches.
/// Application-data patterns are matched relative to the AppData root the node lives in. Names that are merely
/// suggestive (a folder called "cache" in Documents) only count for the node's own name and score far lower than
/// anchored, well-known locations (AppData\Local\Temp, ...\Chrome\User Data\Default\Cache).
/// </summary>
public sealed class KnownLocationRule : IAssessmentRule
{
    private sealed record Loc(string[] Seq, double Llr, double Risk, ReasonCode Code, string Label);
    private sealed record NameRule(double Llr, double Risk, ReasonCode Code, string Label);

    // Relative to an application-data root (AppData\Local, AppData\Roaming ...).
    private static readonly Loc[] AppDataTable =
    {
        new(new[] { "crashdumps" }, 4.5, 0.03, ReasonCode.CrashOrDiagnosticData, "crash dumps"),
        new(new[] { "microsoft", "windows", "inetcache" }, 4.5, 0.03, ReasonCode.KnownCacheLocation, "internet cache"),
        new(new[] { "microsoft", "windows", "wer" }, 4.2, 0.04, ReasonCode.CrashOrDiagnosticData, "Windows Error Reporting data"),
        new(new[] { "microsoft", "windows", "webcache" }, 3.5, 0.08, ReasonCode.KnownCacheLocation, "web cache"),
        new(new[] { "nvidia", "dxcache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "NVIDIA shader cache"),
        new(new[] { "nvidia", "glcache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "NVIDIA shader cache"),
        new(new[] { "amd", "dxcache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "AMD shader cache"),
        new(new[] { "nuget", "v3-cache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "NuGet HTTP cache"),
        new(new[] { "npm-cache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "npm cache"),
        new(new[] { "pip", "cache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "pip cache"),
        new(new[] { "yarn", "cache" }, 3.8, 0.04, ReasonCode.KnownCacheLocation, "Yarn cache"),
    };

    // Developer tool caches: the directory names are specific enough to match anywhere in the path.
    private static readonly Loc[] ToolTable =
    {
        new(new[] { ".nuget", "packages" }, 3.0, 0.08, ReasonCode.KnownCacheLocation, "NuGet package cache"),
        new(new[] { ".gradle", "caches" }, 3.6, 0.05, ReasonCode.KnownCacheLocation, "Gradle cache"),
        new(new[] { ".m2", "repository" }, 2.8, 0.10, ReasonCode.KnownCacheLocation, "Maven repository cache"),
        new(new[] { ".npm", "_cacache" }, 4.0, 0.03, ReasonCode.KnownCacheLocation, "npm cache"),
        new(new[] { ".cargo", "registry" }, 3.0, 0.08, ReasonCode.KnownCacheLocation, "Cargo registry cache"),
    };

    // Names that identify disposable data when found anywhere below an application-data root.
    private static readonly Dictionary<string, NameRule> AppDataNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cache"] = new(4.0, 0.03, ReasonCode.KnownCacheLocation, "cache folder"),
        ["caches"] = new(4.0, 0.03, ReasonCode.KnownCacheLocation, "cache folder"),
        ["code cache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "code cache"),
        ["gpucache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "GPU cache"),
        ["shadercache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "shader cache"),
        ["grshadercache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "shader cache"),
        ["graphitedawncache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "shader cache"),
        ["dawncache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "shader cache"),
        ["d3dscache"] = new(4.2, 0.02, ReasonCode.KnownCacheLocation, "DirectX shader cache"),
        ["cache2"] = new(4.0, 0.03, ReasonCode.KnownCacheLocation, "browser cache"),
        ["media cache"] = new(4.0, 0.03, ReasonCode.KnownCacheLocation, "media cache"),
        ["cachestorage"] = new(3.8, 0.05, ReasonCode.KnownCacheLocation, "service-worker cache"),
        ["blob_storage"] = new(3.6, 0.06, ReasonCode.KnownCacheLocation, "browser blob storage"),
        ["inetcache"] = new(4.5, 0.03, ReasonCode.KnownCacheLocation, "internet cache"),
        ["squirreltemp"] = new(4.5, 0.03, ReasonCode.TempLocation, "updater temp folder"),
        ["crashpad"] = new(3.8, 0.04, ReasonCode.CrashOrDiagnosticData, "crash reporter data"),
        ["crash reports"] = new(3.8, 0.04, ReasonCode.CrashOrDiagnosticData, "crash reports"),
        ["crashes"] = new(3.6, 0.05, ReasonCode.CrashOrDiagnosticData, "crash reports"),
        ["temp"] = new(4.5, 0.03, ReasonCode.TempLocation, "temp folder"),
        ["tmp"] = new(4.2, 0.03, ReasonCode.TempLocation, "temp folder"),
        ["logs"] = new(2.6, 0.08, ReasonCode.LogFile, "log folder"),
        ["log"] = new(2.0, 0.08, ReasonCode.LogFile, "log folder"),
    };

    // Merely suggestive names outside application data - only ever applied to the node's OWN name.
    private static readonly Dictionary<string, NameRule> OwnNameHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cache"] = new(2.0, 0.20, ReasonCode.KnownCacheLocation, "folder named like a cache"),
        ["caches"] = new(2.0, 0.20, ReasonCode.KnownCacheLocation, "folder named like a cache"),
        [".cache"] = new(3.0, 0.10, ReasonCode.KnownCacheLocation, "hidden cache folder"),
        ["temp"] = new(1.8, 0.20, ReasonCode.TempLocation, "folder named like a temp folder"),
        ["tmp"] = new(1.8, 0.20, ReasonCode.TempLocation, "folder named like a temp folder"),
    };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        Signal? best = null;

        void Consider(ReasonCode code, double llr, double risk, string detail)
        {
            if (best is null || llr > best.Value.JunkLlr)
                best = new Signal(code, EvidenceGroup.Location, llr, risk, detail);
        }

        if (ctx.Machine.IsUnderTempRoot(ctx.Path))
            Consider(ReasonCode.TempLocation, 5.0, 0.03, "inside the system temp folder");

        var rel = ctx.AppDataRelative;
        if (rel is not null)
        {
            foreach (var loc in AppDataTable)
                if (PathKey.ContainsSequence(rel, loc.Seq))
                    Consider(loc.Code, loc.Llr, loc.Risk, loc.Label);

            foreach (var segment in rel)
                if (AppDataNames.TryGetValue(segment, out var n))
                    Consider(n.Code, n.Llr, n.Risk, $"{n.Label} '{segment}'");
        }
        else if (ctx.IsDirectory && OwnNameHints.TryGetValue(ctx.Name, out var w))
        {
            Consider(w.Code, w.Llr, w.Risk, $"{w.Label} '{ctx.Name}'");
        }

        foreach (var loc in ToolTable)
            if (PathKey.ContainsSequence(ctx.Segments, loc.Seq))
                Consider(loc.Code, loc.Llr, loc.Risk, loc.Label);

        if (best is { } b)
            sink.Add(b);
    }
}
