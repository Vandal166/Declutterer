using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Declutterer.Abstractions;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Observation;
using Serilog;

namespace Declutterer.UI.ViewModels;

/// <summary>
/// The watchlist: what is being observed and how far along it is, the on/off switch (with the consent text the first time), the
/// actions on a row (stop, keep, snooze, re-enroll, reassess), deleting everything that was stored, and the optional daily
/// background check-in.
/// </summary>
public sealed partial class WatchlistViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<WatchlistRowViewModel> _rows = new();

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasRows;

    [ObservableProperty]
    private bool _isLedgerAvailable = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _lastRunText = "No check-in has run yet.";

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    private bool _isSchedulerSupported;

    [ObservableProperty]
    private bool _isSchedulerInstalled;

    [ObservableProperty]
    private string _schedulerText = string.Empty;

    private readonly IObservationCoordinator? _coordinator;
    private readonly IConfirmationDialogService? _confirmations;
    private readonly IExplorerLauncher? _explorer;
    private readonly IObservationScheduler? _scheduler;
    private Action? _onHide;

    public WatchlistViewModel(IObservationCoordinator coordinator, IConfirmationDialogService confirmations, IExplorerLauncher explorer, IObservationScheduler scheduler)
    {
        _coordinator = coordinator;
        _confirmations = confirmations;
        _explorer = explorer;
        _scheduler = scheduler;
    }

    public WatchlistViewModel() { } // for designer

    public void SetHideCallback(Action onHide) => _onHide = onHide;

    public void SetOwnerWindow(Window window) => _confirmations?.SetOwnerWindow(window);

    /// <summary>Reloads everything shown on the page.</summary>
    public async Task LoadAsync()
    {
        if (_coordinator is null)
            return;

        IsLoading = true;
        try
        {
            IsEnabled = _coordinator.IsEnabled;
            IsLedgerAvailable = _coordinator.IsLedgerAvailable;
            StorageText = $"Stored locally in: {_coordinator.LedgerPath}";

            var now = DateTime.UtcNow;
            var entries = await _coordinator.GetWatchlistAsync();
            Rows.Clear();
            foreach (var entry in entries.OrderBy(e => Order(e.State)).ThenBy(e => e.DisplayPath, StringComparer.OrdinalIgnoreCase))
                Rows.Add(new WatchlistRowViewModel(entry, now));
            HasRows = Rows.Count > 0;

            LastRunText = DescribeLastRun(await _coordinator.GetLastRunAsync());
            await LoadSchedulerAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not load the watchlist");
            StatusMessage = "Could not load the watchlist: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSchedulerAsync()
    {
        if (_scheduler is null)
            return;

        IsSchedulerSupported = _scheduler.IsSupported;
        if (!_scheduler.IsSupported)
        {
            SchedulerText = "Background check-ins are not supported on this platform.";
            return;
        }

        var status = await _scheduler.GetStatusAsync();
        IsSchedulerInstalled = status.Installed;
        SchedulerText = status.Installed
            ? "A daily background check-in is set up. It records the days this computer is on, even when Declutterer is closed."
            : "Optional: a daily background check-in records the days this computer is on, even when Declutterer is closed. Without it, only the days you open Declutterer count.";
    }

    private static int Order(WatchState state) => state switch
    {
        WatchState.QuietEligible => 0,
        WatchState.Watching => 1,
        WatchState.Alive => 2,
        WatchState.Unobservable => 3,
        WatchState.Retired => 4,
        _ => 5,
    };

    public static string DescribeLastRun(RunLogRecord? run)
    {
        if (run is null)
            return "No check-in has run yet.";

        var when = run.FinishedUtc.ToLocalTime().ToString("g");
        return run.Outcome switch
        {
            CheckInOutcome.Ok => $"Last check-in: {when}, {run.ItemsChecked} item(s) checked. {run.Detail}".TrimEnd(),
            CheckInOutcome.LockHeld => $"Last check-in attempt: {when}. Another check-in was already running.",
            CheckInOutcome.LedgerUnavailable => $"Last check-in attempt: {when}. The observation data file could not be used.",
            CheckInOutcome.Cancelled => $"Last check-in: {when}, cancelled.",
            CheckInOutcome.Disabled => $"Last check-in attempt: {when}. Observation was switched off.",
            _ => $"Last check-in: {when} failed. {run.Detail}".TrimEnd(),
        };
    }

    // ── switching it on and off ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task EnableAsync()
    {
        if (_coordinator is null)
            return;

        if (!await _coordinator.HasConsentAsync())
        {
            // the first time: exactly what is stored and for how long, before anything is
            var agreed = _confirmations is not null && await _confirmations.ShowConfirmationAsync(
                "Watch folders over time?", ObservationConsentText.Build(_coordinator.Options, _coordinator.LedgerPath), "Turn on");
            if (!agreed)
                return;
        }

        await _coordinator.SetEnabledAsync(true);
        StatusMessage = _coordinator.IsEnabled
            ? "Observation is on. Run Smart Select to see which folders it suggests watching."
            : "Observation could not be turned on: its data file is not usable.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        if (_coordinator is null)
            return;

        await _coordinator.SetEnabledAsync(false);
        StatusMessage = "Observation is off. What was stored is kept until you delete it; nothing is checked any more.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearDataAsync()
    {
        if (_coordinator is null)
            return;

        var confirmed = _confirmations is not null && await _confirmations.ShowConfirmationAsync(
            "Delete all observation data?",
            "This removes every watched folder, all check-in records and the list of days this computer was in use. " +
            "This does NOT delete any files/folders on your machine.");
        if (!confirmed)
            return;

        await _coordinator.ClearAllDataAsync();
        StatusMessage = "All observation data was deleted.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (_coordinator is null)
            return;

        IsLoading = true;
        try
        {
            var result = await _coordinator.RunCheckInAsync("manual");
            StatusMessage = result.Outcome switch
            {
                CheckInOutcome.Ok => $"Checked {result.ItemsChecked} item(s): {result.Alive} in use, {result.QuietEligible} newly unused, {result.Missing} gone, {result.Deferred} left for next time.",
                CheckInOutcome.Disabled => "Observation is off.",
                CheckInOutcome.LockHeld => "Another check-in is already running.",
                CheckInOutcome.LedgerUnavailable => "The observation data file could not be used.",
                _ => "The check-in did not complete: " + result.Detail,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual observation check-in failed");
            StatusMessage = "The check-in failed: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
        await LoadAsync();
    }

    // ── rows ─────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StopWatchingAsync(WatchlistRowViewModel? row)
    {
        if (_coordinator is null || row is null)
            return;

        await _coordinator.StopWatchingAsync(row.Id);
        StatusMessage = $"Stopped watching {row.Name}.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PinAsync(WatchlistRowViewModel? row)
    {
        if (_coordinator is null || row is null)
            return;

        var pin = !row.IsPinned;
        await _coordinator.PinAsync(row.Id, pin);
        StatusMessage = pin ? $"{row.Name} is kept: Smart Select will never suggest it." : $"{row.Name} is no longer pinned.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SnoozeAsync(WatchlistRowViewModel? row)
    {
        if (_coordinator is null || row is null)
            return;

        await _coordinator.SnoozeAsync(row.Id);
        StatusMessage = $"{row.Name} is snoozed for {_coordinator.Options.SnoozeDays} days: not checked and not suggested.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ReenrollAsync(WatchlistRowViewModel? row)
    {
        if (_coordinator is null || row is null)
            return;

        var done = await _coordinator.ReenrollAsync(row.Id);
        StatusMessage = done ? $"{row.Name} is being watched again from today." : $"{row.Name} could not be fingerprinted completely, so it was not restarted.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ReassessAsync(WatchlistRowViewModel? row)
    {
        if (_coordinator is null || row is null)
            return;

        IsLoading = true;
        try
        {
            var result = await _coordinator.ReassessAsync(row.Id);
            StatusMessage = result is null
                ? $"{row.Name} could not be reassessed."
                : $"{row.Name}: {result.Tier} (score {(int)Math.Round(result.JunkScore * 100)}/100).";
        }
        finally
        {
            IsLoading = false;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenInExplorer(WatchlistRowViewModel? row)
    {
        if (_explorer is null || row is null)
            return;

        try
        {
            _explorer.OpenInExplorer(row.FullPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open watched folder in explorer: {Path}", row.FullPath);
            StatusMessage = $"Could not open {row.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Back() => _onHide?.Invoke();

    // ── optional background check-in ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task InstallScheduleAsync()
    {
        if (_scheduler is null || !_scheduler.IsSupported)
            return;

        var d = _scheduler.Describe();
        var message = $"{d.Summary}\n\nIt will run:\n  {d.Command}\n\n" + string.Join("\n", d.Details.Select(line => "  • " + line));
        var agreed = _confirmations is not null && await _confirmations.ShowConfirmationAsync($"Add a daily check-in ({d.Mechanism})?", message, "Add daily check-in");
        if (!agreed)
            return;

        var result = await _scheduler.InstallAsync();
        StatusMessage = result.Message;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveScheduleAsync()
    {
        if (_scheduler is null)
            return;

        var result = await _scheduler.UninstallAsync();
        StatusMessage = result.Message;
        await LoadAsync();
    }
}
