using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface IScanWorkflowService
{
    Task<bool> ExecuteScanAsync(ScanOptions scanOptions, ObservableCollection<TreeNode> roots);
    Task<bool> LoadChildrenParallelAsync(List<TreeNode> validRoots, ScanOptions? scanOptions);
}