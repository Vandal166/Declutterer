using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Declutterer.Abstractions;
using Declutterer.Common;
using Declutterer.Models;
using Declutterer.ViewModels;
using Declutterer.Views;

namespace Declutterer.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IExplorerLauncher _explorerLauncher;
    private readonly IErrorDialogService _errorDialogService;
    private Window? _ownerWindow;

    public NavigationService(IExplorerLauncher explorerLauncher, IErrorDialogService errorDialogService)
    {
        _explorerLauncher = explorerLauncher;
        _errorDialogService = errorDialogService;
    }

    public void SetOwnerWindow(Window window)
    {
        _ownerWindow = window;
    }

    public async Task<ScanOptions?> ShowScanOptionsAsync()
    {
        if (_ownerWindow is not Window window)
            throw new InvalidOperationException("Owner window not set. Call SetOwnerWindow first.");

        var scanOptionsWindow = new ScanOptionsWindow
        {
            DataContext = new ScanOptionsWindowViewModel()
        };

        return await scanOptionsWindow.ShowDialog<ScanOptions?>(window);
    }

    public async Task ShowCleanupWindowAsync(ObservableHashSet<TreeNode> selectedNodes)
    {
        if (_ownerWindow is not Window window)
            throw new InvalidOperationException("Owner window not set. Call SetOwnerWindow first.");

        var cleanupWindow = new CleanupWindow
        {
            DataContext = new CleanupWindowViewModel(selectedNodes, _explorerLauncher, _errorDialogService)
        };

        await cleanupWindow.ShowDialog(window);
    }
}