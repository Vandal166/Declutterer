namespace Declutterer.Domain.Models;

/// <summary>
/// Machine-readable reason for a part of a <see cref="JunkAssessment"/>.
/// Every verdict the Smart Select engine reaches is explained by a list of these.
/// </summary>
public enum ReasonCode
{
    // ── Evidence that the item is disposable ────────────────────────────────
    KnownCacheLocation,
    TempLocation,
    CrashOrDiagnosticData,
    RegenerableBuildOutput,
    TempFile,
    BackupOrLeftoverFile,
    LogFile,
    StaleInstaller,
    InstallerOfInstalledApp,
    ArchiveAlreadyExtracted,
    DuplicateOfSibling,
    EmptyDirectory,
    EmptyFile,
    OnlyDisposableContent,
    OrphanedAppData,
    LongUntouched,

    // ── Evidence that the item is in use / valuable / dangerous to delete ───
    SystemProtected,
    SensitiveLocation,
    ContainerFolder,
    RunningProcess,
    StartupOrServiceReference,
    InstalledApplication,
    RecentlyOpened,
    RecentlyModified,
    RecentlyAccessed,
    ContainsUserDocuments,
    ContainsCredentials,
    ContainsExecutables,
    VersionControlRepo,
    ProjectRoot,
    SyncedFolder,
    UserSettingsOrSaves,
    BrowserProfile,
    IncompleteEvidence,

    // ── Evidence from observing an item over time (see docs/Observation.md) ──
    /// <summary>It was seen in use (or only weakly touched) while being watched.</summary>
    ObservedActive,
    /// <summary>It stayed untouched for its whole observation window. Bounded, expiring evidence.</summary>
    ObservedInactive,
    /// <summary>Observation could not reach a conclusion (incomplete walks, too few days the machine was on).</summary>
    ObservationInconclusive,
    /// <summary>It was moved or renamed while being watched.</summary>
    ObservedMoved,
    /// <summary>The user pinned or snoozed it: never suggested for deletion.</summary>
    PinnedByUser,
    /// <summary>A folder below this one is pinned or snoozed in the Watchlist: deleting this one would delete it too.</summary>
    ContainsKeptFolder,
}

public static class ReasonCodeExtensions
{
    /// <summary>Short human readable title shown in tooltips and the review window.</summary>
    public static string ToTitle(this ReasonCode code) => code switch
    {
        ReasonCode.KnownCacheLocation => "Known cache location",
        ReasonCode.TempLocation => "Temporary folder",
        ReasonCode.CrashOrDiagnosticData => "Crash / diagnostic data",
        ReasonCode.RegenerableBuildOutput => "Regenerable build output",
        ReasonCode.TempFile => "Temporary file",
        ReasonCode.BackupOrLeftoverFile => "Backup / leftover file",
        ReasonCode.LogFile => "Log file",
        ReasonCode.StaleInstaller => "Installer / disk image",
        ReasonCode.InstallerOfInstalledApp => "Installer of an installed app",
        ReasonCode.ArchiveAlreadyExtracted => "Archive already extracted",
        ReasonCode.DuplicateOfSibling => "Duplicate of a sibling file",
        ReasonCode.EmptyDirectory => "Empty directory",
        ReasonCode.EmptyFile => "Empty file",
        ReasonCode.OnlyDisposableContent => "Only disposable content",
        ReasonCode.OrphanedAppData => "Leftover of a removed application",
        ReasonCode.LongUntouched => "Long untouched",
        ReasonCode.SystemProtected => "Protected system location",
        ReasonCode.SensitiveLocation => "Sensitive location",
        ReasonCode.ContainerFolder => "System / user container folder",
        ReasonCode.RunningProcess => "In use by a running program",
        ReasonCode.StartupOrServiceReference => "Referenced by startup / service",
        ReasonCode.InstalledApplication => "Belongs to an installed application",
        ReasonCode.RecentlyOpened => "Recently opened",
        ReasonCode.RecentlyModified => "Recently modified",
        ReasonCode.RecentlyAccessed => "Recently accessed",
        ReasonCode.ContainsUserDocuments => "Contains user documents / media",
        ReasonCode.ContainsCredentials => "Contains credentials / keys",
        ReasonCode.ContainsExecutables => "Contains executables",
        ReasonCode.VersionControlRepo => "Contains a version-control repository",
        ReasonCode.ProjectRoot => "Looks like a project folder",
        ReasonCode.SyncedFolder => "Inside a cloud-synced folder",
        ReasonCode.UserSettingsOrSaves => "User settings / saved games",
        ReasonCode.BrowserProfile => "Browser profile",
        ReasonCode.IncompleteEvidence => "Incomplete evidence",
        ReasonCode.ObservedActive => "Observed in use",
        ReasonCode.ObservedInactive => "Observed unused",
        ReasonCode.ObservationInconclusive => "Observation inconclusive",
        ReasonCode.ObservedMoved => "Observed moved or renamed",
        ReasonCode.PinnedByUser => "Kept by you",
        ReasonCode.ContainsKeptFolder => "Contains a folder you kept",
        _ => code.ToString()
    };
}
