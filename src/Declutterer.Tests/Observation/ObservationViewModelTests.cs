using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;
using Declutterer.UI.ViewModels;
using NSubstitute;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Tests.Observation;

public class ObservationViewModelTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly ScriptedCoordinator _coordinator = new();
    private readonly FakeConfirmations _confirmations = new();
    private readonly FakeExplorer _explorer = new();
    private readonly FakeScheduler _scheduler = new();

    private WatchlistViewModel Watchlist() => new(_coordinator, _confirmations, _explorer, _scheduler);

    private static WatchEntry Entry(long id, string path, WatchState state = WatchState.Watching) =>
        ObservationTestKit.Entry(path, Now, ObservationClass.AppData, 28, state: state) with { Id = id, CoveredDays = 7, SizeAtEnrollment = 5L * 1024 * 1024 };

    // ── the watchlist page ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_ShowsTheEntriesMostInterestingFirst_AndTheStatusTexts()
    {
        _coordinator.IsEnabled = true;
        _coordinator.Entries.AddRange(new[]
        {
            Entry(1, "/data/b-watching"),
            Entry(2, "/data/a-quiet", WatchState.QuietEligible) with { ValidUntilUtc = Now.AddDays(20) },
            Entry(3, "/data/c-alive", WatchState.Alive) with { AliveUntilUtc = Now.AddDays(10) },
            Entry(4, "/data/d-gave-up", WatchState.Unobservable),
        });
        _coordinator.LastRun = new RunLogRecord(Now, Now, "startup", CheckInOutcome.Ok, 4, "1 alive");
        var vm = Watchlist();

        await vm.LoadAsync();

        Assert.True(vm.IsEnabled);
        Assert.True(vm.HasRows);
        Assert.Equal(new[] { "a-quiet", "b-watching", "c-alive", "d-gave-up" }, vm.Rows.Select(r => r.Name).ToArray());
        Assert.Contains("ledger.db", vm.StorageText);
        Assert.Contains("4 item(s) checked", vm.LastRunText);
        Assert.True(vm.IsSchedulerSupported);
        Assert.False(vm.IsSchedulerInstalled);
        Assert.Contains("Optional", vm.SchedulerText);
    }

    [Fact]
    public async Task AnEmptyWatchlist_SaysSo()
    {
        var vm = Watchlist();

        await vm.LoadAsync();

        Assert.False(vm.HasRows);
        Assert.Equal("No check-in has run yet.", vm.LastRunText);
    }

    [Fact]
    public void TheRows_ExplainEachState()
    {
        WatchlistRowViewModel R(WatchEntry e) => new(e, Now);

        var watching = R(Entry(1, "/data/app"));
        Assert.Equal("Watching: 7 of 28 days observed", watching.StateText);
        Assert.Equal(25, watching.ProgressPercent);
        Assert.True(watching.ShowProgress);

        var quiet = R(Entry(2, "/data/app", WatchState.QuietEligible) with { CoveredDays = 28, ValidUntilUtc = Now.AddDays(20) });
        Assert.StartsWith("Unused for 28 days", quiet.StateText);
        Assert.Equal(100, quiet.ProgressPercent);
        Assert.True(quiet.CanReassess);

        var alive = R(Entry(3, "/data/app", WatchState.Alive) with { AliveUntilUtc = Now.AddDays(5), LastActiveUtc = Now.AddDays(-2), LastActiveStrength = UseStrength.Strong, LastActiveDetail = "3 created" });
        Assert.StartsWith("In use", alive.StateText);
        Assert.Equal("Use 2 days ago: 3 created", alive.LastActivityText);
        Assert.False(alive.ShowProgress);

        var gaveUp = R(Entry(4, "/data/app", WatchState.Unobservable));
        Assert.True(gaveUp.CanReenroll);
        Assert.False(gaveUp.CanSnooze);

        var pinned = R(Entry(5, "/data/app", WatchState.Resolved) with { Pinned = true });
        Assert.Equal("Unpin", pinned.PinButtonText);
        Assert.StartsWith("Pinned", pinned.StateText);
        Assert.False(pinned.CanStop);

        var snoozed = R(Entry(6, "/data/app") with { SnoozeUntilUtc = Now.AddDays(10) });
        Assert.True(snoozed.IsSnoozed);
        Assert.Contains("snoozed until", snoozed.StateText);

        var assessed = R(Entry(7, "/data/app", WatchState.QuietEligible) with { LastTier = CleanupTier.Likely, LastScore = 0.834, LastReasons = "why" });
        Assert.Equal("Likely junk (score 83/100)", assessed.VerdictText);
        Assert.Equal("why", assessed.VerdictDetails);
        Assert.Equal("Not reassessed yet", watching.VerdictText);
        Assert.Equal("Nothing seen yet", watching.LastActivityText);
    }

    [Fact]
    public async Task TurningItOnTheFirstTime_ShowsExactlyWhatIsStored_AndNeedsAgreement()
    {
        var vm = Watchlist();

        _confirmations.Answers.Enqueue(false);
        await vm.EnableCommand.ExecuteAsync(null);
        Assert.False(_coordinator.IsEnabled);
        Assert.DoesNotContain("enabled=True", _coordinator.Calls);

        await vm.EnableCommand.ExecuteAsync(null);
        var prompt = _confirmations.Prompts[1];
        Assert.Equal("Turn on", prompt.ConfirmText);
        Assert.Contains("ledger.db", prompt.Message);                       // where
        Assert.Contains("never reads file contents", prompt.Message);       // what it does not do
        Assert.Contains("90 days", prompt.Message);                         // how long
        Assert.Contains("Nothing is deleted because of this", prompt.Message);
        Assert.True(_coordinator.IsEnabled);
        Assert.True(vm.IsEnabled);
        Assert.Contains("Observation is on", vm.StatusMessage);
    }

    [Fact]
    public async Task TurningItOnAgain_DoesNotAskAgain_AndTurningItOffNeedsNoQuestion()
    {
        _coordinator.Consent = true;
        var vm = Watchlist();

        await vm.EnableCommand.ExecuteAsync(null);
        await vm.DisableCommand.ExecuteAsync(null);

        Assert.Empty(_confirmations.Prompts);
        Assert.False(vm.IsEnabled);
        Assert.Contains("Observation is off", vm.StatusMessage);
    }

    [Fact]
    public async Task IfTheLedgerCanNotBeUsed_ItSaysSo_InsteadOfPretendingItIsOn()
    {
        _coordinator.Consent = true;
        var broken = new BrokenCoordinator();
        var vm = new WatchlistViewModel(broken, _confirmations, _explorer, _scheduler);

        await vm.EnableCommand.ExecuteAsync(null);

        Assert.False(vm.IsEnabled);
        Assert.Contains("not usable", vm.StatusMessage);
    }

    private sealed class BrokenCoordinator : Declutterer.Abstractions.IObservationCoordinator
    {
        private readonly ScriptedCoordinator _inner = new() { Consent = true, IsLedgerAvailable = false };
        public bool IsEnabled => false;
        public ObservationOptions Options => _inner.Options;
        public bool IsLedgerAvailable => false;
        public string LedgerPath => _inner.LedgerPath;
        public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);
        public Task<bool> HasConsentAsync(CancellationToken ct = default) => _inner.HasConsentAsync(ct);
        public Task SetEnabledAsync(bool enabled, CancellationToken ct = default) => Task.CompletedTask; // refuses
        public Task<EnrollmentResult> EnrollAsync(IReadOnlyList<ObservationCandidate> c, CancellationToken ct = default) => _inner.EnrollAsync(c, ct);
        public Task<CheckInRunResult> RunCheckInAsync(string t, CancellationToken ct = default) => _inner.RunCheckInAsync(t, ct);
        public Task StartupCatchUpAsync(CancellationToken ct = default) => _inner.StartupCatchUpAsync(ct);
        public Task<IReadOnlyList<WatchEntry>> GetWatchlistAsync(CancellationToken ct = default) => _inner.GetWatchlistAsync(ct);
        public Task<RunLogRecord?> GetLastRunAsync(CancellationToken ct = default) => _inner.GetLastRunAsync(ct);
        public Task<bool> StopWatchingAsync(long id, CancellationToken ct = default) => _inner.StopWatchingAsync(id, ct);
        public Task<bool> PinAsync(long id, bool pinned, CancellationToken ct = default) => _inner.PinAsync(id, pinned, ct);
        public Task<bool> SnoozeAsync(long id, CancellationToken ct = default) => _inner.SnoozeAsync(id, ct);
        public Task<bool> ReenrollAsync(long id, CancellationToken ct = default) => _inner.ReenrollAsync(id, ct);
        public Task<ReassessmentResult?> ReassessAsync(long id, CancellationToken ct = default) => _inner.ReassessAsync(id, ct);
        public Task ClearAllDataAsync(CancellationToken ct = default) => _inner.ClearAllDataAsync(ct);
    }

    [Fact]
    public async Task DeletingAllData_NeedsConfirmation()
    {
        _coordinator.Entries.Add(Entry(1, "/data/a"));
        var vm = Watchlist();
        await vm.LoadAsync();

        _confirmations.Answers.Enqueue(false);
        await vm.ClearDataCommand.ExecuteAsync(null);
        Assert.DoesNotContain("clear", _coordinator.Calls);
        Assert.True(vm.HasRows);

        await vm.ClearDataCommand.ExecuteAsync(null);
        Assert.Contains("clear", _coordinator.Calls);
        Assert.False(vm.HasRows);
        Assert.Contains("deleted", vm.StatusMessage);
    }

    [Fact]
    public async Task TheRowActions_CallTheCoordinatorWithTheRightEntry()
    {
        _coordinator.Entries.Add(Entry(7, "/data/app", WatchState.QuietEligible));
        _coordinator.ReassessResult = new ReassessmentResult(CleanupTier.Likely, 0.83, 0.2, "r",
            new JunkAssessment { JunkScore = 0.83, DeletionRisk = 0.2, Confidence = 0.7, Tier = CleanupTier.Likely, Reasons = Array.Empty<Signal>() });
        var vm = Watchlist();
        await vm.LoadAsync();
        var row = vm.Rows.Single();

        await vm.StopWatchingCommand.ExecuteAsync(row);
        await vm.PinCommand.ExecuteAsync(row);
        await vm.SnoozeCommand.ExecuteAsync(row);
        await vm.ReenrollCommand.ExecuteAsync(row);
        await vm.ReassessCommand.ExecuteAsync(row);
        vm.OpenInExplorerCommand.Execute(row);

        Assert.Equal(new[] { "stop:7", "pin:7", "snooze:7", "reenroll:7", "reassess:7" }, _coordinator.Calls.Where(c => c.Contains(':') && !c.StartsWith("checkin")).ToArray());
        Assert.Equal(new[] { "/data/app" }, _explorer.Opened.ToArray());
        Assert.Contains("Likely", vm.StatusMessage);
    }

    [Fact]
    public async Task ARowThatIsPinned_IsUnpinned_ByThePinButton()
    {
        _coordinator.Entries.Add(Entry(7, "/data/app", WatchState.Resolved) with { Pinned = true });
        var vm = Watchlist();
        await vm.LoadAsync();

        await vm.PinCommand.ExecuteAsync(vm.Rows.Single());

        Assert.Contains("unpin:7", _coordinator.Calls);
    }

    [Fact]
    public async Task RowActionsWithoutARow_DoNothing()
    {
        var vm = Watchlist();

        await vm.StopWatchingCommand.ExecuteAsync(null);
        await vm.PinCommand.ExecuteAsync(null);
        vm.OpenInExplorerCommand.Execute(null);

        Assert.Empty(_coordinator.Calls);
        Assert.Empty(_explorer.Opened);
    }

    [Fact]
    public async Task ReenrollThatFails_IsReported_AndAnExplorerFailureDoesNotThrow()
    {
        _coordinator.Entries.Add(Entry(7, "/data/app", WatchState.Unobservable));
        _coordinator.ReenrollSucceeds = false;
        var vm = Watchlist();
        await vm.LoadAsync();

        await vm.ReenrollCommand.ExecuteAsync(vm.Rows.Single());
        Assert.Contains("could not be fingerprinted", vm.StatusMessage);

        _explorer.Throws = new FileNotFoundException("gone");
        vm.OpenInExplorerCommand.Execute(vm.Rows.Single());
        Assert.Contains("Could not open", vm.StatusMessage);
    }

    [Fact]
    public async Task CheckNow_RunsAManualCheckIn_AndReportsTheOutcome()
    {
        _coordinator.CheckInResult = new CheckInRunResult(CheckInOutcome.Ok, 5, 2, 1, 0, 1, 1, 0, TimeSpan.Zero, null);
        var vm = Watchlist();

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("checkin:manual", _coordinator.Calls);
        Assert.Equal("Checked 5 item(s): 2 in use, 1 newly unused, 1 gone, 1 left for next time.", vm.StatusMessage);

        _coordinator.CheckInResult = CheckInRunResult.Of(CheckInOutcome.LockHeld);
        await vm.CheckNowCommand.ExecuteAsync(null);
        Assert.Contains("already running", vm.StatusMessage);
    }

    [Fact]
    public async Task TheDailyBackgroundCheckIn_IsShownInFullBeforeItIsAdded_AndCanBeRemoved()
    {
        var vm = Watchlist();

        _confirmations.Answers.Enqueue(false);
        await vm.InstallScheduleCommand.ExecuteAsync(null);
        Assert.Empty(_scheduler.Calls); // declined: nothing was created

        await vm.InstallScheduleCommand.ExecuteAsync(null);
        var prompt = _confirmations.Prompts[1];
        Assert.Contains("--observe", prompt.Message);       // the exact command
        Assert.Contains("Runs as you.", prompt.Message);    // and what it can do
        Assert.Equal("Add daily check-in", prompt.ConfirmText);
        Assert.Equal(new[] { "install" }, _scheduler.Calls.ToArray());
        Assert.True(vm.IsSchedulerInstalled);
        Assert.Contains("daily background check-in is set up", vm.SchedulerText);

        await vm.RemoveScheduleCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "install", "uninstall" }, _scheduler.Calls.ToArray());
        Assert.False(vm.IsSchedulerInstalled);
    }

    [Fact]
    public async Task ASchedulerThatCanNotWriteItsFiles_IsReported_NotThrown()
    {
        _scheduler.Throws = new UnauthorizedAccessException("read-only home folder");
        var vm = Watchlist();

        await vm.InstallScheduleCommand.ExecuteAsync(null);
        Assert.Contains("read-only home folder", vm.StatusMessage);

        vm.StatusMessage = string.Empty;
        await vm.RemoveScheduleCommand.ExecuteAsync(null);
        Assert.Contains("read-only home folder", vm.StatusMessage);
    }

    [Fact]
    public async Task AnUnsupportedPlatform_OffersNoSchedule()
    {
        _scheduler.IsSupported = false;
        var vm = Watchlist();

        await vm.LoadAsync();
        await vm.InstallScheduleCommand.ExecuteAsync(null);

        Assert.False(vm.IsSchedulerSupported);
        Assert.Empty(_confirmations.Prompts);
    }

    [Fact]
    public void Back_CallsTheHideCallback()
    {
        var vm = Watchlist();
        int hidden = 0;
        vm.SetHideCallback(() => hidden++);

        vm.BackCommand.Execute(null);

        Assert.Equal(1, hidden);
    }

    [Theory]
    [InlineData(CheckInOutcome.Ok, "Last check-in:")]
    [InlineData(CheckInOutcome.LockHeld, "already running")]
    [InlineData(CheckInOutcome.LedgerUnavailable, "could not be used")]
    [InlineData(CheckInOutcome.Disabled, "switched off")]
    [InlineData(CheckInOutcome.Error, "failed")]
    public void TheLastRun_IsDescribedPerOutcome(CheckInOutcome outcome, string expected)
    {
        Assert.Contains(expected, WatchlistViewModel.DescribeLastRun(new RunLogRecord(Now, Now, "x", outcome, 3, "d")));
        Assert.Equal("No check-in has run yet.", WatchlistViewModel.DescribeLastRun(null));
    }

    // ── the main window ──────────────────────────────────────────────────────────────────────

    private sealed class ScriptedSmartCleanup : ISmartCleanupService
    {
        public SmartCleanupResult Result { get; set; } = new(0, 0, 10, 0, 0, 0, false, TimeSpan.Zero);

        public Task<SmartCleanupResult> SelectAsync(IReadOnlyList<TreeNode> roots, SmartSelectMode mode, ScanOptions? scanOptions,
            Declutterer.Utilities.Helpers.ObservableHashSet<TreeNode> selectedNodes, CancellationToken cancellationToken = default,
            IProgress<SmartCleanupProgress>? progress = null, Func<bool>? isCurrentScope = null) => Task.FromResult(Result);
    }

    private (MainWindowViewModel Vm, ScriptedSmartCleanup Smart, WatchlistViewModel Watchlist) Main(bool withCoordinator = true)
    {
        var smart = new ScriptedSmartCleanup();
        var watchlist = Watchlist();
        var vm = new MainWindowViewModel(Substitute.For<INavigationService>(), Substitute.For<IScanWorkflowService>(), Substitute.For<ITreeNavigationService>(),
            Substitute.For<IContextMenuService>(), Substitute.For<ICommandService>(), Substitute.For<IClipboardService>(),
            Substitute.For<ISelectionManagementService>(), new HistoryWindowViewModel(), smart,
            withCoordinator ? _coordinator : null, withCoordinator ? watchlist : null, withCoordinator ? _confirmations : null);
        vm.Roots.Add(new TreeNode { Name = "Root", FullPath = "/root" });
        return (vm, smart, watchlist);
    }

    private static ObservationCandidate Candidate(string name, long size, ObservationClass cls = ObservationClass.AppData) =>
        new("/root/" + name, name, size, cls, CleanupTier.Likely, 0.8, 0.2, null,
            ObservationCandidateReason.Suspicious, new[] { ReasonCode.KnownCacheLocation });

    [Fact]
    public async Task SmartSelect_OffersWatchingTheCandidates_WhenThereAreSome()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100), Candidate("b", 200) } };

        await vm.SmartSelectCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.ObservationCandidateCount);
        Assert.True(vm.HasObservationCandidates);
        Assert.Equal("Watch 2 unselected folders", vm.ObservationCandidateText);
        Assert.Contains("junk score", vm.ObservationCandidateTooltip);
        Assert.Contains("tier Likely", vm.ObservationCandidateTooltip);
        Assert.Contains("deletion risk", vm.ObservationCandidateTooltip);

        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        Assert.Equal("Watch 1 unselected folder", vm.ObservationCandidateText);

        smart.Result = smart.Result with { ObservationCandidates = null };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        Assert.False(vm.HasObservationCandidates);
        Assert.Equal(string.Empty, vm.ObservationCandidateText);
    }

    [Fact]
    public async Task PartialDiscoveryIsShownOnTheButtonAndInTheSummary()
    {
        var (vm, smart, _) = Main();
        var coverage = new ObservationDiscoverySummary(
            FoldersExamined: 12, CandidatesFound: 1, CandidatesReturned: 1, AvailableWatchSlots: 200, ExcludedByCapacity: 0,
            ExcludedSelectedOverlap: 0, ExcludedAlreadyWatched: 0, ExcludedNested: 0, ExcludedUnsafeOrIncomplete: 0,
            ExcludedValuableOrSensitive: 0, ExcludedNotEligible: 8, HiddenFoldersSkipped: 1, FilteredFoldersSkipped: 0,
            ReparseFoldersSkipped: 0, SystemFoldersSkipped: 0, InaccessibleDirectories: 0, DepthLimitedDirectories: 1,
            EntryLimitedDirectories: 0, IsComplete: false, WasCancelled: false, PartialReason: "the configured directory depth limit was reached");
        smart.Result = smart.Result with
        {
            ObservationCandidates = new[] { Candidate("a", 100) },
            ObservationDiscovery = coverage,
        };

        await vm.SmartSelectCommand.ExecuteAsync(null);

        Assert.Equal("Watch 1 unselected folder (partial)", vm.ObservationCandidateText);
        Assert.Contains("Watch discovery partial", vm.SmartSelectSummary);
        Assert.Contains("1 hidden folder was excluded", vm.SmartSelectSummary);
    }

    [Fact]
    public async Task Watching_AsksFirst_ExplainsTheWindow_AndThenEnrolls()
    {
        var (vm, smart, watchlist) = Main();
        var candidates = new[] { Candidate("a", 3L * 1024 * 1024), Candidate("b", 1024 * 1024, ObservationClass.GenericFolder) };
        smart.Result = smart.Result with { ObservationCandidates = candidates };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        _coordinator.EnrollResult = new EnrollmentResult(2, 1, Array.Empty<EnrollmentSkip>());
        _coordinator.Entries.Add(Entry(1, "/root/a"));

        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        var prompt = Assert.Single(_confirmations.Prompts);
        Assert.Equal("Start watching", prompt.ConfirmText);
        Assert.Contains("Watch 2 folder(s) (4 MB in total)", prompt.Message);
        Assert.Contains("28 to 45 days", prompt.Message);
        Assert.Contains("Nothing is deleted", prompt.Message);
        Assert.Equal(new[] { "/root/a", "/root/b" }, _coordinator.EnrolledCandidates.Select(c => c.Path).ToArray());
        Assert.Contains("Watching 2 folder(s)", vm.SmartSelectSummary);
        Assert.Contains("1 of them were in use recently", vm.SmartSelectSummary);
        Assert.False(vm.HasObservationCandidates); // offered once
        Assert.Single(watchlist.Rows);              // and the watchlist knows already
        Assert.False(vm.IsAnyNodeLoading);
    }

    [Fact]
    public async Task EnrollmentRechecksSelection_AndSkipsCandidatesThatBecameSelected()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100), Candidate("b", 200) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        vm.SelectedNodes.Add(new TreeNode { Name = "a", FullPath = "/root/a", Size = 100 });
        _coordinator.EnrollResult = new EnrollmentResult(1, 0, Array.Empty<EnrollmentSkip>());

        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "/root/b" }, _coordinator.EnrolledCandidates.Select(c => c.Path).ToArray());
        Assert.Contains("Skipped 1", vm.SmartSelectSummary);
    }

    [Fact]
    public async Task ChangingSmartSelectModeClearsOldWatchSuggestions()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        Assert.True(vm.HasObservationCandidates);

        vm.SmartSelectMode = SmartSelectMode.Aggressive;

        Assert.False(vm.HasObservationCandidates);
        Assert.Empty(vm.ObservationCandidateTooltip);
    }

    [Fact]
    public async Task ChangingScanRootsClearsOldWatchSuggestions()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        Assert.True(vm.HasObservationCandidates);

        vm.Roots.Add(new TreeNode { Name = "Other", FullPath = "/other" });

        Assert.False(vm.HasObservationCandidates);
        Assert.Empty(vm.ObservationCandidateTooltip);
        Assert.Empty(vm.SmartSelectSummary);
    }

    [Fact]
    public async Task DecliningTheConfirmation_EnrollsNothing_AndKeepsTheOffer()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        _confirmations.Answers.Enqueue(false);

        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        Assert.DoesNotContain("enroll", _coordinator.Calls);
        Assert.True(vm.HasObservationCandidates);
    }

    [Fact]
    public async Task WithoutAWayToAsk_NothingIsEnrolled()
    {
        var smart = new ScriptedSmartCleanup();
        var vm = new MainWindowViewModel(Substitute.For<INavigationService>(), Substitute.For<IScanWorkflowService>(), Substitute.For<ITreeNavigationService>(),
            Substitute.For<IContextMenuService>(), Substitute.For<ICommandService>(), Substitute.For<IClipboardService>(),
            Substitute.For<ISelectionManagementService>(), new HistoryWindowViewModel(), smart, _coordinator, Watchlist(), null);
        vm.Roots.Add(new TreeNode { Name = "Root", FullPath = "/root" });
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);

        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        Assert.DoesNotContain("enroll", _coordinator.Calls);
        Assert.True(vm.HasObservationCandidates); // still on offer
    }

    [Fact]
    public async Task AFailureWhileEnrolling_IsReported_NotThrown()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);
        _coordinator.EnrollThrows = new InvalidOperationException("disk gone");

        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        Assert.Contains("disk gone", vm.SmartSelectSummary);
        Assert.False(vm.IsAnyNodeLoading);
        Assert.True(vm.HasObservationCandidates); // the user may try again
    }

    [Fact]
    public async Task WithoutObservationAvailable_TheCommandDoesNothing_AndNothingIsOffered()
    {
        var (vm, smart, _) = Main(withCoordinator: false);
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };

        await vm.SmartSelectCommand.ExecuteAsync(null);
        await vm.EnrollObservationCandidatesCommand.ExecuteAsync(null);

        Assert.Null(vm.WatchlistViewModel);
        Assert.Empty(_coordinator.Calls);
        Assert.Empty(_confirmations.Prompts);
    }

    [Fact]
    public async Task TheWatchlist_ReplacesTheMainViewInline_WithoutLosingTheScan_AndBackReturns()
    {
        var (vm, _, watchlist) = Main();
        _coordinator.Entries.Add(Entry(1, "/root/a"));
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.True(vm.IsMainViewVisible);

        await vm.ShowWatchlistInlineCommand.ExecuteAsync(null);

        Assert.True(vm.IsWatchlistVisible);
        Assert.False(vm.IsMainViewVisible);
        Assert.Contains(nameof(MainWindowViewModel.IsMainViewVisible), changes);
        Assert.Single(vm.Roots);          // the scan is still there
        Assert.Single(watchlist.Rows);    // and the list was refreshed on the way in

        watchlist.BackCommand.Execute(null);
        Assert.False(vm.IsWatchlistVisible);
        Assert.True(vm.IsMainViewVisible);
        Assert.Single(vm.Roots);
    }

    [Fact]
    public async Task TheViewsExcludeEachOther_AndHomeClearsTheOffer()
    {
        var (vm, smart, _) = Main();
        smart.Result = smart.Result with { ObservationCandidates = new[] { Candidate("a", 100) } };
        await vm.SmartSelectCommand.ExecuteAsync(null);

        await vm.ShowWatchlistInlineCommand.ExecuteAsync(null);
        vm.ShowHistoryInlineCommand.Execute(null); // the history clears the scan, like Home, and replaces the watchlist
        Assert.True(vm.IsHistoryVisible);
        Assert.False(vm.IsWatchlistVisible);
        Assert.False(vm.HasObservationCandidates);
        Assert.Empty(vm.Roots);

        await vm.ShowWatchlistInlineCommand.ExecuteAsync(null);
        Assert.False(vm.IsHistoryVisible);
        Assert.True(vm.IsWatchlistVisible);
    }

    [Fact]
    public void TheEnrollmentSummary_NamesWhatWasSkipped()
    {
        var text = MainWindowViewModel.DescribeEnrollment(new EnrollmentResult(1, 0, new[]
        {
            new EnrollmentSkip("/root/big", "too large"), new EnrollmentSkip("/root/gone", "no longer exists"),
            new EnrollmentSkip("/root/c", "limit"), new EnrollmentSkip("/root/d", "limit"),
        }));

        Assert.Contains("Watching 1 folder(s)", text);
        Assert.Contains("Skipped 4: big (too large); gone (no longer exists); c (limit); ...", text);
        Assert.Equal("Nothing was added to the watchlist. Skipped 1: observation is switched off.", MainWindowViewModel.DescribeEnrollment(EnrollmentResult.None("observation is switched off")));
    }
}
