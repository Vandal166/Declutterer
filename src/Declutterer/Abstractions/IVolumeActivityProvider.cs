using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Declutterer.Abstractions;

/// <summary>What a volume's change journal says about the days since the last time it was read.</summary>
/// <param name="Available">False when there is no usable journal (not Windows, not NTFS, no privilege ...).</param>
/// <param name="ActiveDaysUtc">UTC dates on which the volume showed activity: proof the machine was in use.</param>
/// <param name="JournalWrapped">The journal was reset or overwritten since the stored cursor: a coverage gap, not evidence of quiet.</param>
public sealed record VolumeActivityReport(bool Available, IReadOnlyList<DateTime> ActiveDaysUtc, bool JournalWrapped, string? Detail);

/// <summary>Optional extra heartbeat (Windows: NTFS USN journal). Without one, check-in days are the only heartbeat.</summary>
public interface IVolumeActivityProvider
{
    Task<VolumeActivityReport> ReadAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
}
