using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Declutterer.ViewModels;
using Declutterer.Models;
using System;
using Avalonia.Media;

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
            // finding TreeDataGrid control and setting up the hierarchical data source for it
            var treeDataGrid = this.FindControl<TreeDataGrid>("TreeDataGrid");
            if (treeDataGrid != null)
            {
                var source = new HierarchicalTreeDataGridSource<TreeNode>(viewModel.Roots)
                {
                    Columns =
                    {
                        new HierarchicalExpanderColumn<TreeNode>(
                            new TextColumn<TreeNode, string>("Name", x => x.Name, options: new TextColumnOptions<TreeNode>
                            {
                                TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                                CanUserResizeColumn = true,
                                MaxWidth = new GridLength(400)
                            }),
                            x => x.Children,
                            x => x.HasChildren,
                            x => x.IsExpanded),
                        new TextColumn<TreeNode, string>("Size", x => x.SizeFormatted,
                            options: new TextColumnOptions<TreeNode>
                            {
                                TextAlignment = TextAlignment.Right,
                            }),

                        new TextColumn<TreeNode, DateTime?>("Last Modified", x => x.LastModified),
                        new TextColumn<TreeNode, string>("Path", x => x.FullPath, options: new TextColumnOptions<TreeNode>
                        {
                            TextTrimming = TextTrimming.PathSegmentEllipsis,
                            // MaxWidth = new GridLength((GetTopLevel(this)?.Bounds.Width ?? 900) / 3),
                            // CanUserResizeColumn = true,
                            // CanUserSortColumn = true
                        }),
                    }
                };
                
                // sub to row expanding event to trigger lazy loading
                source.RowExpanding += async (sender, args) =>
                {
                    if (args.Row.Model is { IsDirectory: true, HasChildren: true } node)
                    {
                        if (node.Children.Count == 0) // if children not loaded yet then load them
                        {
                            await viewModel.LoadChildrenForNodeAsync(node);
                        }
                        
                        // Pre-load grandchildren for any child directories that don't have their children loaded yet
                        foreach (var child in node.Children.Where(c => c is { IsDirectory: true, HasChildren: true, Children.Count: 0 }))
                        {
                            _ = viewModel.LoadChildrenForNodeAsync(child);
                        }
                    }
                };
                
                source.RowCollapsing += (sender, args) =>
                {
                    if (args.Row.Model is TreeNode node) // setting IsExpanded to false on collapse for the node
                    {
                        node.IsExpanded = false;
                    }
                };
                
                treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
            }
        }
    }
}