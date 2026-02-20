using System.Threading.Tasks;
using Avalonia.Controls;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface INavigationService
{
    void SetOwnerWindow(Window window);
    Task<ScanOptions?> ShowScanOptionsAsync();
    Task<DeleteResult?> ShowCleanupWindowAsync(ObservableHashSet<TreeNode> selectedNodes);
}