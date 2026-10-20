using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Cleanup;

/// <summary>
/// Builds a realistic "user profile" on disk (…/Users/test with AppData\Roaming, Local, Temp ...), controls the age of
/// everything in it, fakes the machine state (installed apps, running programs, recent items) and runs the real
/// engine + selector over it.
/// </summary>
public sealed class CleanupScenario : IDisposable
{
    private readonly TempTestDirectory _dir = new();
    private readonly Dictionary<string, int> _ageDays = new(StringComparer.OrdinalIgnoreCase);

    public CleanupScenario()
    {
        Root = System.IO.Path.Combine(_dir.Path, "Users", "test");
        Directory.CreateDirectory(Root);

        Roaming = Abs("AppData/Roaming");
        Local = Abs("AppData/Local");
        Directory.CreateDirectory(Roaming);
        Directory.CreateDirectory(Local);

        Machine.UtcNow = DateTime.UtcNow;
        Machine.HasInstalledAppInventory = true;
        Machine.AppDataRoots.Add(Roaming);
        Machine.AppDataRoots.Add(Local);
        Machine.TempRoots.Add(System.IO.Path.Combine(Local, "Temp"));
        Machine.ContainerRoots.Add(Root);
        Machine.ContainerRoots.Add(Abs("Downloads"));
    }

    public string Root { get; }
    public string Roaming { get; }
    public string Local { get; }
    public SystemSnapshotData Machine { get; } = new();

    // creation time can not be faked on Linux, so it is switched off here and covered by a Windows-only test
    public SmartCleanupOptions Options { get; } = new() { UseCreationTime = false, MaxDegreeOfParallelism = 2 };

    public Func<string, string?>? SafetyGate { get; set; }

    public string Abs(string rel) => System.IO.Path.Combine(Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string File(string rel, int ageDays, long size = 2048)
    {
        var path = Abs(rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            fs.SetLength(size);
        _ageDays[path] = ageDays;
        return path;
    }

    public string Bytes(string rel, byte[] data, int ageDays)
    {
        var path = Abs(rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, data);
        _ageDays[path] = ageDays;
        return path;
    }

    public string Dir(string rel, int ageDays)
    {
        var path = Abs(rel);
        Directory.CreateDirectory(path);
        _ageDays[path] = ageDays;
        return path;
    }

    public ScenarioResult Run()
    {
        ApplyTimes();
        var rootSnap = Build(Root, null, true, new Dictionary<string, NodeSnapshot>(StringComparer.OrdinalIgnoreCase), out var byRel);

        var machine = new SystemSnapshot(Machine);
        var engine = SafetyGate is null
            ? new JunkAssessmentEngine()
            : new JunkAssessmentEngine(JunkAssessmentEngine.DefaultRules(), new DirectoryFactsCollector(), SafetyGate);

        var analysis = engine.Analyze(new[] { rootSnap }, machine, Options);
        return new ScenarioResult(this, rootSnap, byRel, analysis);
    }

    // ── file system helpers ────────────────────────────────────────────────

    private void ApplyTimes()
    {
        var now = DateTime.UtcNow;
        // files first
        foreach (var (path, age) in _ageDays)
        {
            if (System.IO.File.Exists(path))
            {
                var t = now.AddDays(-age);
                System.IO.File.SetLastWriteTimeUtc(path, t);
                System.IO.File.SetLastAccessTimeUtc(path, t);
            }
        }
        // directories: as old as their newest content (that is how real directory timestamps behave)
        Settle(Root);

        int Settle(string dir)
        {
            int newest = _ageDays.TryGetValue(dir, out var own) ? own : int.MaxValue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                int a = Directory.Exists(entry) ? Settle(entry) : (_ageDays.TryGetValue(entry, out var fa) ? fa : 0);
                newest = Math.Min(newest, a);
            }
            if (newest == int.MaxValue) newest = 0;
            var t = now.AddDays(-newest);
            Directory.SetLastWriteTimeUtc(dir, t);
            Directory.SetLastAccessTimeUtc(dir, t);
            return newest;
        }
    }

    private NodeSnapshot Build(string path, NodeSnapshot? parent, bool isDir, Dictionary<string, NodeSnapshot> _, out Dictionary<string, NodeSnapshot> byRel)
    {
        byRel = new Dictionary<string, NodeSnapshot>(StringComparer.OrdinalIgnoreCase);
        var root = BuildRec(path, parent, isDir, byRel);
        return root;
    }

    private NodeSnapshot BuildRec(string path, NodeSnapshot? parent, bool isDir, Dictionary<string, NodeSnapshot> byRel)
    {
        FileSystemInfo info = isDir ? new DirectoryInfo(path) : new FileInfo(path);
        long size = isDir ? DirSize(path) : ((FileInfo)info).Length;

        var snap = new NodeSnapshot
        {
            Path = path,
            Name = info.Name,
            IsDirectory = isDir,
            Size = size,
            LastWriteUtc = info.LastWriteTimeUtc,
            LastAccessUtc = info.LastAccessTimeUtc,
            Depth = parent is null ? 0 : parent.Depth + 1,
            Parent = parent,
        };

        var rel = System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/');
        byRel[rel] = snap;

        if (isDir)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                snap.Children.Add(BuildRec(entry, snap, Directory.Exists(entry), byRel));
        }
        return snap;
    }

    private static long DirSize(string dir) =>
        new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    public void Dispose() => _dir.Dispose();
}

public sealed class ScenarioResult
{
    private readonly CleanupScenario _scenario;
    private readonly NodeSnapshot _root;
    private readonly Dictionary<string, NodeSnapshot> _byRel;

    public ScenarioResult(CleanupScenario scenario, NodeSnapshot root, Dictionary<string, NodeSnapshot> byRel, AnalysisResult analysis)
    {
        _scenario = scenario;
        _root = root;
        _byRel = byRel;
        Analysis = analysis;
    }

    public AnalysisResult Analysis { get; }

    public JunkAssessment this[string rel] => Analysis.Assessments[_byRel[rel]];

    public CleanupTier Tier(string rel) => this[rel].Tier;

    public bool Has(string rel, ReasonCode code) => this[rel].Reasons.Any(r => r.Code == code);

    public IReadOnlyList<string> Selected(SmartSelectMode mode)
    {
        var plan = new SmartCleanupSelector().Plan(new[] { _root }, Analysis.Assessments, mode, _scenario.Options);
        return plan.Selected
            .Select(n => System.IO.Path.GetRelativePath(_scenario.Root, n.Path).Replace('\\', '/'))
            .ToList();
    }

    public string Explain(string rel) => rel + " => " + this[rel].Explain(10);
}
