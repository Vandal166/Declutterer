using System;
using Avalonia.Controls;
using Declutterer.Abstractions;
using Declutterer.UI.Services.Interaction;
using Declutterer.UI.ViewModels;
using Declutterer.Utilities.Extensions;

namespace Declutterer.UI.Views;

public partial class CleanupWindow : Window
{
    private readonly IErrorDialogService _errorDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;

    public CleanupWindow(IErrorDialogService errorDialogService, IConfirmationDialogService confirmationDialogService)
    {
        InitializeComponent();
        _errorDialogService = errorDialogService;
        _confirmationDialogService = confirmationDialogService;
    }
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FitToScreen(0.65, 0.85);
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
       
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            // Set up the ViewModel with the TopLevel for clipboard access
            viewModel.SetTopLevel(this);
            
            viewModel.RequestClose += Close;
            
            // Init Shared interactions
            var controlInteractionService = new ControlInteractionService();
            
            // Initialize dialog services with this window as owner
            _errorDialogService.SetOwnerWindow(this);
            _confirmationDialogService.SetOwnerWindow(this);

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
    
    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        
        if (DataContext is CleanupWindowViewModel viewModel)
        {
            viewModel.OnWindowClosing();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        if(DataContext is CleanupWindowViewModel vm)
        {
            vm.RequestClose -= Close; // unsub
        }
        base.OnClosed(e);
    }
}