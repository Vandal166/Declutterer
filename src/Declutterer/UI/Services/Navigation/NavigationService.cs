using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Declutterer.Abstractions;
using Declutterer.Domain.Services.Deletion;
using Declutterer.UI.Views;
using Declutterer.Utilities.Helpers;
using CleanupWindowViewModel = Declutterer.UI.ViewModels.CleanupWindowViewModel;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using ScanOptionsWindowViewModel = Declutterer.UI.ViewModels.ScanOptionsWindowViewModel;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.Services.Navigation;

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

    public async Task<DeleteResult?> ShowCleanupWindowAsync(ObservableHashSet<TreeNode> selectedNodes)
    {
        if (_ownerWindow is not Window window)
            throw new InvalidOperationException("Owner window not set. Call SetOwnerWindow first.");

        var cleanupWindow = new CleanupWindow(_errorDialogService, _confirmationDialogService)
        {
            DataContext = new CleanupWindowViewModel(selectedNodes, _explorerLauncher, _errorDialogService, _confirmationDialogService, _deleteService)
        };

        return await cleanupWindow.ShowDialog<DeleteResult?>(window);
    }
}