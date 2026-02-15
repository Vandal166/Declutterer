using System;
using Avalonia.Controls;
using Declutterer.Factories;
using Declutterer.ViewModels;
using Declutterer.Services;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    private readonly TreeGridInteractionService _interactionService;
    
    public MainWindow(TreeGridInteractionService interactionService)
    {
        InitializeComponent();
        
        _interactionService = interactionService;
        
        // Set up the ViewModel with the TopLevel for folder picker
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(this);
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
            if (treeDataGrid != null)
            {
                var source = TreeDataGridSourceFactory.CreateTreeDataGridSource(viewModel, GetTopLevel(this)?.Bounds.Width);
                
                _interactionService.InitializeHandlers(treeDataGrid, source);
                
                treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
            }
        }
    }
}