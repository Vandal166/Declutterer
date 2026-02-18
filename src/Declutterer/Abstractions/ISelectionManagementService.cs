using System;
using System.Collections.Generic;
using System.ComponentModel;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Abstractions;

/// <summary>
/// Service responsible for managing tree node selection state and event subscriptions.
/// Handles complex selection logic including recursive operations and parent-child selection rules.
/// </summary>
public interface ISelectionManagementService : IDisposable
{
    /// <summary>
    /// Event raised when a node's IsCheckboxSelected property changes.
    /// Subscribers should handle the selection logic.
    /// </summary>
    event EventHandler<PropertyChangedEventArgs>? OnNodePropertyChanged;
    
    /// <summary>
    /// Subscribes to selection changes for a given TreeNode.
    /// Prevents duplicate subscriptions for the same node.
    /// </summary>
    void SubscribeToNodeSelectionChanges(TreeNode node);
    
    /// <summary>
    /// Unsubscribes from all node property changes and clears tracking.
    /// </summary>
    void UnsubscribeFromAllNodes();
    
    /// <summary>
    /// Handles a node's selection change, updating the SelectedNodes collection
    /// and managing child/descendant selection states.
    /// </summary>
    /// <param name="node">The node whose selection changed</param>
    /// <param name="selectedNodes">The collection of currently selected nodes</param>
    void HandleNodeSelectionChanged(TreeNode node, ObservableHashSet<TreeNode> selectedNodes);
}
