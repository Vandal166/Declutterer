using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Services.Observation;

namespace Declutterer.Abstractions;

/// <summary>Runs the full Smart Select engine on one watched item (fresh safety scan, fresh machine snapshot) and stores the verdict.</summary>
public interface IObservationReassessor
{
    /// <returns>The new verdict, or null if the item could not be assessed (gone, ledger unavailable, engine failed).</returns>
    Task<ReassessmentResult?> ReassessAsync(WatchEntry entry, CancellationToken cancellationToken = default);
}
