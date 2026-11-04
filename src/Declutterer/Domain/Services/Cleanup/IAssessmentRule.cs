using System;
using System.Collections.Generic;
using System.IO;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>One independent source of evidence. Rules must be stateless and thread-safe.</summary>
public interface IAssessmentRule
{
    void Evaluate(AssessmentContext ctx, SignalSink sink);
}

/// <summary>Collects the signals emitted by rules and lets later rules react to earlier ones.</summary>
public sealed class SignalSink
{
    private readonly List<Signal> _signals = new();

    public IReadOnlyList<Signal> Signals => _signals;

    public void Add(Signal s) => _signals.Add(s);

    public void Add(ReasonCode code, EvidenceGroup group, double junkLlr, double risk, string detail, bool hardKeep = false, double riskRelief = 0) =>
        _signals.Add(new Signal(code, group, junkLlr, risk, detail, hardKeep, riskRelief));

    public bool Has(ReasonCode code)
    {
        foreach (var s in _signals)
            if (s.Code == code) return true;
        return false;
    }

    /// <summary>
    /// The item is (inside) a strongly identified cache / temp / regenerable location. In such places the usual
    /// "recently modified" and "contains many source files" alarms are meaningless: caches are rewritten constantly
    /// and node_modules is full of source code, yet both are disposable.
    /// </summary>
    public bool InDisposableContext => IsDisposableRegion(_signals);

    /// <summary>True if the signals identify a strongly disposable location (cache, temp, build output, crash data).</summary>
    public static bool IsDisposableRegion(IReadOnlyList<Signal> signals)
    {
        foreach (var s in signals)
        {
            if (s.Group == EvidenceGroup.Location && s.JunkLlr >= 3.0 &&
                s.Code is ReasonCode.KnownCacheLocation or ReasonCode.TempLocation or ReasonCode.RegenerableBuildOutput
                    or ReasonCode.CrashOrDiagnosticData)
                return true;
        }
        return false;
    }

    /// <summary>A byte-identical copy exists next to the item, so its content is not at risk of being lost.</summary>
    public bool ContentIsRedundant => Has(ReasonCode.DuplicateOfSibling);
}

/// <summary>Everything a rule may look at for one node. Cheap members first; expensive ones are lazy.</summary>
public sealed class AssessmentContext
{
    private string[]? _segments;
    private FileClass? _fileClass;
    private DateTime? _creationUtc;
    private bool _creationResolved;
    private DirectoryFacts? _content;
    private string[]? _appDataRelative;
    private bool _appDataResolved;

    public AssessmentContext(NodeSnapshot node, ISystemSnapshot system, SmartCleanupOptions options, MarkerCache markers,
        DirectoryFacts? facts, bool accessTimeReliable, ObservationSummary? observation = null)
    {
        Observation = observation;
        Node = node;
        Machine = system;
        Options = options;
        Markers = markers;
        Facts = facts;
        AccessTimeReliable = accessTimeReliable;
    }

    public NodeSnapshot Node { get; }
    /// <summary>Point-in-time knowledge about the machine (installed apps, running processes ...).</summary>
    public ISystemSnapshot Machine { get; }
    public SmartCleanupOptions Options { get; }
    public MarkerCache Markers { get; }

    /// <summary>Disk facts for directories (null in stage A / when not inspected).</summary>
    public DirectoryFacts? Facts { get; }

    public bool AccessTimeReliable { get; }

    /// <summary>What watching this item over time showed, if it is on the watchlist (null otherwise).</summary>
    public ObservationSummary? Observation { get; }

    public DateTime NowUtc => Machine.UtcNow;
    public bool IsDirectory => Node.IsDirectory;
    public string Path => Node.Path;
    public string Name => Node.Name;
    public string[] Segments => _segments ??= PathKey.Segments(Node.Path);

    public FileClass FileClass => _fileClass ??= IsDirectory ? FileClass.Unknown : FileClassifier.Classify(Node.Name);

    /// <summary>
    /// Path segments below the application-data root (AppData\Roaming, AppData\Local, ProgramData ...) this node lives in,
    /// or null when it is not inside one. Application-data patterns are matched against this, never against the absolute
    /// path, so a scan root that merely happens to sit below some "AppData\Local\Temp" does not taint its contents.
    /// </summary>
    public string[]? AppDataRelative
    {
        get
        {
            if (!_appDataResolved)
            {
                _appDataResolved = true;
                if (Machine.IsUnderAppDataRoot(Node.Path, out var root))
                {
                    var norm = PathKey.Normalize(Node.Path);
                    _appDataRelative = norm.Length <= root.Length ? Array.Empty<string>() : PathKey.Segments(norm[root.Length..]);
                }
            }
            return _appDataRelative;
        }
    }

    public bool IsUnderAppData => AppDataRelative is not null;

    /// <summary>For files: the file itself. For directories: the inspected subtree (null until inspected).</summary>
    public DirectoryFacts? Content =>
        IsDirectory
            ? Facts
            : _content ??= DirectoryFacts.ForFile(FileClass, Node.Size, Node.LastWriteUtc ?? DateTime.MinValue);

    /// <summary>Creation time, resolved lazily from disk (a freshly copied file keeps an old write time but gets a new creation time).</summary>
    public DateTime? CreationUtc
    {
        get
        {
            if (_creationResolved)
                return _creationUtc;
            _creationResolved = true;
            try
            {
                var t = IsDirectory ? Directory.GetCreationTimeUtc(Node.Path) : File.GetCreationTimeUtc(Node.Path);
                _creationUtc = t.Year < 1700 ? null : t; // 1601 = "does not exist"
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _creationUtc = null;
            }
            return _creationUtc;
        }
    }
}
