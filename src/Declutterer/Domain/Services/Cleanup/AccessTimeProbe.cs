using System;
using System.Collections.Generic;

namespace Declutterer.Domain.Services.Cleanup;

/// <summary>
/// NTFS frequently has last-access updates switched off (and Windows 10+ manages it itself). When they are off, access time
/// equals creation time forever. If a sample of files shows access times clearly after creation, the timestamps are alive and
/// can be used as usage evidence; otherwise they are ignored. Shared by the Smart Select engine and the observation feature.
/// </summary>
public static class AccessTimeProbe
{
    public const int MinSamples = 20;
    public const double MinLiveShare = 0.05;

    /// <summary>
    /// <paramref name="samples"/> are files as (access, write, created). Files written within the last 7 days are skipped,
    /// because a fresh file always looks "accessed after creation".
    /// </summary>
    public static bool LooksMaintained(IEnumerable<(DateTime AccessUtc, DateTime WriteUtc, DateTime CreatedUtc)> samples, DateTime nowUtc)
    {
        int sampled = 0, live = 0;
        foreach (var (access, write, created) in samples)
        {
            if ((nowUtc - write).TotalDays < 7)
                continue;
            if (created.Year < 1700)
                continue;

            sampled++;
            if ((access - created).TotalDays >= 1)
                live++;
            if (sampled >= 1000)
                break;
        }
        return sampled >= MinSamples && live / (double)sampled >= MinLiveShare;
    }
}
