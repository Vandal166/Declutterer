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
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IDeleteService _deleteService;
    private Window? _ownerWindow;

    public NavigationService(IExplorerLauncher explorerLauncher, IErrorDialogService errorDialogService, IConfirmationDialogService confirmationDialogService, IDeleteService deleteService, IDeletionHistoryRepository deletionHistoryRepository)
    {
        _explorerLauncher = explorerLauncher;
        _errorDialogService = errorDialogService;
        _confirmationDialogService = confirmationDialogService;
        _deleteService = deleteService;
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

        var cleanupWindow = new CleanupWindow(_errorDialogService, _confirmationDialogService)
        {
            DataContext = new CleanupWindowViewModel(selectedNodes, _explorerLauncher, _errorDialogService, _confirmationDialogService, _deleteService)
        };

        await cleanupWindow.ShowDialog(window);
    }
}