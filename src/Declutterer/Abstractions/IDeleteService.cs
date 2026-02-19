using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface IDeleteService
{
    Task<DeleteResult> DeleteItemsAsync(ObservableCollection<TreeNode> items, DeleteMode mode, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DeleteResult> MoveToRecycleBinAsync(ObservableCollection<TreeNode> items, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DeleteResult> DeletePermanentlyAsync(ObservableCollection<TreeNode> items, IProgress<DeleteProgress>? progress = null,
        CancellationToken cancellationToken = default);
}