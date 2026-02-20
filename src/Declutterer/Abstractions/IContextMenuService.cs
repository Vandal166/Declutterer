using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface IContextMenuService
{
    /// <summary>
    /// Toggles the selection state of a node.
    /// </summary>
    void ToggleNodeSelection(TreeNode? node);

    /// <summary>
    /// Opens the node's path in the Explorer.
    /// </summary>
    Task OpenInExplorerAsync(TreeNode? node);
}