using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Declutterer.Domain.Services.Observation;

/// <summary>One sampled file, used to decide whether last-access timestamps are maintained at all.</summary>
public readonly record struct AccessSample(DateTime AccessUtc, DateTime WriteUtc, DateTime CreatedUtc);

/// <summary>
/// A metadata-only picture of a file or folder tree: a hash over every entry's (relative path, kind, size, last-write time),
/// plus the hashes of the first-level children so a change can be attributed. No file content is ever read.
/// Three root hashes make "only the shell wrote here" decidable at any depth:
/// <see cref="RootHash"/> covers everything, <see cref="SignificantHash"/> leaves out Thumbs.db / desktop.ini / .DS_Store
/// and directory times, <see cref="DroppingHash"/> covers only those droppings.
/// </summary>
public sealed record Fingerprint
{
    public string RootHash { get; init; } = string.Empty;
    public string SignificantHash { get; init; } = string.Empty;
    public string DroppingHash { get; init; } = string.Empty;

    /// <summary>First-level entry name to a short hash of that entry's whole subtree (capped; see <see cref="ChildrenTruncated"/>).</summary>
    public Dictionary<string, string> Children { get; init; } = new();
    public bool ChildrenTruncated { get; init; }

    public int EntryCount { get; init; }
    public long TotalBytes { get; init; }
    public DateTime NewestWriteUtc { get; init; }
    /// <summary>Newest last-access time of any FILE (directory access times are polluted by our own listing).</summary>
    public DateTime NewestAccessUtc { get; init; }
    /// <summary>Number of files accessed after <c>FingerprintWalkOptions.AccessedSinceUtc</c>.</summary>
    public int FilesAccessedSince { get; init; }
    public int FileCount { get; init; }
    public int ReparsePointCount { get; init; }
    public int UnreadableCount { get; init; }
    /// <summary>The walk hit a budget (entries, depth, time). The hashes then only describe part of the tree.</summary>
    public bool Truncated { get; init; }
    public DateTime CapturedUtc { get; init; }

    /// <summary>Only a complete fingerprint can be compared: a truncated or partly unreadable one never proves quiet.</summary>
    [JsonIgnore]
    public bool IsComplete => !Truncated && UnreadableCount == 0;

    /// <summary>Not persisted.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessSample> AccessSamples { get; init; } = Array.Empty<AccessSample>();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static Fingerprint? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Fingerprint>(json, Json);
        }
        catch (JsonException)
        {
            return null; // an unreadable fingerprint is treated as "no baseline" (inconclusive), never as equal
        }
    }
}

/// <summary>The difference between two complete fingerprints.</summary>
public sealed record ChangeReport(bool Comparable, bool Identical, bool ShellDroppingsOnly, int Created, int Modified, int Deleted)
{
    public static readonly ChangeReport NotComparable = new(false, false, false, 0, 0, 0);

    public int Total => Created + Modified + Deleted;

    public string Describe() =>
        !Comparable ? "not comparable"
        : Identical ? "unchanged"
        : ShellDroppingsOnly ? "only shell files (Thumbs.db, desktop.ini, .DS_Store) changed"
        : $"{Created} created, {Modified} modified, {Deleted} deleted (top level)";
}
