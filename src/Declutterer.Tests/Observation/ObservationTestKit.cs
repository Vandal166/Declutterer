using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Observation;

internal static class ObservationTestKit
{
    public static WatchEntry Entry(string path, DateTime now, ObservationClass cls = ObservationClass.GenericFolder, int requiredDays = 45,
        Fingerprint? baseline = null, WatchState state = WatchState.Watching) => new()
    {
        Key = ObservationKey.From(path),
        DisplayPath = path,
        Class = cls,
        State = state,
        EnrolledUtc = now,
        QuietSinceUtc = now,
        RequiredDays = requiredDays,
        SizeAtEnrollment = 64L * 1024 * 1024,
        NominatedTier = CleanupTier.Likely,
        NominatedScore = 0.8,
        BaselineFingerprint = baseline,
    };

    public static Fingerprint Print(string hash = "aa", bool complete = true) => new()
    {
        RootHash = hash,
        SignificantHash = hash,
        DroppingHash = "d",
        Children = new Dictionary<string, string> { ["x"] = hash },
        EntryCount = 3,
        CapturedUtc = DateTime.UtcNow,
        Truncated = !complete,
    };
}

internal sealed class FakeSnapshotProvider : Declutterer.Abstractions.ISystemSnapshotProvider
{
    private readonly Declutterer.Domain.Services.Cleanup.SystemSnapshotData _data;
    private readonly Func<DateTime> _now;

    public FakeSnapshotProvider(Declutterer.Domain.Services.Cleanup.SystemSnapshotData data, Func<DateTime> now)
    {
        _data = data;
        _now = now;
    }

    public int Captures { get; private set; }

    public Task<Declutterer.Abstractions.ISystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        Captures++;
        _data.UtcNow = _now();
        return Task.FromResult<Declutterer.Abstractions.ISystemSnapshot>(new Declutterer.Domain.Services.Cleanup.SystemSnapshot(_data));
    }
}

internal sealed class FakeIdentity : Declutterer.Abstractions.IFileIdentityProvider
{
    public bool IsSupported { get; set; } = true;
    public Dictionary<string, string> Ids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? TryGetFileId(string path) => IsSupported && Ids.TryGetValue(path, out var id) ? id : null;
}

internal sealed class FakeReassessor : Declutterer.Abstractions.IObservationReassessor
{
    public List<long> Calls { get; } = new();

    public Task<ReassessmentResult?> ReassessAsync(WatchEntry entry, CancellationToken cancellationToken = default)
    {
        Calls.Add(entry.Id);
        var assessment = new JunkAssessment
        {
            JunkScore = 0.8, DeletionRisk = 0.1, Confidence = 0.7, Tier = CleanupTier.Likely, Reasons = Array.Empty<Signal>(),
        };
        return Task.FromResult<ReassessmentResult?>(new ReassessmentResult(CleanupTier.Likely, 0.8, 0.1, "fake", assessment));
    }
}

/// <summary>A source that burns simulated time, to exercise the run budget.</summary>
internal sealed class SlowSource : Declutterer.Abstractions.IActivitySource
{
    private readonly ManualTimeProvider _time;
    private readonly TimeSpan _cost;

    public SlowSource(ManualTimeProvider time, TimeSpan cost)
    {
        _time = time;
        _cost = cost;
    }

    public List<string> Seen { get; } = new();
    public ObservationSource Source => ObservationSource.None;

    public Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken)
    {
        lock (Seen) Seen.Add(entry.DisplayPath);
        _time.Advance(_cost);
        return Task.FromResult(ActivityReport.Nothing(ObservationSource.None));
    }
}

internal sealed class ThrowingSource : Declutterer.Abstractions.IActivitySource
{
    public ObservationSource Source => ObservationSource.UsageArtifact;

    public Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("boom");
}

internal sealed class FakeVolumeActivity : Declutterer.Abstractions.IVolumeActivityProvider
{
    public VolumeActivityReport Report { get; set; } = new(false, Array.Empty<DateTime>(), false, null);

    public Task<VolumeActivityReport> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) => Task.FromResult(Report);
}

