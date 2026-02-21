using Avalonia.Controls;
using Declutterer.Abstractions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Factories;

public static class ContextMenuFactory
{
    //TODO : expand this for more modularity, since not all contexts will need either "Select" or "Open in Explorer" options
    
    /// <summary>
    /// Creates a context menu with standard tree/list item options: Select, Open in Explorer, Copy Path.
    /// </summary>
    /// <param name="contextMenuProvider">The ViewModel that provides context menu commands</param>
    /// <returns>A configured ContextMenu ready to be assigned to a control</returns>
    public static ContextMenu CreateStandardContextMenu(IContextMenuProvider contextMenuProvider)
    {
        var contextMenu = new ContextMenu
        {
            Padding = new Avalonia.Thickness(4),
        };

        var selectMenuItem = new MenuItem
        {
            Header = "Select",
            Padding = new Avalonia.Thickness(8, 6),
        };

        var openMenuItem = new MenuItem
        {
            Header = "Open in Explorer",
            Padding = new Avalonia.Thickness(8, 6),
        };

        var copyPathMenuItem = new MenuItem
        {
            Header = "Copy Path",
            Padding = new Avalonia.Thickness(8, 6),
        };

        contextMenu.Items.Add(selectMenuItem);
        contextMenu.Items.Add(openMenuItem);
        contextMenu.Items.Add(copyPathMenuItem);

        // storing references to menu items and provider in the Tag for later command binding updates
        contextMenu.Tag = new MenuItemsContainer
        {
            SelectMenuItem = selectMenuItem,
            OpenMenuItem = openMenuItem,
            CopyPathMenuItem = copyPathMenuItem,
            ContextMenuProvider = contextMenuProvider
        };

        return contextMenu;
    }

    /// <summary>
    /// Updates the context menu with the appropriate commands and command parameters for the clicked item.
    /// </summary>
    /// <param name="contextMenu">The context menu to update</param>
    /// <param name="contextItem">The item that was right-clicked</param>
    public static void UpdateContextMenuBindings(ContextMenu contextMenu, TreeNode? contextItem)
    {
        if (contextItem is null || contextMenu.Tag is not MenuItemsContainer container)
            return;

        var provider = container.ContextMenuProvider;

        // Update menu item commands and parameters with the current item
        container.SelectMenuItem.Command = provider.ContextMenuSelectCommand;
        container.SelectMenuItem.CommandParameter = contextItem;

        container.OpenMenuItem.Command = provider.ContextMenuOpenInExplorerCommand;
        container.OpenMenuItem.CommandParameter = contextItem;

        container.CopyPathMenuItem.Command = provider.ContextMenuCopyPathCommand;
        container.CopyPathMenuItem.CommandParameter = contextItem;
    }

    /// <summary>
    /// Internal container for storing references to menu items for later binding updates.
    /// </summary>
    private sealed class MenuItemsContainer
    {
        public MenuItem SelectMenuItem { get; init; } = null!;
        public MenuItem OpenMenuItem { get; init; } = null!;
        public MenuItem CopyPathMenuItem { get; init; } = null!;
        public IContextMenuProvider ContextMenuProvider { get; init; } = null!;
    }
}

