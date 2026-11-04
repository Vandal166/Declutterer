using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Abstractions;

/// <summary>
/// One independent source of evidence about whether an item is in use. Sources must only read metadata: they must not open the
/// watched files, because opening a file is itself an access.
/// </summary>
public interface IActivitySource
{
    ObservationSource Source { get; }

    Task<ActivityReport> ObserveAsync(WatchEntry entry, ObservationContext context, CancellationToken cancellationToken);
}