/// <summary>
/// A whole observation setup on disk: temp folders, a real SQLite ledger, a hand-moved clock and the real check-in service.
/// "Days" pass by advancing the clock; the files keep their real (settled, old) timestamps.
/// </summary>
internal sealed class CheckInRig : IDisposable
{
    public TempTestDirectory Dir { get; } = new();
    public ManualTimeProvider Time { get; } = new();
    public SqliteObservationLedger Ledger { get; }
    public Declutterer.Domain.Services.Cleanup.SystemSnapshotData Machine { get; } = new() { HasInstalledAppInventory = true };
    public ObservationOptions Options { get; } = new() { Enabled = true };
    public FingerprintWalker Walker { get; }
    public FakeIdentity Identity { get; } = new();
    public FakeReassessor Reassessor { get; } = new();
    public FakeVolumeActivity Volume { get; } = new();
    public List<Declutterer.Abstractions.IActivitySource> Sources { get; } = new();
    public ObservationCheckInService Service { get; private set; } = null!;

    public CheckInRig(bool defaultSources = true)
    {
        Ledger = new SqliteObservationLedger(Path.Combine(Dir.Path, "_ledger", "ledger.db"), Time);
        Walker = new FingerprintWalker(Time);
        if (defaultSources)
        {
            Sources.Add(new SnapshotDiffSource(Identity));
            Sources.Add(new UsageArtifactSource());
            Sources.Add(new AccessTimeSource());
        }
    }

    public void Build() => Service = new ObservationCheckInService(Ledger, new FakeSnapshotProvider(Machine, () => Time.UtcNow), Sources,
        Walker, Options, Reassessor, Volume, Time);

    public string Item(string relative = "item") => Path.Combine(Dir.Path, relative);

    /// <summary>Creates a few files in the folder and gives everything a fixed old timestamp.</summary>
    public string CreateItem(string relative = "item", int files = 3)
    {
        for (int i = 0; i < files; i++)
            Dir.CreateFile($"{relative}/sub/f{i}.dat", 1000 + i);
        Settle(relative);
        return Item(relative);
    }

    public void Settle(string relative = "item")
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var root = Item(relative);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(f, t);
            File.SetLastAccessTimeUtc(f, t);
        }
        foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            Directory.SetLastWriteTimeUtc(d, t);
        Directory.SetLastWriteTimeUtc(root, t);
    }

    public async Task<WatchEntry> EnrollAsync(string relative = "item", int requiredDays = 5, ObservationClass cls = ObservationClass.GenericFolder)
    {
        var path = Item(relative);
        var entry = ObservationTestKit.Entry(path, Time.UtcNow, cls, requiredDays, Walker.Walk(path)) with { FileId = Identity.TryGetFileId(path) };
        return (await Ledger.EnrollAsync(entry))!;
    }

    public Task<CheckInRunResult> RunAsync(string trigger = "test", CancellationToken ct = default) => Service.RunAsync(trigger, ct);

    /// <summary>Moves the clock by whole days, then checks in.</summary>
    public async Task<CheckInRunResult> RunAfterDaysAsync(double days)
    {
        Time.AdvanceDays(days);
        return await RunAsync();
    }

    public async Task<WatchEntry> GetAsync(long id) => (await Ledger.GetAsync(id))!;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Dir.Dispose();
    }
}

internal sealed class ScriptedCoordinator : Declutterer.Abstractions.IObservationCoordinator
{
    public bool IsEnabled { get; set; }
    public bool Consent { get; set; }
    public bool IsLedgerAvailable { get; set; } = true;
    public string LedgerPath { get; set; } = "/data/Declutterer/observation/ledger.db";
    public ObservationOptions Options { get; } = new();
    public List<WatchEntry> Entries { get; } = new();
    public RunLogRecord? LastRun { get; set; }
    public EnrollmentResult EnrollResult { get; set; } = new(0, 0, Array.Empty<EnrollmentSkip>());
    public CheckInRunResult CheckInResult { get; set; } = CheckInRunResult.Of(CheckInOutcome.Ok);
    public ReassessmentResult? ReassessResult { get; set; }
    public bool ReenrollSucceeds { get; set; } = true;
    public Exception? EnrollThrows { get; set; }
    public List<string> Calls { get; } = new();
    public List<ObservationCandidate> EnrolledCandidates { get; } = new();

