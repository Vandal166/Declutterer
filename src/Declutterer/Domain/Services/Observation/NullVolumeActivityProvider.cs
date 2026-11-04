using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;

namespace Declutterer.Domain.Services.Observation;

/// <summary>No change journal: the days on which a check-in ran are the only heartbeat.</summary>
public sealed class NullVolumeActivityProvider : IVolumeActivityProvider
{
    public Task<VolumeActivityReport> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VolumeActivityReport(false, Array.Empty<DateTime>(), false, null));
}
