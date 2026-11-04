using Declutterer.Abstractions;
using Declutterer.Application;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Domain.Services.Observation;
using Declutterer.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Declutterer.Tests.Observation;

public class ObservationServicesTests : IDisposable
{
    private readonly TempTestDirectory _t = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _t.Dispose();
    }

    private ServiceProvider Provider() =>
        new ServiceCollection().AddObservationServices(Path.Combine(_t.Path, "ledger.db")).BuildServiceProvider();

    [Fact]
    public void EveryObservationServiceResolves_AndTheLedgerIsOneInstance()
    {
        using var p = Provider();

        Assert.NotNull(p.GetRequiredService<IObservationCoordinator>());
        Assert.NotNull(p.GetRequiredService<IPreDeleteRevalidator>());
        Assert.NotNull(p.GetRequiredService<IObservationReassessor>());
        Assert.NotNull(p.GetRequiredService<ObservationCheckInService>());
        Assert.NotNull(p.GetRequiredService<IObservationScheduler>());
        Assert.NotNull(p.GetRequiredService<JunkAssessmentEngine>());
        Assert.NotNull(p.GetRequiredService<ISystemSnapshotProvider>());
        Assert.Same(p.GetRequiredService<IObservationLedger>(), p.GetRequiredService<IOwnActivityRecorder>());
        Assert.Same(p.GetRequiredService<ObservationOptions>(), p.GetRequiredService<ObservationOptions>());
        Assert.False(p.GetRequiredService<ObservationOptions>().Enabled); // off until the user agrees
    }

    [Fact]
    public void ResolvingTheServices_DoesNotTouchTheDatabase()
    {
        using var p = Provider();

        p.GetRequiredService<IObservationCoordinator>();
        p.GetRequiredService<IPreDeleteRevalidator>();

        Assert.False(File.Exists(Path.Combine(_t.Path, "ledger.db"))); // created lazily, on first real use
    }

    [Fact]
    public void TheDeleteService_PicksUpTheRevalidatorFromTheContainer_LikeTheRealRegistrationDoes()
    {
        var services = new ServiceCollection().AddObservationServices(Path.Combine(_t.Path, "ledger.db"));
        services.AddSingleton<IDeletionHistoryRepository, NoHistory>();
        services.AddSingleton<IDeleteService, DeleteService>();
        using var p = services.BuildServiceProvider();

        Assert.IsType<DeleteService>(p.GetRequiredService<IDeleteService>());
    }

    private sealed class NoHistory : IDeletionHistoryRepository
    {
        public Task AddEntryAsync(DeletionHistoryEntry entry) => Task.CompletedTask;
        public Task<IList<DeletionHistoryEntry>> GetEntriesAsync() => Task.FromResult<IList<DeletionHistoryEntry>>(new List<DeletionHistoryEntry>());
        public Task<IList<DeletionHistoryEntry>> GetEntriesBetweenAsync(DateTime from, DateTime to) => GetEntriesAsync();
        public Task<IList<DeletionHistoryEntry>> GetEntriesByPathAsync(string pathPattern) => GetEntriesAsync();
        public Task DeleteEntryAsync(string entryId) => Task.CompletedTask;
        public Task ClearHistoryAsync() => Task.CompletedTask;
        public Task<int> GetEntryCountAsync() => Task.FromResult(0);
    }

    [Fact]
    public async Task TheWholeStackWorksFromTheContainer_EnableEnrollCheckIn()
    {
        var item = _t.CreateSubDirectory("watched");
        _t.CreateFile("watched/a.dat", 100);
        using var p = Provider();
        var coordinator = p.GetRequiredService<IObservationCoordinator>();

        await coordinator.SetEnabledAsync(true);
        var enrolled = await coordinator.EnrollAsync(new[]
        {
            new ObservationCandidate(item, "watched", 100, ObservationClass.GenericFolder, CleanupTier.Likely, 0.8, 0.2, null,
                ObservationCandidateReason.Suspicious, new[] { ReasonCode.KnownCacheLocation }),
        });
        var run = await coordinator.RunCheckInAsync("test");

        Assert.Equal(1, enrolled.Enrolled);
        Assert.Equal(CheckInOutcome.Ok, run.Outcome);
        Assert.Single(await coordinator.GetWatchlistAsync());
        Assert.True(File.Exists(Path.Combine(_t.Path, "ledger.db")));
    }

    // ── headless ─────────────────────────────────────────────────────────────────────────────

    private sealed class FakeCoordinator : IObservationCoordinator
    {
        public bool IsEnabled { get; set; } = true;
        public ObservationOptions Options { get; } = new();
        public bool IsLedgerAvailable => true;
        public string LedgerPath => "x";
        public CheckInOutcome Outcome { get; set; } = CheckInOutcome.Ok;
        public List<string> Triggers { get; } = new();
        public int Initialized { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) { Initialized++; return Task.CompletedTask; }
        public Task<CheckInRunResult> RunCheckInAsync(string trigger, CancellationToken ct = default) { Triggers.Add(trigger); return Task.FromResult(CheckInRunResult.Of(Outcome)); }
        public Task<bool> HasConsentAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task SetEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task<EnrollmentResult> EnrollAsync(IReadOnlyList<ObservationCandidate> c, CancellationToken ct = default) => Task.FromResult(EnrollmentResult.None("x"));
        public Task StartupCatchUpAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<WatchEntry>> GetWatchlistAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WatchEntry>>(Array.Empty<WatchEntry>());
        public Task<RunLogRecord?> GetLastRunAsync(CancellationToken ct = default) => Task.FromResult<RunLogRecord?>(null);
        public Task<bool> StopWatchingAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> PinAsync(long id, bool pinned, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> SnoozeAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> ReenrollAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<ReassessmentResult?> ReassessAsync(long id, CancellationToken ct = default) => Task.FromResult<ReassessmentResult?>(null);
        public Task ClearAllDataAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Theory]
    [InlineData(CheckInOutcome.Ok, 0)]
    [InlineData(CheckInOutcome.Disabled, 0)]
    [InlineData(CheckInOutcome.LockHeld, 2)]
    [InlineData(CheckInOutcome.Error, 1)]
    [InlineData(CheckInOutcome.LedgerUnavailable, 1)]
    [InlineData(CheckInOutcome.Cancelled, 1)]
    public async Task Headless_MapsTheOutcomeToTheExitCode(CheckInOutcome outcome, int exit)
    {
        var coordinator = new FakeCoordinator { Outcome = outcome };

        var code = await ObservationHeadless.RunAsync(coordinator, "headless", CancellationToken.None);

        Assert.Equal(exit, code);
        Assert.Equal(new[] { "headless" }, coordinator.Triggers);
        Assert.Equal(1, coordinator.Initialized);
    }

    [Fact]
    public async Task Headless_DoesNothing_WhenTheFeatureIsOff()
    {
        var coordinator = new FakeCoordinator { IsEnabled = false };

        Assert.Equal(0, await ObservationHeadless.RunAsync(coordinator, "headless", CancellationToken.None));
        Assert.Empty(coordinator.Triggers);
    }

    [Theory]
    [InlineData("--observe", true)]
    [InlineData("--OBSERVE", true)]
    [InlineData("--other", false)]
    public void TheFlag_IsRecognised(string arg, bool expected)
    {
        Assert.Equal(expected, ObservationHeadless.IsRequested(new[] { "x", arg }));
    }
}
