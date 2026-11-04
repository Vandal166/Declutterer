using System;
using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;
using Declutterer.Domain.Services.Cleanup.Rules;
using Declutterer.Domain.Services.Observation;
using Declutterer.Utilities.Extensions;
using Declutterer.Utilities.Helpers;

namespace Declutterer.UI.ViewModels;

/// <summary>One line of the watchlist: a watch entry with everything formatted for display.</summary>
public sealed class WatchlistRowViewModel : ViewModelBase
{
    private readonly DateTime _nowUtc;

    public WatchlistRowViewModel(WatchEntry entry, DateTime nowUtc)
    {
        Entry = entry;
        _nowUtc = nowUtc;
    }

    public WatchEntry Entry { get; }

    public long Id => Entry.Id;

    public string Name
    {
        get
        {
            var name = System.IO.Path.GetFileName(PathKey.Normalize(Entry.DisplayPath));
            return string.IsNullOrEmpty(name) ? Entry.DisplayPath : name;
        }
    }

    public string FullPath => Entry.DisplayPath;

    public string DisplayPath => PathExtensions.GetMiddleEllipsis(Entry.DisplayPath, 80);

    public string SizeText => ByteConverter.ToReadableString(Entry.SizeAtEnrollment);

    public string ClassText => Entry.Class switch
    {
        ObservationClass.AppData => "App data",
        ObservationClass.ProjectFolder => "Project folder",
        _ => "Folder",
    };

    public bool IsPinned => Entry.Pinned;

    public bool IsSnoozed => Entry.SnoozeUntilUtc is { } until && until > _nowUtc && !Entry.Pinned;

    public bool IsActive => Entry.IsActive;

    public bool CanReenroll => Entry.State is WatchState.Unobservable or WatchState.Retired;

    public bool CanReassess => Entry.State == WatchState.QuietEligible;

    public bool CanSnooze => Entry.IsActive && !IsSnoozed;

    public bool CanStop => !Entry.Pinned && Entry.State != WatchState.Resolved;

    public string PinButtonText => Entry.Pinned ? "Unpin" : "Keep (pin)";

    public string StateText
    {
        get
        {
            var text = Entry.State switch
            {
                WatchState.Watching => $"Watching: {Entry.CoveredDays} of {Entry.RequiredDays} days observed",
                WatchState.Alive => Entry.AliveUntilUtc is { } until ? $"In use: counts until {Local(until)}" : "In use",
                WatchState.QuietEligible => Entry.ValidUntilUtc is { } valid
                    ? $"Unused for {Entry.CoveredDays} days: evidence valid until {Local(valid)}"
                    : $"Unused for {Entry.CoveredDays} days",
                WatchState.Unobservable => "Given up: it could not be checked reliably",
                WatchState.Retired => "Kept coming back to life, so it is no longer watched",
                WatchState.Resolved when Entry.Pinned => "Pinned: never suggested for deletion",
                _ => Entry.Resolution.ToString(),
            };
            return IsSnoozed ? $"{text} (snoozed until {Local(Entry.SnoozeUntilUtc!.Value)})" : text;
        }
    }

    public string StateColor => Entry.State switch
    {
        WatchState.Watching => "#4A90E2",
        WatchState.Alive => "#B7791F",
        WatchState.QuietEligible => "#2D7D2D",
        WatchState.Resolved when Entry.Pinned => "#1E40AF",
        _ => "#888888",
    };

    public double ProgressPercent => Entry.State switch
    {
        WatchState.QuietEligible => 100,
        WatchState.Watching when Entry.RequiredDays > 0 => Math.Clamp(Entry.CoveredDays * 100.0 / Entry.RequiredDays, 0, 100),
        _ => 0,
    };

    public bool ShowProgress => Entry.State is WatchState.Watching or WatchState.QuietEligible;

    public string LastActivityText
    {
        get
        {
            if (Entry.LastActiveUtc is not { } at)
                return "Nothing seen yet";

            var kind = Entry.LastActiveStrength == UseStrength.Strong ? "Use" : "Weak signal";
            var ago = OwnershipRule.Ago(Math.Max(0, (_nowUtc - at).TotalDays));
            return string.IsNullOrWhiteSpace(Entry.LastActiveDetail) ? $"{kind} {ago}" : $"{kind} {ago}: {Entry.LastActiveDetail}";
        }
    }

    public string VerdictText => Entry.LastTier is { } tier && Entry.LastScore is { } score
        ? $"{TierLabel(tier)} (score {(int)Math.Round(score * 100)}/100)"
        : "Not reassessed yet";

    /// <summary>The reasons behind the last verdict, for the tooltip.</summary>
    public string VerdictDetails => Entry.LastReasons ?? "The verdict is worked out when the watching period is complete.";

    public string LastCheckInText => Entry.LastCheckInUtc is { } t ? $"Last checked {Local(t)}" : "Not checked yet";

    private static string TierLabel(CleanupTier tier) => tier switch
    {
        CleanupTier.Safe => "Safe to delete",
        CleanupTier.Likely => "Likely junk",
        CleanupTier.Review => "Review",
        CleanupTier.Protected => "Protected",
        _ => "Keep",
    };

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("d");
}
