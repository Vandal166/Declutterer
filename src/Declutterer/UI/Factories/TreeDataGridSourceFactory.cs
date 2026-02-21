using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Declutterer.UI.Behaviours;
using Declutterer.UI.Converters;
using MainWindowViewModel = Declutterer.UI.ViewModels.MainWindowViewModel;
using TreeNode = Declutterer.Domain.Models.TreeNode;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace Declutterer.UI.Factories;

public static class TreeDataGridSourceFactory
{
    /// <summary>
    /// Builds a HierarchicalTreeDataGridSource for the TreeDataGrid control, with columns for selection, name, size, last modified date, and path.
    /// </summary>
    /// <returns>The fully configured HierarchicalTreeDataGridSource instance ready to be set as the ItemsSource for a TreeDataGrid.</returns>
    public static HierarchicalTreeDataGridSource<TreeNode> CreateTreeDataGridSource(MainWindowViewModel viewModel, double? viewWidth = null)
    {
        return new HierarchicalTreeDataGridSource<TreeNode>(viewModel.Roots)
        {
            Columns =
            {
                new TemplateColumn<TreeNode>("Select", 
                    new FuncDataTemplate<TreeNode>((node, _) =>
                    {
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
                            HorizontalAlignment = HorizontalAlignment.Center,
                        };
                        
                        using var __ = CheckBoxSelectionBehavior.AttachToNode(checkBox, node, viewModel);
                                
                        return checkBox;
                    }, supportsRecycling: false), // Disable recycling to ensure each node has its own CheckBox
                    options: new TemplateColumnOptions<TreeNode>
                    {
                        CanUserResizeColumn = false,
                        CanUserSortColumn = false,
                    }),
                new HierarchicalExpanderColumn<TreeNode>(
                    new TemplateColumn<TreeNode>("Name",
                        new FuncDataTemplate<TreeNode>((node, _) =>
                        {
                            if (node is null)
                                return null;

                            var panel = new StackPanel 
                            { 
                                Orientation = Orientation.Horizontal, 
                                Spacing = 8
                            };

                            // Add icon with data binding - will update when Icon property changes
                            var image = new Image
                            {
                                Width = 16,
                                Height = 16,
                                VerticalAlignment = VerticalAlignment.Center,
                            };

                            // Add text with data binding
                            var textBlock = new TextBlock
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.PrefixCharacterEllipsis,
                            };
                            
                            TreeNodeNameWithIconBehavior.AttachToNode(node, panel, image, textBlock);
                            
                            return panel;
                        }, supportsRecycling: false),
                        options: new TemplateColumnOptions<TreeNode>
                        {
                            CanUserResizeColumn = true,
                            MaxWidth = new GridLength(400),
                            CanUserSortColumn = true,
                            CompareAscending = (a, b) => string.Compare(a?.Name, b?.Name, StringComparison.OrdinalIgnoreCase),
                            CompareDescending = (a, b) => string.Compare(b?.Name, a?.Name, StringComparison.OrdinalIgnoreCase),
                        }),
                    x => x.Children,
                    x => x.HasChildren,
                    x => x.IsExpanded),
                new TemplateColumn<TreeNode>("Size",
                    new FuncDataTemplate<TreeNode>((node, _) =>
                    {
                        if (node is null)
                            return null;

                        var sizeConverter = new SizeToBrushConverter();
                        var brush = sizeConverter.Convert(node.Size, typeof(SolidColorBrush), null, null);

                        var textBlock = new TextBlock
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Right,
                            Foreground = brush as SolidColorBrush ?? new SolidColorBrush(Colors.Black),
                            FontWeight = node.IsSizeBold ? FontWeight.Bold : FontWeight.Normal,
                        };

                        // Bind to Size property so it updates when size changes
                        textBlock.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("SizeFormatted") { Source = node });
                        textBlock.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding("Size")
                        {
                            Source = node,
                            Converter = sizeConverter
                        });
                        textBlock.Bind(TextBlock.FontWeightProperty, new Avalonia.Data.Binding("IsSizeBold")
                        {
                            Source = node,
                            Converter = new BoolToFontWeightConverter()
                        });

                        return textBlock;
                    }, supportsRecycling: false),
                    options: new TemplateColumnOptions<TreeNode>
                    {
                        CanUserResizeColumn = true,
                        CanUserSortColumn = true,
                        CompareAscending = (a, b) => a?.Size.CompareTo(b?.Size ?? 0) ?? 0,
                        CompareDescending = (a, b) => b?.Size.CompareTo(a?.Size ?? 0) ?? 0,
                    }),

                new TextColumn<TreeNode, DateTime?>("Last Modified", x => x.LastModified,
                    options: new TextColumnOptions<TreeNode>
                    {
                        TextAlignment = TextAlignment.Right,
                        CanUserResizeColumn = true,
                        CanUserSortColumn = true,
                        CompareAscending = (a, b) => Nullable.Compare(a?.LastModified, b?.LastModified),
                        CompareDescending = (a, b) => Nullable.Compare(b?.LastModified, a?.LastModified),
                    }),
                new TextColumn<TreeNode, string>("Path", x => x.FullPath, options: new TextColumnOptions<TreeNode>
                {
                    TextTrimming = TextTrimming.PathSegmentEllipsis,
                    CanUserSortColumn = false,
                    MaxWidth = new GridLength((viewWidth ?? 900) / 3), // this will make the path column take up at most 1/3 of the window width
                }),
            }
        };
    }
}