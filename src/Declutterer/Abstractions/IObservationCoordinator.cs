using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Abstractions;

public sealed record EnrollmentSkip(string Path, string Reason);

public sealed record EnrollmentResult(int Enrolled, int AliveOnEnrollment, IReadOnlyList<EnrollmentSkip> Skipped)
{
    public static EnrollmentResult None(string reason) => new(0, 0, new[] { new EnrollmentSkip(string.Empty, reason) });
}

/// <summary>
/// The one place the UI (and the scheduled headless run) talks to for observation: settings and consent, enrolling candidates,
/// running check-ins and acting on watchlist rows. Every call is fail-soft; none throws for ledger or file system trouble.
/// </summary>
public interface IObservationCoordinator
{
    bool IsEnabled { get; }

    /// <summary>The tunables, for texts that quote them (how many days a folder has to stay quiet).</summary>
    ObservationOptions Options { get; }
    bool IsLedgerAvailable { get; }
    string LedgerPath { get; }

    /// <summary>Reads the persisted on/off setting. Call once at startup.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>True once the user has seen what is stored and agreed.</summary>
    Task<bool> HasConsentAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns the feature on or off and persists it. Turning it on records the consent.</summary>
    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<EnrollmentResult> EnrollAsync(IReadOnlyList<ObservationCandidate> candidates, CancellationToken cancellationToken = default);

    Task<CheckInRunResult> RunCheckInAsync(string trigger, CancellationToken cancellationToken = default);

    /// <summary>At application start: load the setting and, if the feature is on, run the check-in the machine missed while it was off.</summary>
    Task StartupCatchUpAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchEntry>> GetWatchlistAsync(CancellationToken cancellationToken = default);

    Task<RunLogRecord?> GetLastRunAsync(CancellationToken cancellationToken = default);

    Task<bool> StopWatchingAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> PinAsync(long id, bool pinned, CancellationToken cancellationToken = default);

    Task<bool> SnoozeAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Gives an item that was given up on (Unobservable, Retired) a fresh baseline and starts watching it again.</summary>
    Task<bool> ReenrollAsync(long id, CancellationToken cancellationToken = default);

    Task<ReassessmentResult?> ReassessAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Deletes everything the feature stored.</summary>
    Task ClearAllDataAsync(CancellationToken cancellationToken = default);
}
