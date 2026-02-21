using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Declutterer.Domain.Models;
using MainWindowViewModel = Declutterer.UI.ViewModels.MainWindowViewModel;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Behaviours;

public static class CheckBoxSelectionBehavior
{
    private class CheckBoxAttachment : IDisposable
    {
        private readonly CheckBox _checkBox;
        private readonly TreeNode _node;
        private readonly MainWindowViewModel _viewModel;
        private EventHandler<RoutedEventArgs>? _isCheckedChangedHandler;
        
        public CheckBoxAttachment(CheckBox checkBox, TreeNode node, MainWindowViewModel viewModel)
        {
            _checkBox = checkBox;
            _node = node;
            _viewModel = viewModel;
            
            // Set initial checked state
            _checkBox.IsChecked = _node.IsCheckboxSelected;
            
            // Bind IsChecked to TreeNode.IsSelected
            _checkBox.Bind(ToggleButton.IsCheckedProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsCheckboxSelected))
            {
                Source = _node,
                Mode = Avalonia.Data.BindingMode.TwoWay
            });
                            
            // Bind IsEnabled to TreeNode.IsEnabled
            _checkBox.Bind(InputElement.IsEnabledProperty, new Avalonia.Data.Binding(nameof(TreeNode.IsCheckboxEnabled))
            {
                Source = _node,
                Mode = Avalonia.Data.BindingMode.OneWay
            });
                            
            // Sync from CheckBox to TreeNode
            _isCheckedChangedHandler = (s, e) =>
            {
                var isChecked = _checkBox.IsChecked ?? false;
                _viewModel.UpdateNodeSelection(new SelectionUpdateRequest(_node, isChecked));
            };
            
            _checkBox.IsCheckedChanged += _isCheckedChangedHandler;
        }
        
        public void Dispose()
        {
            if (_isCheckedChangedHandler != null)
            {
                _checkBox.IsCheckedChanged -= _isCheckedChangedHandler;
            }
        }
    }
    
    /// <summary>
    /// Attaches checkbox selection behavior to a TreeNode.
    /// </summary>
    /// <param name="checkBox">The checkbox control to attach to</param>
    /// <param name="node">The TreeNode to synchronize with</param>
    /// <param name="viewModel">The MainWindowViewModel for selection updates</param>
    /// <returns>An IDisposable that must be disposed to clean up event subscriptions and prevent memory leaks</returns>
    /// <remarks>
    /// IMPORTANT: The returned IDisposable MUST be disposed when the checkbox is no longer needed,
    /// otherwise event handlers will remain subscribed and cause memory leaks. The checkbox's
    /// IsCheckedChanged event will remain subscribed until Dispose() is called.
    /// </remarks>
    public static IDisposable AttachToNode(CheckBox checkBox, TreeNode node, MainWindowViewModel viewModel)
    {
        return new CheckBoxAttachment(checkBox, node, viewModel);
    }
}