using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Services;


public sealed class CommandService : ICommandService
{
    private readonly SmartSelectionService _smartSelectionService;

    public CommandService(SmartSelectionService smartSelectionService)
    {
        _smartSelectionService = smartSelectionService ?? throw new ArgumentNullException(nameof(smartSelectionService));
    }

    public void SelectAllChildren(IEnumerable<TreeNode> roots)
    {
        var rootChildrenToSelect = roots.SelectMany(r => r.Children);
        foreach (var child in rootChildrenToSelect)
        {
            child.IsCheckboxSelected = true;
        }
    }

    public void DeselectAll(ObservableHashSet<TreeNode> selectedNodes)
    {
        foreach (var node in selectedNodes.ToList())
        {
            node.IsCheckboxSelected = false;
        }
        selectedNodes.Clear();
    }

    /// <inheritdoc/>
    public void SmartSelect(IEnumerable<TreeNode> roots, ScanOptions? scanOptions, ObservableHashSet<TreeNode> selectedNodes)
    {
        if (scanOptions is null)
            return;

        DeselectAll(selectedNodes);

        var scorerOptions = new ScorerOptions();
        foreach (var rootChild in roots)
        {
            var toSelect = _smartSelectionService.Select(rootChild, scanOptions, scorerOptions);
            foreach (var selectedNode in toSelect)
            {
                selectedNode.IsCheckboxSelected = true;
                selectedNodes.Add(selectedNode);
            }
        }
    }
}
