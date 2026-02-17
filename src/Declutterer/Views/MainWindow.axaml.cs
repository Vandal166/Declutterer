using System;
using Avalonia.Controls;
using Declutterer.Abstractions;
using Declutterer.Factories;
using Declutterer.ViewModels;
using Declutterer.Services;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    private readonly TreeGridInteractionService _interactionService;
    
    public MainWindow(TreeGridInteractionService interactionService, INavigationService navigationService, IClipboardService clipboardService)
    {
        InitializeComponent();
        
        _interactionService = interactionService;

        navigationService.SetOwnerWindow(this);
        
        // Initialize the clipboard service with the window's clipboard
        if (clipboardService is AvaloniaClipboardService avaloniaClipboardService)
        {
            avaloniaClipboardService.SetClipboard(this.Clipboard);
        }
        
        // Clean up resources when window is closed
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Potential issue with singleton ViewModel disposal: The MainWindowViewModel is registered as a singleton in DI, 
        // which means it lives for the entire application lifetime. However, the Dispose() method is called when the window is closed. 
        // If the application closes the window but doesn't exit (e.g., minimizes to tray or opens a new window), the ViewModel would be 
        // in a disposed state with all event handlers unsubscribed. Any subsequent use would fail silently or cause errors.
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Dispose(); // since we dont allow the app to be in tray or open multiple windows, this should be fine
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            // finding TreeDataGrid control and setting up the hierarchical data source for it
            var treeDataGrid = this.FindControl<TreeDataGrid>("TreeDataGrid");
            
            ArgumentNullException.ThrowIfNull(treeDataGrid);
            
            var source = TreeDataGridSourceFactory.CreateTreeDataGridSource(viewModel, GetTopLevel(this)?.Bounds.Width);
            
            _interactionService.InitializeHandlers(treeDataGrid, source); // TreeDataGrid specific interactions
            
            _interactionService.InitializePointerDoublePressedHandler(
                treeDataGrid,
                node =>
                {
                    // Fire-and-forget the async operation; any errors are logged by the ViewModel
                    _ = viewModel.ContextMenuOpenInExplorerCommand.ExecuteAsync(node);
                },
                () =>  viewModel.IsExpandingAll);
            
            // Init Shared interactions
            var controlInteractionService = new ControlInteractionService();
            // Initialize right-click context menu
            controlInteractionService.InitializeContextMenuHandler(treeDataGrid, viewModel);
            
            treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
        }
    }
}