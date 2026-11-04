using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Declutterer.Abstractions;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs an external program without a shell. Injected so scheduling can be tested without executing anything.</summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Exactly what installing the schedule would create, shown to the user before anything is done.</summary>
public sealed record ScheduleDescription(string Mechanism, string Summary, string Command, IReadOnlyList<string> Details);

public sealed record ScheduleStatus(bool Installed, string? Detail);

public sealed record ScheduleOperationResult(bool Success, string Message);

/// <summary>
/// Optional: a once-a-day background check-in (<c>Declutterer --observe</c>) registered with the operating system's own scheduler,
/// so the machine's "in use" days are recorded even when the program is not running. Never installed without an explicit user action;
/// always removable. It runs as the current user, without elevation, and only reads metadata.
/// </summary>
public interface IObservationScheduler
{
    bool IsSupported { get; }

    /// <summary>What would be created. Has no side effects.</summary>
    ScheduleDescription Describe();

    Task<ScheduleStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> InstallAsync(CancellationToken cancellationToken = default);

    Task<ScheduleOperationResult> UninstallAsync(CancellationToken cancellationToken = default);
}
