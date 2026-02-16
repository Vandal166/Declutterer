using System;
using Avalonia.Controls;
using Declutterer.Services;
using Declutterer.ViewModels;

namespace Declutterer.Views;

public partial class CleanupWindow : Window
{
    public CleanupWindow()
    {
        InitializeComponent();
        
        // Set up the ViewModel with the TopLevel for folder picker
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(this);
        }
    }
    //TODO
    // Why TreeDataGrid is easy:
    // It's a single unified control - you attach events to ONE control
    // All data flows through it in a single structure
    //     Why CleanupWindow is hard:
    // You have nested ItemsControls creating dynamic containers at runtime
    //     Each item is buried inside multiple layers of dynamically created controls
    // There's no direct way to attach an event to "all items"
    // The Solution: Flatten the Structure Completely
    // Instead of having nested ItemsControl → ItemsControl, create a single flat ItemsControl that displays all items with visual grouping. This is exactly like how you'd structure a flat list with headers.
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            // Finding the ItemsControl that contains the groups (which contain the ListBoxes)
            var itemsControl = this.FindControl<ItemsControl>("GroupedItemsControl");

            ArgumentNullException.ThrowIfNull(itemsControl);

            // Init Shared interactions
            var controlInteractionService = new ControlInteractionService();

            controlInteractionService.InitializePointerDoublePressedHandler(
                itemsControl,
                node =>
                {
                    // Fire-and-forget the async operation; any errors are logged by the ViewModel
                    _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                });

            // Initialize right-click context menu
            controlInteractionService.InitializeContextMenuHandler(itemsControl, viewModel);
        }
    }
}