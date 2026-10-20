using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup.Rules;

/// <summary>
/// File-level patterns: temp/dump/log/backup extensions, stale installers, "name (1).ext" duplicates that are
/// byte-identical to their sibling, archives that were already extracted next to themselves, empty files.
/// </summary>
public sealed class FilePatternRule : IAssessmentRule
{
    private static readonly Regex DuplicateName = new(
        @"^(?<base>.+?)(?: \(\d+\)| - Copy(?: \(\d+\))?)(?<ext>\.[^.\\/]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CopyOfName = new(@"^Copy of (?<name>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InstallerHint = new(
        @"(setup|install|installer|[-_.](x64|x86|amd64|win64|win32)\b|[-_]v?\d+(\.\d+){1,3}\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> MeaningfulEmptyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "__init__.py", ".gitkeep", ".keep", ".gitignore", ".nojekyll", ".dockerignore", ".npmignore", "py.typed",
    };

    public void Evaluate(AssessmentContext ctx, SignalSink sink)
    {
        if (ctx.IsDirectory)
            return;

        var name = ctx.Name;
        var cls = ctx.FileClass;

        switch (cls)
        {
            case FileClass.Temp:
                sink.Add(ReasonCode.TempFile, EvidenceGroup.Pattern, 3.0, 0.02, $"temporary file type '{Path.GetExtension(name)}'");
                break;
            case FileClass.Dump:
                sink.Add(ReasonCode.CrashOrDiagnosticData, EvidenceGroup.Pattern, 3.0, 0.05, "crash dump");
                break;
            case FileClass.Log:
                sink.Add(ReasonCode.LogFile, EvidenceGroup.Pattern, 1.6, 0.08, "log file");
                break;
            case FileClass.Backup:
                sink.Add(ReasonCode.BackupOrLeftoverFile, EvidenceGroup.Pattern, 1.2, 0.30, $"backup/leftover type '{Path.GetExtension(name)}'");
                break;
            case FileClass.Installer:
                sink.Add(ReasonCode.StaleInstaller, EvidenceGroup.Pattern, 1.0, 0.20, "installer package");
                CheckInstalled(ctx, sink);
                break;
            case FileClass.Executable when InstallerHint.IsMatch(name):
                sink.Add(ReasonCode.StaleInstaller, EvidenceGroup.Pattern, 0.7, 0.25, "executable that looks like an installer");
                CheckInstalled(ctx, sink);
                break;
            case FileClass.DiskImage:
                sink.Add(ReasonCode.StaleInstaller, EvidenceGroup.Pattern, 0.4, 0.35, "disk image");
                break;
            case FileClass.Archive:
                CheckExtractedArchive(ctx, sink);
                break;
        }

        CheckDuplicate(ctx, sink);

        if (ctx.Node.Size == 0 && !MeaningfulEmptyFiles.Contains(name))
            sink.Add(ReasonCode.EmptyFile, EvidenceGroup.Pattern, 1.5, 0.05, "zero-byte file");
    }

    private static void CheckInstalled(AssessmentContext ctx, SignalSink sink)
    {
        if (ctx.Machine.HasInstalledAppInventory && ctx.Machine.FileNameMentionsInstalledApp(ctx.Name, out var app))
            sink.Add(ReasonCode.InstallerOfInstalledApp, EvidenceGroup.Ownership, 1.8, 0.10, $"'{app}' is already installed");
    }

    private static void CheckExtractedArchive(AssessmentContext ctx, SignalSink sink)
    {
        try
        {
            var dir = Path.GetDirectoryName(ctx.Path);
            if (string.IsNullOrEmpty(dir))
                return;

            var baseName = Path.GetFileNameWithoutExtension(ctx.Name);
            if (baseName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                baseName = baseName[..^4];

            var sibling = Path.Combine(dir, baseName);
            if (Directory.Exists(sibling) && new DirectoryInfo(sibling).EnumerateFileSystemInfos().GetEnumerator().MoveNext())
                sink.Add(ReasonCode.ArchiveAlreadyExtracted, EvidenceGroup.Pattern, 2.4, 0.25, $"folder '{baseName}' sits next to it");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // evidence unavailable -> no signal
        }
    }

    private static void CheckDuplicate(AssessmentContext ctx, SignalSink sink)
    {
        // cheap pre-check: duplicates are named "x (1).ext", "x - Copy.ext" or "Copy of x.ext"
        var n = ctx.Name;
        if (n.IndexOf('(') < 0 && n.IndexOf("Copy", StringComparison.Ordinal) < 0)
            return;

        var dir = Path.GetDirectoryName(ctx.Path);
        if (string.IsNullOrEmpty(dir))
            return;

        string? originalName = null;
        var m = DuplicateName.Match(ctx.Name);
        if (m.Success)
            originalName = m.Groups["base"].Value + m.Groups["ext"].Value;
        else
        {
            var c = CopyOfName.Match(ctx.Name);
            if (c.Success)
                originalName = c.Groups["name"].Value;
        }

        if (originalName is null)
            return;

        var original = Path.Combine(dir, originalName);
        if (FilesProbablyIdentical(ctx.Path, original))
            sink.Add(ReasonCode.DuplicateOfSibling, EvidenceGroup.Pattern, 4.0, 0.05, $"identical to '{originalName}'");
    }

    /// <summary>Checks every byte so a partial match can never mark a different file as a safe duplicate.</summary>
    public static bool FilesProbablyIdentical(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length)
                return false;

            long len = fa.Length;
            if (len == 0)
                return true;

            using var sa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            const int chunk = 64 * 1024;
            var bufA = new byte[chunk];
            var bufB = new byte[bufA.Length];
            while (true)
            {
                int ra = ReadFully(sa, bufA);
                int rb = ReadFully(sb, bufB);
                if (ra != rb || !bufA.AsSpan(0, ra).SequenceEqual(bufB.AsSpan(0, rb)))
                    return false;
                if (ra == 0)
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static int ReadFully(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
