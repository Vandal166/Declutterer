using System.Threading.Tasks;
using Avalonia.Controls;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Utilities.Helpers;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public interface INavigationService
{
    void SetOwnerWindow(Window window);
    Task<ScanOptions?> ShowScanOptionsAsync();
    Task<DeleteResult?> ShowCleanupWindowAsync(ObservableHashSet<TreeNode> selectedNodes);
}