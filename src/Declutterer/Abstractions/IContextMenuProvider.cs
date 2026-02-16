using CommunityToolkit.Mvvm.Input;
using Declutterer.Models;

namespace Declutterer.Abstractions;

/// <summary>
/// Interface for ViewModels that support context menu operations.
/// </summary>
public interface IContextMenuProvider
{
    IAsyncRelayCommand<TreeNode?> ContextMenuSelectCommand { get; }
    IAsyncRelayCommand<TreeNode?> ContextMenuOpenInExplorerCommand { get; }
    IAsyncRelayCommand<TreeNode?> ContextMenuCopyPathCommand { get; }
}