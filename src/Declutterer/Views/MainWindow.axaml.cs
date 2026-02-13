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
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
   //TODO 3: add a context menu to the rows with options like "Open in Explorer", "Copy Path", "Delete",
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