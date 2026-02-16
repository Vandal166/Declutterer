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
    
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            // Init Shared interactions
            var controlInteractionService = new ControlInteractionService();

            // Initialize handlers for Large Directories ListBox
            var largeDirectoriesListBox = this.FindControl<ListBox>("LargeDirectoriesListBox");
            if (largeDirectoriesListBox != null)
            {
                controlInteractionService.InitializePointerDoublePressedHandler(
                    largeDirectoriesListBox,
                    node =>
                    {
                        // Fire-and-forget the async operation; any errors are logged by the ViewModel
                        _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                    });

                // Initialize right-click context menu
                controlInteractionService.InitializeContextMenuHandler(largeDirectoriesListBox, viewModel);
            }

            // Initialize handlers for Large Files ListBox
            var largeFilesListBox = this.FindControl<ListBox>("LargeFilesListBox");
            if (largeFilesListBox != null)
            {
                controlInteractionService.InitializePointerDoublePressedHandler(
                    largeFilesListBox,
                    node =>
                    {
                        // Fire-and-forget the async operation; any errors are logged by the ViewModel
                        _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                    });

                // Initialize right-click context menu
                controlInteractionService.InitializeContextMenuHandler(largeFilesListBox, viewModel);
            }

            // Initialize handlers for Old Files ListBox
            var oldFilesListBox = this.FindControl<ListBox>("OldFilesListBox");
            if (oldFilesListBox != null)
            {
                controlInteractionService.InitializePointerDoublePressedHandler(
                    oldFilesListBox,
                    node =>
                    {
                        // Fire-and-forget the async operation; any errors are logged by the ViewModel
                        _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                    });

                // Initialize right-click context menu
                controlInteractionService.InitializeContextMenuHandler(oldFilesListBox, viewModel);
            }

            // Initialize handlers for Other Items ListBox
            var otherItemsListBox = this.FindControl<ListBox>("OtherItemsListBox");
            if (otherItemsListBox != null)
            {
                controlInteractionService.InitializePointerDoublePressedHandler(
                    otherItemsListBox,
                    node =>
                    {
                        // Fire-and-forget the async operation; any errors are logged by the ViewModel
                        _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                    });

                // Initialize right-click context menu
                controlInteractionService.InitializeContextMenuHandler(otherItemsListBox, viewModel);
            }
        }
    }
}