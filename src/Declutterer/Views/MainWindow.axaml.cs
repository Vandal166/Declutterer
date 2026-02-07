using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Declutterer.ViewModels;
using Declutterer.Models;
using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Input;

namespace Declutterer.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingSelection = false; // Guard against re-entrancy during recursive selection updates
    
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
   //TODO overload the sorting for the columns? Since sometiems they sort wrong
   
   //TODO 2: alt + left click on Expander to expand ALL children recursively (can be done by checking if Alt key is pressed in RowExpanding event and then setting IsExpanded to true for all descendants)
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
                        new TemplateColumn<TreeNode>("Select", 
                            new FuncDataTemplate<TreeNode>((node, _) =>
                            {
                                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                                if(node is null)
                                    return null;
                                
                                // Don't create checkbox for root nodes (Depth == 0)
                                if (node.Depth == 0)
                                {
                                    node.IsExpanded = true; // auto expanded root node
                                    return new Control();
                                }
                                
                                var checkBox = new CheckBox
                                {
                                    IsChecked = node.IsSelected
                                };
                                
                                // Sync from CheckBox to TreeNode
                                checkBox.IsCheckedChanged += (s, e) =>
                                {
                                    if (checkBox.IsChecked.HasValue)
                                    {
                                        node.IsSelected = checkBox.IsChecked.Value;
                                        if(node.IsSelected)
                                            viewModel.SelectedNodes.Add(node);
                                        else
                                            viewModel.SelectedNodes.Remove(node);
                                    }
                                };
                                
                                // Sync from TreeNode to CheckBox
                                node.PropertyChanged += (s, e) =>
                                {
                                    if (e.PropertyName == nameof(TreeNode.IsSelected))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[CheckBox sync] Node '{node.Name}' IsSelected changed to {node.IsSelected}");
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        {
                                            checkBox.IsChecked = node.IsSelected;
                                            checkBox.InvalidateVisual();
                                            System.Diagnostics.Debug.WriteLine($"[CheckBox sync] Set checkBox.IsChecked to {checkBox.IsChecked} for '{node.Name}'");
                                        }, Avalonia.Threading.DispatcherPriority.Render);
                                    }
                                };
                                
                                return checkBox;
                            }, supportsRecycling: false), // Disable recycling to ensure each node has its own CheckBox
                            options: new TemplateColumnOptions<TreeNode>
                            {
                                CanUserResizeColumn = false,
                                CanUserSortColumn = false,
                            }),
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
                        
                        // Subscribe to PropertyChanged for each child to detect IsSelected changes
                        foreach (var child in node.Children)
                        {
                            SubscribeToNodeSelectionChanges(child);
                        }
                    }
                };
                
                // Subscribe to PropertyChanged for root nodes
                foreach (var root in viewModel.Roots)
                {
                    SubscribeToNodeSelectionChanges(root);
                }
                
                source.RowCollapsing += (sender, args) =>
                {
                    if (args.Row.Model is TreeNode node) // setting IsExpanded to false on collapse for the node
                    {
                        node.IsExpanded = false;
                    }
                };
                
                // Handle pointer events to detect Alt+Click on expander
                treeDataGrid.PointerPressed += async (sender, args) =>
                {
                    // Check if Alt key is pressed and it's a left click
                    if ((args.KeyModifiers & KeyModifiers.Alt) == KeyModifiers.Alt && args.GetCurrentPoint(treeDataGrid).Properties.IsLeftButtonPressed)
                    {
                        // Try to find if the click was on an expander by checking the visual tree
                        var point = args.GetCurrentPoint(treeDataGrid);
                        var visual = treeDataGrid.InputHitTest(point.Position) as Control;
                        
                        // Walk up the visual tree to find the row being clicked
                        while (visual != null)
                        {
                            if (visual is DataGridRow row && row.DataContext is TreeNode node)
                            {
                                // Check if node has children (has an expander)
                                if (node.HasChildren)
                                {
                                    // Toggle the expansion state recursively
                                    bool shouldExpand = !node.IsExpanded;
                                    await ToggleAllDescendantsAsync(node, shouldExpand, viewModel);
                                    args.Handled = true;
                                }
                                break;
                            }
                            visual = visual.Parent as Control;
                        }
                    }
                };
                
                treeDataGrid.Source = source; // assigning the source to the TreeDataGrid so that it can display the data
            }
        }
    }
    
    /// <summary>
    /// Subscribes to PropertyChanged events on a TreeNode to detect IsSelected changes.
    /// </summary>
    private void SubscribeToNodeSelectionChanges(TreeNode node)
    {
        node.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(TreeNode.IsSelected))
            {
                OnTreeNodeSelectionChanged(node);
            }
        };
    }
    
    /// <summary>
    /// Called whenever a TreeNode's IsSelected property changes.
    /// Modify this method to add your custom logic.
    /// </summary>
    private void OnTreeNodeSelectionChanged(TreeNode node)
    {
        // Prevent re-entrancy when we're programmatically updating children
        if (_isUpdatingSelection)
            return;
            
        System.Diagnostics.Debug.WriteLine($"Node '{node.Name}' selection changed to: {node.IsSelected}");

        // Only propagate to currently-loaded children
        // Newly-loaded children will inherit the IsSelected state from their parent via DirectoryScanService
        if (node.Children.Count == 0)
            return; // No children to update
        
        // Recursively set IsSelected on all currently-loaded descendants
        SetIsSelectedRecursively(node.Children, node.IsSelected);
    }
    
    /// <summary>
    /// Recursively sets the IsSelected property on all nodes in the collection and their descendants.
    /// </summary>
    private void SetIsSelectedRecursively(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, bool isSelected)
    {
        _isUpdatingSelection = true;
        try
        {
            SetIsSelectedRecursivelyInternal(nodes, isSelected);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }
    
    private void SetIsSelectedRecursivelyInternal(System.Collections.ObjectModel.ObservableCollection<TreeNode> nodes, bool isSelected)
    {
        foreach (var child in nodes)
        {
            // Only update if the value is different to avoid unnecessary property change notifications
            if (child.IsSelected != isSelected)
            {
                child.IsSelected = isSelected;
            }
            
            // Recursively update all already-loaded children
            // Note: Newly loaded children will inherit the IsSelected state from their parent
            // when they are loaded via LoadChildrenAsync in DirectoryScanService
            if (child.Children.Count > 0)
            {
                SetIsSelectedRecursivelyInternal(child.Children, isSelected);
            }
        }
    }
    
    /// <summary>
    /// Recursively toggles the expansion state of a node and all its descendants.
    /// </summary>
    private async Task ToggleAllDescendantsAsync(TreeNode node, bool shouldExpand, MainWindowViewModel viewModel)
    {
        // Load children if not already loaded
        if (node.Children.Count == 0 && node.IsDirectory && node.HasChildren)
        {
            await viewModel.LoadChildrenForNodeAsync(node);
        }
        
        // Set expansion state for current node
        node.IsExpanded = shouldExpand;
        
        if (shouldExpand)
        {
            // Recursively expand all descendants
            foreach (var child in node.Children)
            {
                if (child.IsDirectory && child.HasChildren)
                {
                    await ToggleAllDescendantsAsync(child, true, viewModel);
                }
            }
        }
        else
        {
            // Recursively collapse all descendants
            foreach (var child in node.Children)
            {
                if (child.IsDirectory && child.HasChildren)
                {
                    await ToggleAllDescendantsAsync(child, false, viewModel);
                }
            }
        }
    }
}