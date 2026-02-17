using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;

namespace Declutterer.Services;

/// <summary>
/// Service responsible for managing tree node selection state and event subscriptions.
/// Handles complex selection logic including recursive operations and parent-child selection rules.
/// </summary>
public sealed class SelectionManagementService : ISelectionManagementService
{
    private readonly HashSet<TreeNode> _subscribedNodes = new();
    private readonly Dictionary<TreeNode, PropertyChangedEventHandler> _nodePropertyHandlers = new();
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates

    /// <summary>
    /// Subscribes to selection changes for a given TreeNode.
    /// Prevents duplicate subscriptions for the same node.
    /// </summary>
    public void SubscribeToNodeSelectionChanges(TreeNode node)
    {
        if (!_subscribedNodes.Add(node))
            return; // preventing another subscription for the same node, example after collapsing/expanding which can trigger multiple PropertyChanged events for the same node
        
        // Create and store the handler so we can unsubscribe later
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.IsCheckboxSelected))
            {
                // Selection change will be handled by the caller who has access to SelectedNodes
                OnNodePropertyChanged?.Invoke(node, args);
            }
        };
        
        _nodePropertyHandlers[node] = handler;
        node.PropertyChanged += handler;
    }
    
    /// <summary>
    /// Unsubscribes from all node property changes and clears tracking.
    /// </summary>
    public void UnsubscribeFromAllNodes()
    {
        foreach (var kvp in _nodePropertyHandlers)
        {
            kvp.Key.PropertyChanged -= kvp.Value;
        }
        _nodePropertyHandlers.Clear();
        _subscribedNodes.Clear();
    }
    
    /// <summary>
    /// Event raised when a node's IsCheckboxSelected property changes.
    /// Subscribers should handle the selection logic.
    /// </summary>
    public event EventHandler<PropertyChangedEventArgs>? OnNodePropertyChanged;
    
    /// <summary>
    /// Handles a node's selection change, updating the SelectedNodes collection
    /// and managing child/descendant selection states.
    /// Called whenever a TreeNode's IsSelected property changes.
    /// </summary>
    public void HandleNodeSelectionChanged(TreeNode node, ObservableHashSet<TreeNode> selectedNodes)
    {
        // Prevent re-entrancy when we're programmatically updating children
        if (_isUpdatingSelection)
            return;

        // Update SelectedNodes collection
        if (node.IsCheckboxSelected)
        {
            selectedNodes.Add(node);
            
            // removing all descendants from SelectedNodes since parent selection encompasses them
            RemoveDescendantsFromSelectedNodes(node, selectedNodes);
            
            // But still update children's IsSelected visual state(they are gonna have their checkboxes disabled)
            if (node.Children.Count > 0)
            {
                SetIsSelectedRecursively(node.Children, true, selectedNodes);
            }
        }
        else
        {
            // Remove the parent node
            selectedNodes.Remove(node);
            
            // When deselecting a parent, also deselect all children
            if (node.Children.Count > 0)
            {
                SetIsSelectedRecursively(node.Children, false, selectedNodes);
            }
        }
        
        // Update IsEnabled state for all children since parent's selection changed
        UpdateChildrenEnabledState(node);
    }

    /// <summary>
    /// Recursively removes all descendants of a node from the SelectedNodes collection.
    /// Used when a parent is selected to prevent double-counting children.
    /// </summary>
    private static void RemoveDescendantsFromSelectedNodes(TreeNode node, ObservableHashSet<TreeNode> selectedNodes)
    {
        foreach (var child in node.Children)
        {
            selectedNodes.Remove(child);
            if (child.Children.Count > 0)
            {
                RemoveDescendantsFromSelectedNodes(child, selectedNodes);
            }
        }
    }
    
    /// <summary>
    /// Recursively sets the IsSelected property on all nodes in the collection and their descendants.
    /// </summary>
    private void SetIsSelectedRecursively(ObservableCollection<TreeNode> nodes, bool isSelected, ObservableHashSet<TreeNode> selectedNodes)
    {
        _isUpdatingSelection = true;
        try
        {
            SetIsSelectedRecursivelyInternal(nodes, isSelected, selectedNodes);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
    
    private static void SetIsSelectedRecursivelyInternal(ObservableCollection<TreeNode> nodes, bool isSelected, ObservableHashSet<TreeNode> selectedNodes)
    {
        foreach (var child in nodes)
        {
            
            // Only update if the value is different to avoid unnecessary property change notifications
            if (child.IsCheckboxSelected != isSelected)
            {
                child.IsCheckboxSelected = isSelected;
                
                // Only update the SelectedNodes collection when deselecting
                // When selecting, we don't add children since parent selection should be enough
                if (!isSelected)
                {
                    selectedNodes.Remove(child);
                }
            }
            
            // Recursively update all already-loaded children
            // Note: Newly loaded children will inherit the IsSelected state from their parent
            // when they are loaded via LoadChildrenAsync in DirectoryScanService
            if (child.Children.Count > 0)
            {
                SetIsSelectedRecursivelyInternal(child.Children, isSelected, selectedNodes);
            }
        }
    }

    /// <summary>
    /// Recursively updates the IsEnabled state for all descendants.
    /// Children are disabled if they have any ancestor that is selected.
    /// </summary>
    private static void UpdateChildrenEnabledState(TreeNode node)
    {
        foreach (var child in node.Children)
        {
            child.IsCheckboxEnabled = !IsAnyAncestorSelected(child);
            // Recursively update grandchildren
            UpdateChildrenEnabledState(child);
        }
    }

    private static bool IsAnyAncestorSelected(TreeNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.IsCheckboxSelected)
                return true;
            current = current.Parent;
        }
        return false;
    }
    
    private bool _disposed = false;
    
    public void Dispose()
    {
        if (_disposed)
            return;
            
        UnsubscribeFromAllNodes();
        _disposed = true;
    }
}
