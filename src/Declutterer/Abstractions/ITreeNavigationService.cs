using System.Collections.Generic;
using System.Threading.Tasks;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface ITreeNavigationService
{
    /// <summary>
    /// Loads subdirectories and files for a given node when it's expanded.
    /// </summary>
    Task LoadChildrenForNodeAsync(TreeNode node, ScanOptions? currentScanOptions);

    /// <summary>
    /// Recursively toggles the expansion state of a node and all its descendants.
    /// Uses parallel processing with task batching to efficiently handle large directory trees.
    /// </summary>
    /// <param name="node">The node to process</param>
    /// <param name="shouldExpand">Whether to expand or collapse</param>
    /// <param name="isRoot">True if this is the root node of the Alt+Click (its toggle is handled by TreeDataGrid)</param>
    /// <param name="currentScanOptions">Current scan options to use when loading children</param>
    Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, bool isRoot = false, ScanOptions? currentScanOptions = null);
}
