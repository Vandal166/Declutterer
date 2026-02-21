using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Domain.Services.Deletion;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public interface IDeleteService
{
    Task<DeleteResult> MoveToRecycleBinAsync(ObservableCollection<TreeNode> items, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DeleteResult> DeletePermanentlyAsync(ObservableCollection<TreeNode> items, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default);
}