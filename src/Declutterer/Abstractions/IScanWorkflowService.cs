using System.Collections.Generic;
using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface IScanWorkflowService
{
    Task<bool> ExecuteScanAsync(ScanOptions scanOptions, List<TreeNode> roots);
    Task<bool> LoadChildrenParallelAsync(List<TreeNode> validRoots, ScanOptions? scanOptions);
}