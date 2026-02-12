using System.Linq;
using Avalonia.Controls;
using Declutterer.ViewModels;
using Declutterer.Models;
using Declutterer.Services;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    private readonly TreeGridInteractionService _interactionService;
    
    private double _lastPointerPressedTime = 0; // For detecting double-clicks on expanders
    
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
                var source = viewModel.CreateTreeDataGridSource(viewModel, GetTopLevel(this)?.Bounds.Width);
                
                // sub to row expanding event to trigger lazy loading
                source.RowExpanding += async (sender, args) =>
                {
                    if(_interactionService.IsExpandingAll) 
                        return; // Skip if we're already in the middle of an expand/collapse all operation triggered by Alt+Click
                    
                    if (args.Row.Model is { IsDirectory: true, HasChildren: true} node)
                    {
                        // if children not loaded yet then load them and skip loading children for root nodes since we already load them with children in the initial scan
                        // NOTE: this fixed the duplicate entries issue
                        if (node.Children.Count == 0 && node.Depth != 0) 
                        {
                            await viewModel.LoadChildrenForNodeAsync(node);
                        }
                       
                        // Pre-load grandchildren for any child directories that don't have their children loaded yet
                        foreach (var child in node.Children.Where(c => c is { IsDirectory: true, HasChildren: true, Children.Count: 0 }))
                        {
                            _ = viewModel.LoadChildrenForNodeAsync(child);
                        }
                        
                        // Subscribe to PropertyChanged for each child to detect IsSelected changes
                        foreach (var child in node.Children)
                        {
                            viewModel.SubscribeToNodeSelectionChanges(child);
                        }
                    }
                };
                
                // Subscribe to PropertyChanged for root nodes
                foreach (var root in viewModel.Roots)
                {
                    viewModel.SubscribeToNodeSelectionChanges(root);
                }
                
                source.RowCollapsing += (sender, args) =>
                {
                    if(_interactionService.IsExpandingAll)
                        return;
                    
                    if (args.Row.Model is TreeNode node) // setting IsExpanded to false on collapse for the node
                    {
                        node.IsExpanded = false;
                    }
                };
                
                // Handle pointer events to detect Alt+Click on expander
                treeDataGrid.PointerPressed += async (sender, args) =>
                {
                    if(_interactionService.IsExpandingAll)
                        return;
                    
                    if(args.GetCurrentPoint(treeDataGrid).Properties.IsLeftButtonPressed)
                    {
                        double currentTime = args.Timestamp;
                        if (currentTime - _lastPointerPressedTime < 300) // 300ms threshold for double-click
                        {
                            //TODO let the vm handle and open in explroer on double click
                            return;
                        }
                        _lastPointerPressedTime = currentTime;
                    }
                };
                
                _interactionService.InitializeHandler(treeDataGrid);

                
                treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
            }
        }
    }

    
}