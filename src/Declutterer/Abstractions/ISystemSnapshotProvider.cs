using System.Threading;
using System.Threading.Tasks;

namespace Declutterer.Abstractions;

/// <summary>Gathers an <see cref="ISystemSnapshot"/> (registry, processes, recent files ...). OS specific.</summary>
public interface ISystemSnapshotProvider
{
    Task<ISystemSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
