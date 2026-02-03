using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Data;
using Avalonia.Media;
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
                treeDataGrid.Source = new HierarchicalTreeDataGridSource<TreeNode>(viewModel.Roots)
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
            }
        }
    }
}