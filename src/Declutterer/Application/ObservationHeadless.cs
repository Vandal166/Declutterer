using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Observation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Declutterer.Application;

/// <summary>
/// <c>Declutterer --observe</c>: one observation check-in without any user interface, for the operating system's scheduler.
/// Exit codes: 0 = ran (or the feature is off, so there was nothing to do), 1 = the check-in failed or the ledger is unusable,
/// 2 = another check-in (for example the running application's) holds the lock, so this run stood aside.
/// </summary>
public static class ObservationHeadless
{
    public const string Flag = "--observe";

    public const int Ok = 0;
    public const int Failed = 1;
    public const int LockHeld = 2;

    public static bool IsRequested(string[] args) => args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Entry point from Main: builds the Avalonia-free container and runs one check-in.</summary>
    public static int Run(string[] args)
    {
        try
        {
            using var provider = new ServiceCollection().AddObservationServices().BuildServiceProvider();
            return RunAsync(provider.GetRequiredService<IObservationCoordinator>(), "headless", CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The headless observation check-in failed");
            return Failed;
        }
    }

    public static async Task<int> RunAsync(IObservationCoordinator coordinator, string trigger, CancellationToken cancellationToken)
    {
        await coordinator.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!coordinator.IsEnabled)
            return Ok; // the user has not enabled observation (or turned it off): a scheduled task left behind does nothing

        var result = await coordinator.RunCheckInAsync(trigger, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            CheckInOutcome.Ok or CheckInOutcome.Disabled => Ok,
            CheckInOutcome.LockHeld => LockHeld,
            _ => Failed,
        };
    }
}
