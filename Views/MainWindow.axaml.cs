using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Declutterer.ViewModels;
using Declutterer.Models;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Set up the ViewModel with the TopLevel for folder picker
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetTopLevel(this);
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            // Find the TreeDataGrid and set up the hierarchical source
            var treeDataGrid = this.FindControl<TreeDataGrid>("TreeDataGrid");
            if (treeDataGrid != null)
            {
                // Create a hierarchical tree data grid source that knows about the Children collection
                var source = new HierarchicalTreeDataGridSource<TreeNode>(viewModel.Roots)
                {
                    Columns =
                    {
                        new HierarchicalExpanderColumn<TreeNode>(
                            new TextColumn<TreeNode, string>("Name", x => x.Name),
                            x => x.Children,
                            x => x.HasChildren,
                            x => x.IsExpanded),
                        new TextColumn<TreeNode, long>("Size", x => x.Size),
                        new TextColumn<TreeNode, System.DateTime?>("Last Modified", x => x.LastModified),
                        new TextColumn<TreeNode, string>("Path", x => x.FullPath),
                    }
                };
                
                // Subscribe to row expanding event to trigger lazy loading
                source.RowExpanding += async (sender, args) =>
                {
                    if (args.Row.Model is TreeNode node && node.IsDirectory && node.HasChildren)
                    {
                        if (node.Children.Count == 0)
                        {
                            // Load children and wait for completion
                            await viewModel.LoadChildrenForNodeAsync(node);
                        }
                        
                        // Pre-load grandchildren for any child directories that don't have their children loaded yet
                        foreach (var child in node.Children.Where(c => c.IsDirectory && c.HasChildren && c.Children.Count == 0))
                        {
                            _ = viewModel.LoadChildrenForNodeAsync(child);
                        }
                    }
                };
                
                source.RowCollapsing += (sender, args) =>
                {
                    if (args.Row.Model is TreeNode node)
                    {
                        node.IsExpanded = false;
                    }
                };
                
                treeDataGrid.Source = source;
            }
        }
    }
}