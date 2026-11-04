using System;
using Declutterer.Domain.Models;

namespace Declutterer.Domain.Services.Observation;

/// <summary>The texts the user reads before observation is switched on and before folders are added to the watchlist.</summary>
public static class ObservationConsentText
{
    /// <summary>Exactly what is stored and for how long. Shown the first time the feature is switched on.</summary>
    public static string Build(ObservationOptions options, string ledgerPath) =>
        "Declutterer can watch suspicious or stale folders for a few weeks and use observed activity as evidence for Smart Select.\n" +
        "\n" +
        $"What is stored (only on this computer, in {ledgerPath}):\n" +
        "  • each watched folder's path, size and a fingerprint of its file names, sizes and modified dates\n" +
        "  • for every check: the result, and how many entries were added, changed or removed\n" +
        "  • the days on which this computer was in use\n" +
        "It never reads file contents and never records which files you open.\n" +
        "\n" +
        $"How long: finished items are forgotten after {options.ResolvedRetentionDays} days and check records after {options.CheckInRetentionDays} days. " +
        "You can delete everything at once in the Watchlist.\n" +
        "\n" +
        "Nothing is deleted because of this. It only changes what Smart Select suggests, and you still confirm every deletion.";

    /// <summary>What adding the suggested folders to the watchlist means, for the confirmation before it happens.</summary>
    public static string BuildEnrollment(int count, long totalBytes, ObservationOptions options, params ObservationClass[] classes)
    {
        var longest = 0;
        var shortest = int.MaxValue;
        foreach (var c in classes)
        {
            var days = options.RequiredDaysFor(c);
            longest = Math.Max(longest, days);
            shortest = Math.Min(shortest, days);
        }
        if (classes.Length == 0)
            longest = shortest = options.GenericFolderDays;

        var window = shortest == longest ? $"{longest} days" : $"{shortest} to {longest} days";
        var size = Declutterer.Utilities.Helpers.ByteConverter.ToReadableString(totalBytes);
        return $"Watch {count} folder(s) ({size} in total)?\n" +
               "\n" +
               $"Each has to stay unused for {window} on which this computer is turned on (so it can take longer in calendar time). " +
               "If something uses a folder, its clock starts again. Watching does not select or delete folders.\n" +
               "\n" +
               "Nothing is deleted. You can stop watching, pin or snooze any of them in the Watchlist.";
    }
}
