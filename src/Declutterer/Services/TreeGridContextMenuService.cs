using System;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Declutterer.Abstractions;
using Declutterer.Models;
using Serilog;

namespace Declutterer.Services;

public sealed class TreeGridContextMenuService : IContextMenuService
{
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;

    public TreeGridContextMenuService(IExplorerLauncher explorerLauncher, IErrorDialogService errorDialogService)
    {
        _explorerLauncher = explorerLauncher ?? throw new ArgumentNullException(nameof(explorerLauncher));
        _errorDialogService = errorDialogService ?? throw new ArgumentNullException(nameof(errorDialogService));
    }

    public void ToggleNodeSelection(TreeNode? node)
    {
        if (node is null || !node.IsCheckboxEnabled || node.Depth == 0)
            return;

        node.IsCheckboxSelected = !node.IsCheckboxSelected;
    }

    public async Task OpenInExplorerAsync(TreeNode? node)
    {
        try
        {
            if (node is null)
                return;

            _explorerLauncher.OpenInExplorer(node.FullPath);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to open node in explorer: {NodePath}", node?.FullPath);
            await _errorDialogService.ShowErrorAsync(
                "Failed to Open in Explorer",
                $"Could not open the path in File Explorer:\n{node?.FullPath}",
                e);
        }
    }
}
