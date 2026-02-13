using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Declutterer.Models;
using Declutterer.ViewModels;

namespace Declutterer.Factories;

public static class CheckBoxSelectionBehavior
{
    public static void AttachToNode(CheckBox checkBox, TreeNode node, MainWindowViewModel viewModel)
    {
        // Set initial checked state
        checkBox.IsChecked = node.IsCheckboxSelected;
        
        // Bind IsChecked to TreeNode.IsSelected
        checkBox.Bind(ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsCheckboxSelected))
        {
            Source = node,
            Mode = Avalonia.Data.BindingMode.TwoWay
        });
                        
        // Bind IsEnabled to TreeNode.IsEnabled
        checkBox.Bind(InputElement.IsEnabledProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsCheckboxEnabled))
        {
            Source = node,
            Mode = Avalonia.Data.BindingMode.OneWay
        });
                        
        // Sync from CheckBox to TreeNode
        checkBox.IsCheckedChanged += (s, e) =>
        {
            var isChecked = checkBox.IsChecked ?? false;
            viewModel.UpdateNodeSelection(new SelectionUpdateRequest(node, isChecked));
        };
    }
}