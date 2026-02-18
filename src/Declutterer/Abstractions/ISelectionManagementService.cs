using System;
using System.ComponentModel;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Abstractions;

public interface ISelectionManagementService : IDisposable
{
    /// <summary>
    /// Event raised when a node's IsCheckboxSelected property changes.
    /// Subscribers should handle the selection logic.
    /// </summary>
    event EventHandler<PropertyChangedEventArgs>? OnNodePropertyChanged;
    
    void SubscribeToNodeSelectionChanges(TreeNode node);
    
    void UnsubscribeFromAllNodes();
    
    /// <summary>
    /// Handles a node's selection change, updating the SelectedNodes collection
    /// and managing child/descendant selection states.
    /// </summary>
    /// <param name="node">The node whose selection changed</param>
    /// <param name="selectedNodes">The collection of currently selected nodes</param>
    void HandleNodeSelectionChanged(TreeNode node, ObservableHashSet<TreeNode> selectedNodes);
}
