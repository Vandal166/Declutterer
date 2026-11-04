using System;
using System.Collections.Generic;
using System.Linq;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

public readonly record struct CoverageEvaluation(int CoveredDays, int CalendarDays, bool PoorCoverage);

/// <summary>
/// Counts covered days. A covered day is a UTC date on which the machine was proven to be in use (a check-in ran, or the volume
/// showed activity). An item's covered days are the heartbeat days strictly after its quiet-since day: a laptop that slept
/// through a month contributes nothing for that month, so 30 calendar days with 3 heartbeat days are 3 covered days.
/// The caller only turns this into progress when the comparison itself was conclusive.
/// </summary>
public sealed class CoverageTracker
{
    private readonly DateTime[] _days;

    public CoverageTracker(IEnumerable<DateTime> heartbeatDaysUtc)
    {
        _days = heartbeatDaysUtc.Select(d => d.ToUniversalTime().Date).Distinct().OrderBy(d => d).ToArray();
    }

    public int CoveredDaysAfter(DateTime quietSinceUtc, DateTime throughUtc)
    {
        var after = quietSinceUtc.ToUniversalTime().Date;
        var through = throughUtc.ToUniversalTime().Date;
        int count = 0;
        foreach (var d in _days)
        {
            if (d > after && d <= through)
                count++;
        }
        return count;
    }

    public CoverageEvaluation Evaluate(WatchEntry entry, DateTime nowUtc, ObservationOptions options)
    {
        var now = nowUtc.ToUniversalTime().Date;
        var quietSince = entry.QuietSinceUtc.ToUniversalTime().Date;
        var checkedThrough = entry.LastCheckInUtc is { } lastCheckIn && lastCheckIn.ToUniversalTime().Date > quietSince
            ? lastCheckIn.ToUniversalTime().Date
            : quietSince;
        var covered = entry.CoveredDays + CoveredDaysAfterExcludingSnooze(checkedThrough, now, entry);
        var calendar = Math.Max(0, (now - quietSince).Days - SnoozedDaysAfter(quietSince, now, entry));
        bool poor = calendar > options.CoverageGraceFactor * entry.RequiredDays && covered < entry.RequiredDays;
        return new CoverageEvaluation(covered, calendar, poor);
    }

    private int CoveredDaysAfterExcludingSnooze(DateTime afterUtc, DateTime throughUtc, WatchEntry entry)
    {
        if (entry.SnoozedAtUtc is not { } snoozedAt || entry.SnoozeUntilUtc is not { } snoozeUntil)
            return CoveredDaysAfter(afterUtc, throughUtc);

        var start = snoozedAt.ToUniversalTime().Date;
        var end = snoozeUntil.ToUniversalTime().Date;
        var after = afterUtc.ToUniversalTime().Date;
        var through = throughUtc.ToUniversalTime().Date;
        return _days.Count(day => day > after && day <= through && (day < start || day > end));
    }

    private static int SnoozedDaysAfter(DateTime quietSinceUtc, DateTime throughUtc, WatchEntry entry)
    {
        if (entry.SnoozedAtUtc is not { } snoozedAt || entry.SnoozeUntilUtc is not { } snoozeUntil)
            return 0;

        var start = Max(snoozedAt.ToUniversalTime().Date, quietSinceUtc.ToUniversalTime().Date.AddDays(1));
        var end = Min(snoozeUntil.ToUniversalTime().Date, throughUtc.ToUniversalTime().Date);
        return end < start ? 0 : (end - start).Days + 1;
    }

    private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;
}
