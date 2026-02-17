using System;
using Avalonia.Controls;
using Avalonia.Input;
using Declutterer.Abstractions;
using Declutterer.Models;

namespace Declutterer.Services;

/// <summary>
/// A service responsible for handling common control interactions.
/// </summary>
public sealed class ControlInteractionService
{
    private double _lastPointerReleasedTime = 0; // For detecting double-clicks
    
    /// <summary>
    /// Initializes a pointer double-pressed handler on the given control. When a double-click is detected, it resolves the TreeNode under the pointer and invokes the provided callback.
    /// </summary>
    /// <param name="control">The control to attach the handler to</param>
    /// <param name="onNodeDoubleClick">Callback to invoke with the TreeNode that was double-clicked (or null if no node)</param>
    /// <param name="onBeforeActionCondition">Optional callback to check a condition before processing the double-click. If it evaluates to true the double-click action will be skipped.</param>
    public void InitializePointerDoublePressedHandler(Control control, Action<TreeNode?> onNodeDoubleClick, Func<bool>? onBeforeActionCondition = null)
    {
        control.PointerReleased += (_, args) =>
        {
            if(onBeforeActionCondition != null && onBeforeActionCondition())
                return;

            if(args.GetCurrentPoint(control).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
            {
                double currentTime = args.Timestamp;
                if (currentTime - _lastPointerReleasedTime < 300) // 300ms threshold for double-click
                {
                    onNodeDoubleClick(GetNodeFromPointerEvent(control, args));
                    return;
                }
                _lastPointerReleasedTime = currentTime;
            }
        };
    }
    
    /// <summary>
    /// Initializes a context menu handler on the given control. When the context menu is requested, it resolves the TreeNode under the pointer and updates the context menu bindings accordingly.
    /// </summary>
    /// <param name="control">The control to attach the handler to</param>
    /// <param name="contextMenuProvider">The context menu provider to use for creating and updating the context menu</param>
    public void InitializeContextMenuHandler(Control control, IContextMenuProvider contextMenuProvider)
    {
        // Create context menu using the factory to avoid duplication
        var contextMenu = ContextMenuFactory.CreateStandardContextMenu(contextMenuProvider);

        control.ContextRequested += (_, args) =>
        {
            var clickedNode = GetNodeFromPointerEvent(control, args);
            // Update context menu bindings dynamically with the current node
            ContextMenuFactory.UpdateContextMenuBindings(contextMenu, clickedNode);
        };

        control.ContextMenu = contextMenu;
    }
    
    private static TreeNode? GetNodeFromPointer(Control Control, Avalonia.Point point)
    {
        var visual = Control.InputHitTest(point) as Control;
        while (visual != null)
        {
            if (visual.DataContext is TreeNode node)
                return node;
            visual = visual.Parent as Control;
        }
        return null;
    }
    
    private static TreeNode? GetNodeFromPointerEvent(Control control, PointerReleasedEventArgs args)
    {
        var point = args.GetCurrentPoint(control).Position;
        return GetNodeFromPointer(control, point);
    }
    
    private static TreeNode? GetNodeFromPointerEvent(Control control, ContextRequestedEventArgs args)
    {
        if (!args.TryGetPosition(control, out var point))
            return null;
    
        return GetNodeFromPointer(control, point);
    }
}
