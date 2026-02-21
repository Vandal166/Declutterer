using System.Collections.Generic;
using Declutterer.Utilities.Helpers;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.Abstractions;

public interface ICommandService
{
    void SelectAllChildren(IEnumerable<TreeNode> roots);

    void DeselectAll(ObservableHashSet<TreeNode> selectedNodes);

    /// <summary>
    /// Performs smart selection on the root nodes based on the provided scan options.
    /// </summary>
    /// <param name="roots">The root nodes to perform smart selection on.</param>
    /// <param name="scanOptions">The scan options that were used for the scan. Required for smart selection.</param>
    /// <param name="selectedNodes">The collection to add selected nodes to.</param>
    void SmartSelect(IEnumerable<TreeNode> roots, ScanOptions? scanOptions, ObservableHashSet<TreeNode> selectedNodes);
}