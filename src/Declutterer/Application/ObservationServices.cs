using System.Runtime.InteropServices;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Observation;
using Declutterer.Integration.FileIdentity;
using Declutterer.Integration.Scheduling;
using Declutterer.Integration.SystemInfo;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Declutterer.Application;

/// <summary>
/// The observation feature's part of the container. It depends on nothing from Avalonia, so the scheduled headless run
/// (<c>Declutterer --observe</c>) can build exactly this and never start a UI.
/// </summary>
public static class ObservationServices
{
    /// <param name="ledgerPath">Where the ledger lives; null means the per-user default. Tests pass a temp path.</param>
    public static IServiceCollection AddObservationServices(this IServiceCollection services, string? ledgerPath = null)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ObservationOptions>();

        // storage
        services.AddSingleton<SqliteObservationLedger>(sp => new SqliteObservationLedger(ledgerPath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IObservationLedger>(sp => sp.GetRequiredService<SqliteObservationLedger>());
        services.AddSingleton<IOwnActivityRecorder>(sp => sp.GetRequiredService<SqliteObservationLedger>());

        // what the machine knows (also used by the Smart Select engine)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            services.AddSingleton<ISystemSnapshotProvider, WindowsSystemSnapshotProvider>();
        else
            services.AddSingleton<ISystemSnapshotProvider, DefaultSystemSnapshotProvider>();

        // the engine, with the observation rule configured from the shared options
        services.AddSingleton<JunkAssessmentEngine>(sp => new JunkAssessmentEngine(sp.GetRequiredService<ObservationOptions>()));

        // observation
        services.AddSingleton<FingerprintWalker>(sp => new FingerprintWalker(sp.GetRequiredService<TimeProvider>()));
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IFileIdentityProvider, WindowsFileIdentityProvider>();
        else
            services.AddSingleton<IFileIdentityProvider, PathOnlyFileIdentityProvider>();
        services.AddSingleton<IVolumeActivityProvider, NullVolumeActivityProvider>();

        services.AddSingleton<IObservationReassessor>(sp => new ObservationReassessor(
            sp.GetRequiredService<JunkAssessmentEngine>(), sp.GetRequiredService<ISystemSnapshotProvider>(), sp.GetRequiredService<IObservationLedger>(),
            sp.GetRequiredService<FingerprintWalker>(), sp.GetRequiredService<ObservationOptions>(), null, sp.GetRequiredService<TimeProvider>()));

        // the sources are listed explicitly: their order is the order of their reports, and no container ambiguity can drop one
        services.AddSingleton<ObservationCheckInService>(sp => new ObservationCheckInService(
            sp.GetRequiredService<IObservationLedger>(), sp.GetRequiredService<ISystemSnapshotProvider>(),
            new IActivitySource[]
            {
                new SnapshotDiffSource(sp.GetRequiredService<IFileIdentityProvider>()),
                new UsageArtifactSource(),
                new AccessTimeSource(),
            },
            sp.GetRequiredService<FingerprintWalker>(), sp.GetRequiredService<ObservationOptions>(), sp.GetRequiredService<IObservationReassessor>(),
            sp.GetRequiredService<IVolumeActivityProvider>(), sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IObservationCoordinator>(sp => new ObservationCoordinator(
            sp.GetRequiredService<IObservationLedger>(), sp.GetRequiredService<ObservationCheckInService>(), sp.GetRequiredService<ISystemSnapshotProvider>(),
            sp.GetRequiredService<FingerprintWalker>(), sp.GetRequiredService<IFileIdentityProvider>(), sp.GetRequiredService<ObservationOptions>(),
            sp.GetRequiredService<IObservationReassessor>(), sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IPreDeleteRevalidator>(sp => new PreDeleteRevalidator(
            sp.GetRequiredService<IObservationLedger>(), sp.GetRequiredService<FingerprintWalker>(), sp.GetRequiredService<ObservationOptions>(),
            null, sp.GetRequiredService<TimeProvider>()));

        // optional background scheduling (only ever installed by an explicit user action)
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IObservationScheduler>(sp => ObservationSchedulerFactory.Create(sp.GetRequiredService<IProcessRunner>()));

        return services;
    }
}
