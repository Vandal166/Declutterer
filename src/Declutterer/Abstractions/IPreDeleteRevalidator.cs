using System.Threading;
using System.Threading.Tasks;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public sealed record RevalidationResult(bool Allowed, string? Reason)
{
    public static readonly RevalidationResult Ok = new(true, null);

    public static RevalidationResult Blocked(string reason) => new(false, reason);
}

/// <summary>
/// The last look before something is deleted. For an item that has observation history it checks that nothing changed since the
/// evidence was gathered; items without history are always allowed (the existing path safety checks still run in the delete service).
/// </summary>
public interface IPreDeleteRevalidator
{
    Task<RevalidationResult> RevalidateAsync(TreeNode item, CancellationToken cancellationToken = default);

    /// <summary>Called after an item was deleted successfully: its watch entries are finished.</summary>
    Task MarkDeletedAsync(TreeNode item, CancellationToken cancellationToken = default);
}
