using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public interface IScanWorkflowService
{
    Task<bool> ExecuteScanAsync(ScanOptions scanOptions, ObservableCollection<TreeNode> roots);
    Task<bool> LoadChildrenParallelAsync(List<TreeNode> validRoots, ScanOptions? scanOptions);
}