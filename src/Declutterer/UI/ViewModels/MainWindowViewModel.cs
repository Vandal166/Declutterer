using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Deletion;
using Declutterer.Domain.Services.Observation;
using Declutterer.Utilities.Helpers;
using Serilog;
using ByteConverter = Declutterer.Utilities.Helpers.ByteConverter;
using ScanOptions = Declutterer.Domain.Models.ScanOptions;
using TreeNode = Declutterer.Domain.Models.TreeNode;

namespace Declutterer.UI.ViewModels;

//TODO: add exclusions for Directories so they won't be scanned at all, not even shown in the tree, Persist the exclusions in some form of settings like json file

public partial class MainWindowViewModel : ViewModelBase, IDisposable, IContextMenuProvider
{
    // ObservableProperty is used to generate the property with INotifyPropertyChanged implementation which will notify the UI when the property changes
    [ObservableProperty]
    private bool _isAnyNodeLoading = false; // used for showing loading indicator on the UI
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTreeDataGridVisible))] // if NoChildrenFound changes, we also want to notify that IsTreeDataGridVisible has changed since it depends on NoChildrenFound
    private bool _noChildrenFound = false; // flag to indicate that no children were found for the selected directories based on the scan options
    
    [ObservableProperty]
    private string _selectedNodesSizeText = string.Empty; // a user-friendly string representation of the total size of the currently selected nodes
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainViewVisible))]
    private bool _isHistoryVisible = false; // flag to show/hide the history view

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainViewVisible))]
    private bool _isWatchlistVisible = false; // flag to show/hide the watchlist (observation) view

    /// <summary>The scan / tree view is shown unless the history or the watchlist has taken its place.</summary>
    public bool IsMainViewVisible => !IsHistoryVisible && !IsWatchlistVisible;

    [ObservableProperty]
    private HistoryWindowViewModel _historyViewModel; // ViewModel for the history view
    
    [ObservableProperty]
    private SmartSelectMode _smartSelectMode = SmartSelectMode.Balanced; // how aggressively Smart Select ticks checkboxes

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunSmartSelect))]
    private bool _isSmartSelectRunning;

    [ObservableProperty]
    private string _smartSelectProgressText = string.Empty;

    [ObservableProperty]
    private string _smartSelectSummary = string.Empty; // what the last Smart Select did, shown next to the selection summary

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasObservationCandidates))]
    private int _observationCandidateCount; // folders Smart Select suggests watching for a while (0 unless observation is on)

    [ObservableProperty]
    private string _observationCandidateText = string.Empty; // label of the "Watch n" button

    [ObservableProperty]
    private string _observationCandidateTooltip = string.Empty;

    public bool HasObservationCandidates => ObservationCandidateCount > 0;

    /// <summary>The page behind the Watchlist button. Null when observation is not available (designer, tests).</summary>
    public WatchlistViewModel? WatchlistViewModel { get; }

    private IReadOnlyList<ObservationCandidate> _observationCandidates = Array.Empty<ObservationCandidate>();
    private ObservationDiscoverySummary? _observationDiscoverySummary;
    private CancellationTokenSource? _smartSelectCancellation;
    private int _scanScopeVersion;

    public IReadOnlyList<SmartSelectMode> SmartSelectModes { get; } = Enum.GetValues<SmartSelectMode>();

    public bool IsExpandingAll { get; private set; } = false; // Flag to prevent multiple simultaneous expand/collapse operations

    public bool IsTreeDataGridVisible => Roots.Count > 0 && !NoChildrenFound; // The TreeDataGrid is visible if there are roots to display and we didn't just find that there are no children based on the scan options
    public bool CanRunSmartSelect => Roots.Count > 0 && !IsSmartSelectRunning;
    
    private readonly IContextMenuService _contextMenuService;
    private readonly ICommandService _commandService;
    private readonly INavigationService _navigationService;
    private readonly IScanWorkflowService _scanWorkflowService;
    private readonly ITreeNavigationService _treeNavigationService;
    private readonly IClipboardService _clipboardService;
    private readonly ISelectionManagementService _selectionManagementService;
    private readonly ISmartCleanupService _smartCleanupService;
    private readonly IObservationCoordinator? _observationCoordinator;
    private readonly IConfirmationDialogService? _confirmationDialogService;
    private readonly EventHandler<PropertyChangedEventArgs>? _selectionChangedHandler;

    private ScanOptions? _currentScanOptions;
    
    // an collection of root TreeNodes representing the top-level directories added by the user
    public ObservableCollection<TreeNode> Roots { get; } = new();
    
    public ObservableHashSet<TreeNode> SelectedNodes { get; } = new(); // the currently selected nodes in the TreeDataGrid
    
    /// <summary>
    /// Event raised when the cleanup window closes. Allows the view to perform cleanup operations.
    /// </summary>
    public event EventHandler<DeleteResult?>? CleanupWindowClosed;
    
    public MainWindowViewModel(INavigationService navigationService, IScanWorkflowService scanWorkflowService, ITreeNavigationService treeNavigationService,
        IContextMenuService contextMenuService, ICommandService commandService, IClipboardService clipboardService, ISelectionManagementService selectionManagementService, HistoryWindowViewModel historyViewModel, ISmartCleanupService smartCleanupService,
        IObservationCoordinator? observationCoordinator = null, WatchlistViewModel? watchlistViewModel = null, IConfirmationDialogService? confirmationDialogService = null)
    {
        _observationCoordinator = observationCoordinator;
        _confirmationDialogService = confirmationDialogService;
        WatchlistViewModel = watchlistViewModel;
        WatchlistViewModel?.SetHideCallback(HideWatchlistInline);
        _navigationService = navigationService;
        _scanWorkflowService = scanWorkflowService;
        _treeNavigationService = treeNavigationService;
        _contextMenuService = contextMenuService;
        _commandService = commandService;
        _clipboardService = clipboardService;
        _selectionManagementService = selectionManagementService;
        _historyViewModel = historyViewModel;
        _smartCleanupService = smartCleanupService;

        // selection change tracking
        SelectedNodes.CollectionChanged += OnSelectedNodesCollectionChanged;
        
        // The root set is part of the scan scope. Any change invalidates suggestions and any in-flight result.
        Roots.CollectionChanged += OnRootsCollectionChanged;
        
        // sub to selection changes on all nodes to keep SelectedNodes collection in sync
        _selectionChangedHandler = (node, args) =>
        {
            if (node is TreeNode treeNode)
            {
                _selectionManagementService.HandleNodeSelectionChanged(treeNode, SelectedNodes);
            }
        };
        _selectionManagementService.OnNodePropertyChanged += _selectionChangedHandler;
    }

    public MainWindowViewModel() { } // for designer

    private void OnRootsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsTreeDataGridVisible));
        OnPropertyChanged(nameof(CanRunSmartSelect));
        _scanScopeVersion++;
        _smartSelectCancellation?.Cancel();
        SmartSelectSummary = string.Empty;
        SetObservationCandidates(null);
    }

    partial void OnSmartSelectModeChanged(SmartSelectMode value)
    {
        _scanScopeVersion++;
        _smartSelectCancellation?.Cancel();
        SmartSelectSummary = string.Empty;
        SetObservationCandidates(null);
    }

    private void OnSelectedNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSelectedNodesSize();
        
    private void UpdateSelectedNodesSize()
    {
        var selectedNodesSize = SelectedNodes.Sum(n => n.Size);
        SelectedNodesSizeText = ByteConverter.ToReadableString(selectedNodesSize);
    }

    // Loads subdirectories and files for a given node when it's expanded. This is called from the UI when a node is expanded.
    public async Task LoadChildrenForNodeAsync(TreeNode node)
    {
        IsAnyNodeLoading = true;
        try
        {
            await _treeNavigationService.LoadChildrenForNodeAsync(node, _currentScanOptions);
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        // Hide history / watchlist view if one is currently visible before navigating to Home
        if (IsHistoryVisible)
            IsHistoryVisible = false;
        if (IsWatchlistVisible)
            IsWatchlistVisible = false;

        Roots.Clear();
        SelectedNodes.Clear();
        _currentScanOptions = null;
        NoChildrenFound = false;
        SmartSelectSummary = string.Empty;
        SmartSelectProgressText = string.Empty;
        SetObservationCandidates(null);
        _scanScopeVersion++;
        _smartSelectCancellation?.Cancel();
    }
    
    [RelayCommand]
    private void SelectAll() => _commandService.SelectAllChildren(Roots);

    [RelayCommand]
    private void DeselectAll() => _commandService.DeselectAll(SelectedNodes);

    /// <summary>
    /// Evidence based selection: computes heuristic junk, deletion-risk and evidence-strength scores, then ticks only what qualifies
    /// for <see cref="SmartSelectMode"/>. It never deletes - the user still reviews and confirms in the cleanup window.
    /// </summary>
    [RelayCommand]
    private async Task SmartSelectAsync()
    {
        if (Roots.Count == 0 || IsSmartSelectRunning)
            return;

        SetObservationCandidates(null);
        var cancellation = new CancellationTokenSource();
        _smartSelectCancellation = cancellation;
        var scopeVersion = _scanScopeVersion;
        IsSmartSelectRunning = true;
        IsAnyNodeLoading = true;
        SmartSelectSummary = "Analyzing...";
        SmartSelectProgressText = "Starting Smart Select...";
        try
        {
            var progress = new Progress<SmartCleanupProgress>(p =>
            {
                SmartSelectProgressText = p.Total > 0
                    ? $"{p.Stage} ({p.Done:N0}/{p.Total:N0})"
                    : $"{p.Stage} ({p.Done:N0} folders)";
            });
            var result = await _smartCleanupService.SelectAsync(Roots.ToList(), SmartSelectMode, _currentScanOptions,
                SelectedNodes, cancellation.Token, progress, () => scopeVersion == _scanScopeVersion);
            if (scopeVersion == _scanScopeVersion)
            {
                SmartSelectSummary = DescribeSmartSelect(result, hiddenSkipped: _currentScanOptions is { IncludeHidden: false });
                SetObservationCandidates(result.ObservationCandidates, result.ObservationDiscovery);
            }
        }
        catch (OperationCanceledException)
        {
            if (scopeVersion == _scanScopeVersion)
                SmartSelectSummary = "Smart Select cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Smart Select failed");
            if (scopeVersion == _scanScopeVersion)
                SmartSelectSummary = "Smart Select failed: " + ex.Message;
        }
        finally
        {
            IsSmartSelectRunning = false;
            IsAnyNodeLoading = false;
            SmartSelectProgressText = string.Empty;
            if (ReferenceEquals(_smartSelectCancellation, cancellation))
            {
                _smartSelectCancellation.Dispose();
                _smartSelectCancellation = null;
            }
        }
    }

    [RelayCommand]
    private void CancelSmartSelect() => _smartSelectCancellation?.Cancel();

    public static string DescribeSmartSelect(SmartCleanupResult r, bool hiddenSkipped = false)
    {
        var text = r.SelectedCount == 0
            ? "Nothing qualified. Old items are only suggested when there is evidence they are unused - try Balanced or Aggressive."
            : $"Selected {r.SelectedCount} items ({ByteConverter.ToReadableString(r.SelectedBytes)}): " +
              $"{r.SafeCount} safe, {r.LikelyCount} likely, {r.ReviewCount} to review. Hover a row in the Assessment column for the reasons.";

        // Say what limited the result, so "found little" can be told apart from "could not look".
        if (r.BudgetExceeded)
            text += $" Time budget reached, {r.UninspectedFolders} folders were not inspected.";
        if (r.StoppedByTimeLimit)
            text += " Stopped at the overall time limit - run Smart Select again to go deeper (opened folders stay loaded).";
        if (r.UnexploredFolders > 0)
            text += $" {r.UnexploredFolders} large folders were not opened (depth / round limit).";
        if (r.IncompleteSafetyScans > 0)
            text += $" {r.IncompleteSafetyScans} folders could not be fully safety-checked and were not auto-selected.";
        if (hiddenSkipped)
            text += " Hidden folders (AppData, ProgramData, .git, .vs) were not scanned - enable \"Include hidden\" in Scan Settings to find caches and app leftovers.";
        if (r.ObservationDiscovery is { } discovery)
            text += " " + DescribeObservationDiscovery(discovery);
        return text;
    }

    private static string DescribeObservationDiscovery(ObservationDiscoverySummary summary)
    {
        var status = summary.IsComplete ? "Watch discovery complete" : "Watch discovery partial";
        var text = $"{status}: examined {summary.FoldersExamined:N0} folders; found {summary.CandidatesFound:N0} eligible unselected folders and offered {summary.CandidatesReturned:N0}.";
        if (summary.AvailableWatchSlots == 0)
            text += " No active-watch capacity is available.";
        else if (summary.ExcludedByCapacity > 0)
            text += $" {summary.AvailableWatchSlots:N0} watch slots were available; {summary.ExcludedByCapacity:N0} candidates exceeded capacity.";
        if (summary.ExcludedSelectedOverlap > 0)
            text += $" {summary.ExcludedSelectedOverlap:N0} overlapped Smart Select selections.";
        if (summary.ExcludedAlreadyWatched > 0)
            text += $" {summary.ExcludedAlreadyWatched:N0} were already watched, pinned or snoozed.";
        if (summary.ExcludedUnsafeOrIncomplete > 0)
            text += $" {summary.ExcludedUnsafeOrIncomplete:N0} were blocked by safety or incomplete facts / checks.";
        if (summary.ExcludedValuableOrSensitive > 0)
            text += $" {summary.ExcludedValuableOrSensitive:N0} contained valuable or sensitive data.";
        if (summary.HiddenFoldersSkipped > 0)
            text += $" {summary.HiddenFoldersSkipped:N0} hidden {(summary.HiddenFoldersSkipped == 1 ? "folder was" : "folders were")} excluded by Scan Settings.";
        if (summary.FilteredFoldersSkipped > 0)
            text += $" {summary.FilteredFoldersSkipped:N0} folders did not match the scan filters.";
        if (summary.ReparseFoldersSkipped > 0)
            text += $" {summary.ReparseFoldersSkipped:N0} reparse folders were skipped.";
        if (summary.SystemFoldersSkipped > 0)
            text += $" {summary.SystemFoldersSkipped:N0} system / unavailable folders were skipped.";
        if (summary.InaccessibleDirectories > 0)
            text += $" {summary.InaccessibleDirectories:N0} folders could not be read.";
        if (summary.DepthLimitedDirectories > 0 || summary.EntryLimitedDirectories > 0)
            text += " A discovery depth or entry limit was reached.";
        if (summary.WasCancelled)
            text += " Discovery was cancelled; its results are partial.";
        else if (!summary.IsComplete && !string.IsNullOrWhiteSpace(summary.PartialReason))
            text += $" {summary.PartialReason}.";
        return text;
    }

    [RelayCommand]
    private async Task ShowScanOptionsWindowAsync()
    {
        var result = await _navigationService.ShowScanOptionsAsync();
        if (result != null)
        {
            if (IsHistoryVisible)
                IsHistoryVisible = false;
            if (IsWatchlistVisible)
                IsWatchlistVisible = false;
            
            _currentScanOptions = result;
            SetObservationCandidates(null);

            await InvokeScanAsync();
        }
    }

    public async Task InvokeScanAsync()
    {
        if(_currentScanOptions is null)
            return;
        
        // Clean up old subscriptions before clearing roots
        _selectionManagementService.UnsubscribeFromAllNodes();
        _scanScopeVersion++;
        _smartSelectCancellation?.Cancel();
        SelectedNodes.Clear();
        SetObservationCandidates(null);
        SmartSelectSummary = string.Empty;
        SmartSelectProgressText = string.Empty;

        IsAnyNodeLoading = true;
        try
        {
            bool scanSucceeded = await _scanWorkflowService.ExecuteScanAsync(_currentScanOptions, Roots);
            NoChildrenFound = !scanSucceeded; // if scan failed or all roots have no children then set NoChildrenFound to true
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }

    [RelayCommand]
    private void ShowHistoryInline()
    {
        // Create history view model with callback to hide history
        ClearAll();
        HistoryViewModel.SetHideHistoryCallback(HideHistoryInline);
        IsHistoryVisible = true;
    }

    [RelayCommand]
    private async Task ShowWatchlistInlineAsync()
    {
        // unlike History this keeps the current scan: Back returns to it
        IsHistoryVisible = false;
        IsWatchlistVisible = true;
        if (WatchlistViewModel is not null)
            await WatchlistViewModel.LoadAsync();
    }

    [RelayCommand]
    private void HideWatchlistInline() => IsWatchlistVisible = false;

    private void SetObservationCandidates(IReadOnlyList<ObservationCandidate>? candidates, ObservationDiscoverySummary? summary = null)
    {
        _observationCandidates = candidates ?? Array.Empty<ObservationCandidate>();
        _observationDiscoverySummary = summary;
        ObservationCandidateCount = _observationCandidates.Count;
        ObservationCandidateText = _observationCandidates.Count switch
        {
            0 => string.Empty,
            1 => summary is { IsComplete: false } ? "Watch 1 unselected folder (partial)" : "Watch 1 unselected folder",
            var n => summary is { IsComplete: false } ? $"Watch {n} unselected folders (partial)" : $"Watch {n} unselected folders",
        };
        ObservationCandidateTooltip = BuildObservationCandidateTooltip(_observationCandidates, summary);
    }

    private static string BuildObservationCandidateTooltip(IReadOnlyList<ObservationCandidate> candidates,
        ObservationDiscoverySummary? summary)
    {
        if (candidates.Count == 0)
            return string.Empty;

        var text = "Suggestions include suspicious and stale folders. Watching does not select or delete them. Junk score, tier and deletion risk are separate signals:";
        foreach (var candidate in candidates.Take(4))
        {
            var why = candidate.Reason == ObservationCandidateReason.Stale ? "stale evidence" : "suspicious evidence";
            var reasons = candidate.ReasonCodes.Take(3).Select(code => code.ToTitle()).ToArray();
            if (reasons.Length > 0)
                why += $" ({string.Join(", ", reasons)})";
            text += $"\n{candidate.Name}: junk score {candidate.JunkScore:P0}; tier {candidate.Tier}; deletion risk {candidate.DeletionRisk:P0}; {why}.";
        }
        if (candidates.Count > 4)
            text += $"\nAnd {candidates.Count - 4:N0} more.";
        if (summary is { IsComplete: false })
            text += " Discovery is partial; see the Smart Select summary for coverage details.";
        return text;
    }

    /// <summary>
    /// Adds the suspicious or stale folders found by discovery after the user confirms what watching means.
    /// Nothing is deleted and nothing is selected by this.
    /// </summary>
    [RelayCommand]
    private async Task EnrollObservationCandidatesAsync()
    {
        if (_observationCoordinator is null || _observationCandidates.Count == 0)
            return;

        var offeredCandidates = _observationCandidates;
        var candidates = offeredCandidates.ToList();
        var message = ObservationConsentText.BuildEnrollment(candidates.Count, candidates.Sum(c => c.Size), _observationCoordinator.Options,
            candidates.Select(c => c.Class).Distinct().ToArray());
        // Fails closed like the Watchlist page does: without a way to ask, nothing is added to the watchlist.
        if (_confirmationDialogService is null ||
            !await _confirmationDialogService.ShowConfirmationAsync("Watch these folders?", message, "Start watching"))
            return;

        // The user may change Smart Select mode / scan scope while the confirmation dialog is open. In that case the old
        // candidate snapshot must not be enrolled.
        if (!ReferenceEquals(offeredCandidates, _observationCandidates))
            return;

        var selectedPaths = SelectedNodes.Select(node => node.FullPath).ToArray();
        int selectedOverlap = candidates.RemoveAll(candidate => selectedPaths.Any(path => ObservationNominator.PathsOverlap(path, candidate.Path)));
        if (selectedOverlap > 0)
            SmartSelectSummary = $"Skipped {selectedOverlap} watch suggestion(s) because their paths overlap folders currently selected for cleanup.";
        if (candidates.Count == 0)
        {
            SetObservationCandidates(null, _observationDiscoverySummary);
            return;
        }

        IsAnyNodeLoading = true;
        try
        {
            var result = await _observationCoordinator.EnrollAsync(candidates);
            SmartSelectSummary = DescribeEnrollment(result) +
                                 (selectedOverlap > 0 ? $" Skipped {selectedOverlap} suggestion(s) that became selected after nomination." : string.Empty);
            SetObservationCandidates(null);
            if (WatchlistViewModel is not null)
                await WatchlistViewModel.LoadAsync();
        }
        catch (OperationCanceledException)
        {
            SmartSelectSummary = "Adding to the watchlist was cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Adding folders to the watchlist failed");
            SmartSelectSummary = "Adding to the watchlist failed: " + ex.Message;
        }
        finally
        {
            IsAnyNodeLoading = false;
        }
    }

    public static string DescribeEnrollment(EnrollmentResult r)
    {
        var text = r.Enrolled == 0
            ? "Nothing was added to the watchlist."
            : $"Watching {r.Enrolled} folder(s). Open the Watchlist to follow their progress; Smart Select uses what is seen the next time you run it." +
              (r.AliveOnEnrollment > 0 ? $" {r.AliveOnEnrollment} of them were in use recently, so they start as \"in use\"." : string.Empty);
        if (r.Skipped.Count > 0)
            text += $" Skipped {r.Skipped.Count}: {string.Join("; ", r.Skipped.Select(s => string.IsNullOrEmpty(s.Path) ? s.Reason : $"{System.IO.Path.GetFileName(s.Path)} ({s.Reason})").Take(3))}" + (r.Skipped.Count > 3 ? "; ..." : ".");
        return text;
    }

    [RelayCommand]
    private void HideHistoryInline()
    {
        // Hide the history view and return to main view
        IsHistoryVisible = false;
        //_historyViewModel = null;
    }
    
    [RelayCommand]
    private async Task ShowCleanupWindowAsync()
    {
        if (SelectedNodes.Count == 0)
            return;

        var result = await _navigationService.ShowCleanupWindowAsync(SelectedNodes);
        
        // Signal that cleanup window has closed
        CleanupWindowClosed?.Invoke(this, result);
    }
    
    public async Task HandleAltClickExpandAsync(TreeNode node, bool shouldExpand)
    {
        IsAnyNodeLoading = true;
        try
        {
            // Reset the flag
            NoChildrenFound = false;
            IsExpandingAll = true;
            
            // Pass isRoot=true so we skip setting IsExpanded on the clicked root node (the TreeDataGrid handles the root node's toggle via normal click processing)
            await _treeNavigationService.ToggleAllDescendantsAsync(node, shouldExpand, isRoot: true, currentScanOptions: _currentScanOptions);
        }
        finally
        {
            IsExpandingAll = false;
            IsAnyNodeLoading = false;
        }
    }
    
    [RelayCommand]
    public void UpdateNodeSelection(SelectionUpdateRequest request)
    {
        if (request.Node.IsCheckboxSelected == request.IsCheckboxSelected)
            return; // No change, skip

        request.Node.IsCheckboxSelected = request.IsCheckboxSelected;

        if (request.IsCheckboxSelected)
        {
            SelectedNodes.Add(request.Node);
        }
        else
        {
            SelectedNodes.Remove(request.Node);
        }
    }
    
    [RelayCommand]
    private Task ContextMenuSelect(TreeNode? node)
    {
        _contextMenuService.ToggleNodeSelection(node);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ContextMenuOpenInExplorer(TreeNode? node)
    {
        await _contextMenuService.OpenInExplorerAsync(node);
    }

    [RelayCommand]
    private async Task ContextMenuCopyPath(TreeNode? node)
    {
        if (node is null)
            return;

        await _clipboardService.CopyTextAsync(node.FullPath);
    }
    
    public void SubscribeToNodeSelectionChanges(TreeNode node)
    {
        _selectionManagementService.SubscribeToNodeSelectionChanges(node);
    }
    
    private bool _disposed = false;
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Unsubscribe from SelectedNodes collection changes
            SelectedNodes.CollectionChanged -= OnSelectedNodesCollectionChanged;
        
            // Unsubscribe from selection management service events
            if (_selectionChangedHandler != null)
            {
                _selectionManagementService.OnNodePropertyChanged -= _selectionChangedHandler;
            }
        
            // Dispose selection management service
            _selectionManagementService.Dispose();
        }
        _disposed = true;
    }
}