    public Task InitializeAsync(CancellationToken ct = default) { Calls.Add("init"); return Task.CompletedTask; }
    public Task<bool> HasConsentAsync(CancellationToken ct = default) { Calls.Add("consent?"); return Task.FromResult(Consent); }
    public Task SetEnabledAsync(bool enabled, CancellationToken ct = default) { Calls.Add("enabled=" + enabled); IsEnabled = enabled; if (enabled) Consent = true; return Task.CompletedTask; }

    public Task<EnrollmentResult> EnrollAsync(IReadOnlyList<ObservationCandidate> candidates, CancellationToken ct = default)
    {
        Calls.Add("enroll");
        if (EnrollThrows is not null)
            throw EnrollThrows;
        EnrolledCandidates.AddRange(candidates);
        return Task.FromResult(EnrollResult);
    }

    public Task<CheckInRunResult> RunCheckInAsync(string trigger, CancellationToken ct = default) { Calls.Add("checkin:" + trigger); return Task.FromResult(CheckInResult); }
    public Task StartupCatchUpAsync(CancellationToken ct = default) { Calls.Add("startup"); return Task.CompletedTask; }
    public Task<IReadOnlyList<WatchEntry>> GetWatchlistAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WatchEntry>>(Entries.ToList());
    public Task<RunLogRecord?> GetLastRunAsync(CancellationToken ct = default) => Task.FromResult(LastRun);
    public Task<bool> StopWatchingAsync(long id, CancellationToken ct = default) { Calls.Add("stop:" + id); return Task.FromResult(true); }
    public Task<bool> PinAsync(long id, bool pinned, CancellationToken ct = default) { Calls.Add((pinned ? "pin:" : "unpin:") + id); return Task.FromResult(true); }
    public Task<bool> SnoozeAsync(long id, CancellationToken ct = default) { Calls.Add("snooze:" + id); return Task.FromResult(true); }
    public Task<bool> ReenrollAsync(long id, CancellationToken ct = default) { Calls.Add("reenroll:" + id); return Task.FromResult(ReenrollSucceeds); }
    public Task<ReassessmentResult?> ReassessAsync(long id, CancellationToken ct = default) { Calls.Add("reassess:" + id); return Task.FromResult(ReassessResult); }
    public Task ClearAllDataAsync(CancellationToken ct = default) { Calls.Add("clear"); Entries.Clear(); return Task.CompletedTask; }
}

internal sealed class FakeConfirmations : Declutterer.Abstractions.IConfirmationDialogService
{
    public Queue<bool> Answers { get; } = new();
    public bool Default { get; set; } = true;
    public List<(string Title, string Message, string? ConfirmText)> Prompts { get; } = new();

    public void SetOwnerWindow(Avalonia.Controls.Window owner) { }

    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        Prompts.Add((title, message, null));
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : Default);
    }

    public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText)
    {
        Prompts.Add((title, message, confirmText));
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : Default);
    }
}

internal sealed class FakeExplorer : Declutterer.Abstractions.IExplorerLauncher
{
    public List<string> Opened { get; } = new();
    public Exception? Throws { get; set; }

    public void OpenInExplorer(string path)
    {
        if (Throws is not null)
            throw Throws;
        Opened.Add(path);
    }
}

internal sealed class FakeScheduler : Declutterer.Abstractions.IObservationScheduler
{
    public bool IsSupported { get; set; } = true;
    public bool Installed { get; set; }
    public List<string> Calls { get; } = new();
    public Declutterer.Abstractions.ScheduleOperationResult InstallResult { get; set; } = new(true, "installed");

    public Declutterer.Abstractions.ScheduleDescription Describe() => new("fake scheduler", "A daily thing.", "\"/x/Declutterer\" --observe", new[] { "Runs as you.", "Metadata only." });
    public Task<Declutterer.Abstractions.ScheduleStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new Declutterer.Abstractions.ScheduleStatus(Installed, null));
    public Task<Declutterer.Abstractions.ScheduleOperationResult> InstallAsync(CancellationToken ct = default) { Calls.Add("install"); Installed = InstallResult.Success; return Task.FromResult(InstallResult); }
    public Task<Declutterer.Abstractions.ScheduleOperationResult> UninstallAsync(CancellationToken ct = default) { Calls.Add("uninstall"); Installed = false; return Task.FromResult(new Declutterer.Abstractions.ScheduleOperationResult(true, "removed")); }
}
