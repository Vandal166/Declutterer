using Declutterer.Domain.Models;
using Declutterer.Domain.Services.Cleanup;

namespace Declutterer.Tests.Cleanup;

/// <summary>
/// End-to-end behaviour of the Smart Select engine. Each test builds a small, realistic folder tree and asserts the
/// verdicts a careful human would reach. The recurring theme: OLD IS NOT USELESS.
/// </summary>
public class JunkAssessmentScenarioTests : IDisposable
{
    private readonly CleanupScenario _s = new();

    public void Dispose() => _s.Dispose();

    // ── regenerable build output ───────────────────────────────────────────

    [Fact]
    public void NodeModules_NextToPackageJson_IsSafe_AndTheProjectItselfIsKept()
    {
        _s.File("Projects/old-app/package.json", 800, 300);
        _s.File("Projects/old-app/src/index.js", 800, 900);
        _s.File("Projects/old-app/node_modules/left-pad/index.js", 800, 500);
        _s.File("Projects/old-app/node_modules/left-pad/package.json", 800, 200);

        var r = _s.Run();

        Assert.True(r.Tier("Projects/old-app/node_modules") == CleanupTier.Safe, r.Explain("Projects/old-app/node_modules"));
        Assert.True(r["Projects/old-app"].Tier <= CleanupTier.Keep, r.Explain("Projects/old-app"));
        Assert.True(r["Projects/old-app/src"].Tier <= CleanupTier.Keep, r.Explain("Projects/old-app/src"));

        Assert.Equal(new[] { "Projects/old-app/node_modules" }, r.Selected(SmartSelectMode.Conservative));
        Assert.Equal(new[] { "Projects/old-app/node_modules" }, r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void DotNetBinAndObj_NextToProjectFile_AreRecognised_ButBinWithoutProjectIsNot()
    {
        _s.File("Code/App/App.csproj", 900, 400);
        _s.File("Code/App/bin/Debug/App.dll", 900, 4000);
        _s.File("Code/App/obj/project.assets.json", 900, 4000);
        _s.File("Docs/bin/important.txt", 900, 4000); // "bin" with no project next to it

        var r = _s.Run();

        Assert.True(r.Has("Code/App/bin", ReasonCode.RegenerableBuildOutput));
        Assert.True(r.Has("Code/App/obj", ReasonCode.RegenerableBuildOutput));
        Assert.False(r.Has("Docs/bin", ReasonCode.RegenerableBuildOutput));
        Assert.True(r.Tier("Docs/bin") <= CleanupTier.Keep);
    }

    [Fact]
    public void ActiveProjectsNodeModules_IsNotSafe_ButStillSelectableInBalanced()
    {
        _s.File("Projects/active/package.json", 2, 300);
        _s.File("Projects/active/node_modules/x/index.js", 2, 500);

        var r = _s.Run();
        var a = r["Projects/active/node_modules"];

        Assert.True(a.Tier is CleanupTier.Likely, r.Explain("Projects/active/node_modules"));
        Assert.DoesNotContain("Projects/active/node_modules", r.Selected(SmartSelectMode.Conservative));
        Assert.Contains("Projects/active/node_modules", r.Selected(SmartSelectMode.Balanced));
    }

    // ── "old" must never be treated as "useless" ───────────────────────────

    [Fact]
    public void VeryOldFolderOfUnknownFiles_IsKept_EvenInAggressiveMode()
    {
        _s.File("Misc/OldStuff/a.dat", 1500, 50_000);
        _s.File("Misc/OldStuff/b.dat", 1500, 50_000);

        var r = _s.Run();

        Assert.True(r.Tier("Misc/OldStuff") == CleanupTier.Keep, r.Explain("Misc/OldStuff"));
        Assert.True(r["Misc/OldStuff"].JunkScore < 0.5);
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void OldPersonalDocuments_AreNeverSelected()
    {
        _s.File("Documents/Taxes/2019-return.pdf", 1500, 100_000);
        _s.File("Documents/Taxes/notes.docx", 1500, 40_000);
        _s.File("Pictures/Holiday/IMG_0001.jpg", 1500, 3_000_000);

        var r = _s.Run();

        Assert.True(r["Documents/Taxes"].DeletionRisk >= 0.6, r.Explain("Documents/Taxes"));
        Assert.Equal(CleanupTier.Keep, r.Tier("Documents/Taxes"));
        Assert.Equal(CleanupTier.Keep, r.Tier("Pictures/Holiday/IMG_0001.jpg"));
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void GitRepository_IsAHardKeep_EvenWhenAncient()
    {
        _s.File("Code/legacy/.git/HEAD", 2000, 40);
        _s.File("Code/legacy/readme.txt", 2000, 500);
        _s.File("Code/legacy/data.bin", 2000, 80_000);

        var r = _s.Run();

        Assert.True(r["Code/legacy"].HasHardKeep, r.Explain("Code/legacy"));
        Assert.True(r["Code/legacy"].DeletionRisk >= 0.95);
        Assert.Equal(CleanupTier.Keep, r.Tier("Code/legacy"));
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void RunningProgramsFolder_AndItsParents_AreHardKeeps()
    {
        var exe = _s.File("Tools/Old/app.exe", 1000, 8000);
        _s.File("Tools/Old/readme.txt", 1000, 100);
        _s.Machine.RunningExecutables.Add(exe);

        var r = _s.Run();

        Assert.True(r.Has("Tools/Old", ReasonCode.RunningProcess), r.Explain("Tools/Old"));
        Assert.True(r["Tools"].HasHardKeep);
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void StartupEntryTarget_IsAHardKeep()
    {
        var exe = _s.File("Tools/Sync/sync.exe", 1000, 8000);
        _s.Machine.StartupAndServiceExecutables.Add(exe);

        var r = _s.Run();

        Assert.True(r.Has("Tools/Sync", ReasonCode.StartupOrServiceReference));
        Assert.Empty(r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void InstalledApplicationFolder_IsAHardKeep()
    {
        _s.File("Games/Cool/game.exe", 700, 9000);
        _s.Machine.InstalledApps.Add(new InstalledApp("Cool Game", "Studio", new[] { _s.Abs("Games/Cool") }));

        var r = _s.Run();

        Assert.True(r.Has("Games/Cool", ReasonCode.InstalledApplication));
        Assert.True(r["Games/Cool"].HasHardKeep);
    }

    [Fact]
    public void CredentialFiles_VetoTheWholeFolder_AndSensitiveFoldersAreProtected()
    {
        _s.File("Backups/old/id_rsa", 1500, 1700);
        _s.File("Backups/old/dump.bak", 1500, 90_000);
        _s.File(".ssh/known_hosts", 1500, 300);

        var r = _s.Run();

        Assert.True(r.Has("Backups/old", ReasonCode.ContainsCredentials), r.Explain("Backups/old"));
        Assert.True(r["Backups/old"].HasHardKeep);
        Assert.True(r[".ssh"].HasHardKeep);
        Assert.True(r[".ssh/known_hosts"].HasHardKeep);

        // A stale .bak next to the key may legitimately be offered for review - but never the key, nor its folder.
        var selected = r.Selected(SmartSelectMode.Aggressive);
        Assert.DoesNotContain("Backups/old", selected);
        Assert.DoesNotContain("Backups/old/id_rsa", selected);
        Assert.DoesNotContain(".ssh", selected);
        Assert.DoesNotContain(".ssh/known_hosts", selected);
    }

    [Fact]
    public void CredentialsInsideDisposableFolderStillVetoFolderSelection()
    {
        _s.File("AppData/Local/Temp/old-extract/id_rsa", 400, 1700);
        _s.File("AppData/Local/Temp/old-extract/setup.tmp", 400, 20_000);

        var r = _s.Run();

        Assert.True(r.Has("AppData/Local/Temp/old-extract", ReasonCode.ContainsCredentials), r.Explain("AppData/Local/Temp/old-extract"));
        Assert.True(r["AppData/Local/Temp/old-extract"].HasHardKeep, r.Explain("AppData/Local/Temp/old-extract"));
        Assert.DoesNotContain("AppData/Local/Temp/old-extract", r.Selected(SmartSelectMode.Aggressive));
    }

    // ── disposable locations ───────────────────────────────────────────────

    [Fact]
    public void OldTempFolder_IsSafe_AndFreshTempFolderIsLikelyButNotSafe()
    {
        _s.File("AppData/Local/Temp/old-extract/setup.tmp", 400, 20_000);
        _s.File("AppData/Local/Temp/old-extract/data.cab", 400, 20_000);
        _s.File("AppData/Local/Temp/fresh-extract/setup.tmp", 10, 20_000);

        var r = _s.Run();

        Assert.True(r.Tier("AppData/Local/Temp/old-extract") == CleanupTier.Safe, r.Explain("AppData/Local/Temp/old-extract"));
        Assert.True(r.Tier("AppData/Local/Temp/fresh-extract") == CleanupTier.Likely, r.Explain("AppData/Local/Temp/fresh-extract"));
        Assert.Contains("AppData/Local/Temp/old-extract", r.Selected(SmartSelectMode.Conservative));
        Assert.DoesNotContain("AppData/Local/Temp/fresh-extract", r.Selected(SmartSelectMode.Conservative));
        Assert.Contains("AppData/Local/Temp/fresh-extract", r.Selected(SmartSelectMode.Balanced));
    }

    [Fact]
    public void ContainerFolders_AreNeverSelectedThemselves_OnlyTheirContents()
    {
        _s.File("AppData/Local/Temp/a/x.tmp", 400, 20_000);
        _s.File("AppData/Local/Temp/b/y.tmp", 400, 20_000);
        _s.File("Downloads/old1.tmp", 400, 1000);
        _s.File("Downloads/old2.tmp", 400, 1000);

        var r = _s.Run();
        var selected = r.Selected(SmartSelectMode.Aggressive);

        Assert.True(r["AppData/Local/Temp"].HasHardKeep, r.Explain("AppData/Local/Temp"));
        Assert.True(r["Downloads"].HasHardKeep);
        Assert.DoesNotContain("AppData/Local/Temp", selected);
        Assert.DoesNotContain("AppData/Local", selected);
        Assert.DoesNotContain("Downloads", selected);
        Assert.Contains("AppData/Local/Temp/a", selected);
        Assert.Contains("AppData/Local/Temp/b", selected);
        Assert.Contains("Downloads/old1.tmp", selected);
    }

    [Fact]
    public void BrowserCache_IsJunk_ButTheBrowserProfileAroundItIsKept()
    {
        _s.File("AppData/Local/Google/Chrome/User Data/Default/Cache/data_0", 400, 500_000);
        _s.File("AppData/Local/Google/Chrome/User Data/Default/Bookmarks", 400, 5_000);
        _s.File("AppData/Local/Google/Chrome/User Data/Default/Login Data", 400, 40_000);

        var r = _s.Run();
        const string profile = "AppData/Local/Google/Chrome/User Data/Default";

        Assert.True(r.Tier(profile + "/Cache") >= CleanupTier.Likely, r.Explain(profile + "/Cache"));
        Assert.True(r.Has(profile, ReasonCode.BrowserProfile));
        Assert.True(r.Tier(profile) <= CleanupTier.Keep, r.Explain(profile));
        Assert.Equal(new[] { profile + "/Cache" }, r.Selected(SmartSelectMode.Balanced));
    }

    [Fact]
    public void WindowsManagedAppData_IsKept_ButItsInternetCacheIsNot()
    {
        _s.File("AppData/Local/Microsoft/Windows/INetCache/IE/a.tmp", 400, 10_000);
        _s.File("AppData/Local/Microsoft/Windows/Notifications/wpndatabase.db", 400, 10_000);
        _s.File("AppData/Roaming/Microsoft/Protect/S-1-5-21/key", 900, 600);

        var r = _s.Run();

        Assert.True(r.Tier("AppData/Local/Microsoft/Windows/INetCache") >= CleanupTier.Likely, r.Explain("AppData/Local/Microsoft/Windows/INetCache"));
        Assert.True(r.Tier("AppData/Local/Microsoft/Windows/Notifications") <= CleanupTier.Keep);
        Assert.True(r["AppData/Roaming/Microsoft/Protect"].HasHardKeep);
    }

    // ── leftovers of removed applications ──────────────────────────────────

    [Fact]
    public void AppDataOfUninstalledApp_IsReviewOrBetter_WhileInstalledAppsDataIsKept()
    {
        _s.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
        _s.Machine.InstalledApps.Add(new InstalledApp("Visual Studio Code", "Microsoft Corporation", Array.Empty<string>()));

        _s.File("AppData/Roaming/OldTool/settings.json", 500, 3000);
        _s.File("AppData/Roaming/OldTool/session.dat", 500, 9000);
        _s.File("AppData/Roaming/Discord/settings.json", 500, 3000);
        _s.File("AppData/Roaming/Code/User/settings.json", 500, 3000);
        _s.File("AppData/Local/GoneApp/app.log", 500, 30_000);
        _s.File("AppData/Local/GoneApp/cache.tmp", 500, 30_000);

        var r = _s.Run();

        Assert.True(r.Has("AppData/Roaming/OldTool", ReasonCode.OrphanedAppData), r.Explain("AppData/Roaming/OldTool"));
        Assert.Equal(CleanupTier.Review, r.Tier("AppData/Roaming/OldTool"));
        Assert.Equal(CleanupTier.Keep, r.Tier("AppData/Roaming/Discord"));
        Assert.Equal(CleanupTier.Keep, r.Tier("AppData/Roaming/Code"));
        Assert.False(r.Has("AppData/Roaming/Discord", ReasonCode.OrphanedAppData));

        Assert.True(r.Tier("AppData/Local/GoneApp") == CleanupTier.Likely, r.Explain("AppData/Local/GoneApp"));

        var balanced = r.Selected(SmartSelectMode.Balanced);
        Assert.Contains("AppData/Local/GoneApp", balanced);
        Assert.DoesNotContain("AppData/Roaming/OldTool", balanced);
        Assert.Contains("AppData/Roaming/OldTool", r.Selected(SmartSelectMode.Aggressive));
        Assert.DoesNotContain("AppData/Roaming/Discord", r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void UnmatchedButActiveAppData_IsNotCalledAnOrphan()
    {
        _s.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
        _s.File("AppData/Roaming/PortableThing/state.cfg", 5, 3000);

        var r = _s.Run();

        Assert.False(r.Has("AppData/Roaming/PortableThing", ReasonCode.OrphanedAppData));
        Assert.Equal(CleanupTier.Keep, r.Tier("AppData/Roaming/PortableThing"));
    }

    [Fact]
    public void WithoutInstalledProgramInventory_NothingIsCalledAnOrphan()
    {
        _s.Machine.HasInstalledAppInventory = false; // e.g. registry unreadable
        _s.File("AppData/Roaming/OldTool/settings.json", 900, 3000);

        var r = _s.Run();

        Assert.False(r.Has("AppData/Roaming/OldTool", ReasonCode.OrphanedAppData));
    }

    [Fact]
    public void SharedOsFolders_AreNeverOrphans()
    {
        _s.Machine.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
        _s.File("AppData/Local/Programs/Something/x.bin", 900, 3000);
        _s.File("AppData/Local/Packages/Contoso.App_abc/settings.dat", 900, 3000);

        var r = _s.Run();

        Assert.False(r.Has("AppData/Local/Programs", ReasonCode.OrphanedAppData));
        Assert.True(r["AppData/Local/Packages"].HasHardKeep);
    }

    // ── file level evidence ────────────────────────────────────────────────

    [Fact]
    public void IdenticalCopy_IsJunk_ButSameSizeDifferentContentIsNot()
    {
        var a = new byte[20_000];
        new Random(1).NextBytes(a);
        var b = (byte[])a.Clone();
        var c = (byte[])a.Clone();
        c[10_000] ^= 0xFF; // same size, differs in the middle

        _s.Bytes("Downloads/photo.jpg", a, 400);
        _s.Bytes("Downloads/photo (1).jpg", b, 400);
        _s.Bytes("Downloads/report.pdf", a, 400);
        _s.Bytes("Downloads/report (1).pdf", c, 400);

        var r = _s.Run();

        Assert.True(r.Has("Downloads/photo (1).jpg", ReasonCode.DuplicateOfSibling));
        Assert.True(r.Tier("Downloads/photo (1).jpg") >= CleanupTier.Likely, r.Explain("Downloads/photo (1).jpg"));
        Assert.False(r.Has("Downloads/report (1).pdf", ReasonCode.DuplicateOfSibling));
        Assert.Equal(CleanupTier.Keep, r.Tier("Downloads/report (1).pdf"));
        Assert.Equal(CleanupTier.Keep, r.Tier("Downloads/photo.jpg")); // the original is never touched
    }

    [Fact]
    public void OldInstaller_IsLikelyJunkOnlyWhenTheAppIsAlreadyInstalled()
    {
        _s.Machine.InstalledApps.Add(new InstalledApp("Zoom Workplace (64-bit)", "Zoom", Array.Empty<string>()));

        _s.File("Downloads/ZoomInstaller.msi", 800, 40_000);
        _s.File("Downloads/Setup_UnknownTool.exe", 800, 40_000);

        var r = _s.Run();

        Assert.True(r.Has("Downloads/ZoomInstaller.msi", ReasonCode.InstallerOfInstalledApp));
        Assert.True(r.Tier("Downloads/ZoomInstaller.msi") >= CleanupTier.Likely, r.Explain("Downloads/ZoomInstaller.msi"));
        Assert.Equal(CleanupTier.Keep, r.Tier("Downloads/Setup_UnknownTool.exe"));
    }

    [Fact]
    public void ArchiveWithExtractedSibling_IsLikelyJunk_UnlessRecentlyOpened()
    {
        _s.File("Downloads/tool-1.2.zip", 1000, 40_000);
        _s.File("Downloads/tool-1.2/tool.exe", 1000, 40_000);
        _s.File("Downloads/data.zip", 1000, 40_000);
        _s.File("Downloads/data/a.txt", 1000, 40_000);
        _s.Machine.RecentItems.Add(new RecentItem(_s.Abs("Downloads/data.zip"), DateTime.UtcNow.AddDays(-10)));

        var r = _s.Run();

        Assert.True(r.Has("Downloads/tool-1.2.zip", ReasonCode.ArchiveAlreadyExtracted));
        Assert.True(r.Tier("Downloads/tool-1.2.zip") >= CleanupTier.Likely, r.Explain("Downloads/tool-1.2.zip"));
        Assert.True(r.Has("Downloads/data.zip", ReasonCode.RecentlyOpened));
        Assert.Equal(CleanupTier.Keep, r.Tier("Downloads/data.zip"));
    }

    [Fact]
    public void EmptyOldFolder_IsJunk_ButAFreshEmptyFolderIsNot()
    {
        _s.Dir("Misc/EmptyOld", 800);
        _s.Dir("Misc/EmptyNew", 1);
        _s.File("Misc/keep.txt", 800, 100);

        var r = _s.Run();

        Assert.True(r.Tier("Misc/EmptyOld") >= CleanupTier.Likely, r.Explain("Misc/EmptyOld"));
        Assert.Equal(CleanupTier.Keep, r.Tier("Misc/EmptyNew"));
    }

    // ── cloud / budget / safety gate ───────────────────────────────────────

    [Fact]
    public void CloudSyncedFolder_RaisesRisk_AndPreventsSafe()
    {
        _s.Machine.SyncedRoots.Add(_s.Abs("OneDrive"));
        _s.File("OneDrive/Projects/app/package.json", 800, 300);
        _s.File("OneDrive/Projects/app/node_modules/x/index.js", 800, 900);

        var r = _s.Run();
        var a = r["OneDrive/Projects/app/node_modules"];

        Assert.True(r.Has("OneDrive/Projects/app/node_modules", ReasonCode.SyncedFolder));
        Assert.True(a.DeletionRisk >= 0.3 - 1e-9);
        Assert.Equal(CleanupTier.Likely, a.Tier);
        Assert.DoesNotContain("OneDrive/Projects/app/node_modules", r.Selected(SmartSelectMode.Conservative));
    }

    [Fact]
    public void ExhaustedTimeBudget_CanNeverProduceSafe()
    {
        _s.Options.TimeBudget = TimeSpan.Zero;
        _s.File("AppData/Local/Temp/old-extract/setup.tmp", 400, 20_000);

        var r = _s.Run();

        Assert.True(r.Analysis.BudgetExceeded);
        Assert.True(r.Tier("AppData/Local/Temp/old-extract") < CleanupTier.Safe, r.Explain("AppData/Local/Temp/old-extract"));
        Assert.DoesNotContain("AppData/Local/Temp/old-extract", r.Selected(SmartSelectMode.Conservative));
    }

    [Fact]
    public void DisablingDeepInspection_StillWorksButNeverReachesSafeForFolders()
    {
        _s.Options.EnableDeepInspection = false;
        _s.File("Projects/old-app/package.json", 800, 300);
        _s.File("Projects/old-app/node_modules/x/index.js", 800, 900);

        var r = _s.Run();

        Assert.Equal(0, r.Analysis.DirectoriesInspected);
        Assert.True(r.Tier("Projects/old-app/node_modules") < CleanupTier.Safe);
        Assert.True(r.Tier("Projects/old-app/node_modules") >= CleanupTier.Review);
    }

    [Fact]
    public void SafetyGate_TurnsAnythingItBlocksIntoProtected()
    {
        _s.SafetyGate = p => p.EndsWith("old-extract", StringComparison.OrdinalIgnoreCase) ? "blocked by test gate" : null;
        _s.File("AppData/Local/Temp/old-extract/setup.tmp", 400, 20_000);
        _s.File("AppData/Local/Temp/other/setup.tmp", 400, 20_000);

        var r = _s.Run();

        Assert.Equal(CleanupTier.Protected, r.Tier("AppData/Local/Temp/old-extract"));
        Assert.True(r.Has("AppData/Local/Temp/old-extract", ReasonCode.SystemProtected));
        Assert.DoesNotContain("AppData/Local/Temp/old-extract", r.Selected(SmartSelectMode.Aggressive));
        Assert.Contains("AppData/Local/Temp/other", r.Selected(SmartSelectMode.Aggressive));
    }

    [Fact]
    public void SelectionNeverContainsNestedItems_AndNeverTheRoot()
    {
        _s.File("AppData/Local/Temp/a/b/c/x.tmp", 400, 20_000);
        _s.File("AppData/Local/Temp/a/y.tmp", 400, 20_000);

        var r = _s.Run();
        var selected = r.Selected(SmartSelectMode.Aggressive);

        Assert.DoesNotContain(".", selected);
        for (int i = 0; i < selected.Count; i++)
            for (int j = 0; j < selected.Count; j++)
                if (i != j)
                    Assert.False(selected[j].StartsWith(selected[i] + "/"), $"{selected[j]} is nested in {selected[i]}");
    }

    [Fact]
    public void EveryAssessmentIsWithinRange_AndExplainsItself()
    {
        _s.File("Projects/a/package.json", 100, 300);
        _s.File("Projects/a/node_modules/x/y.js", 100, 300);
        _s.File("Documents/a.docx", 700, 3000);
        _s.File("AppData/Local/Temp/t.tmp", 3, 100);
        _s.Dir("Empty", 900);

        var r = _s.Run();

        foreach (var a in r.Analysis.Assessments.Values)
        {
            Assert.InRange(a.JunkScore, 0.0, 1.0);
            Assert.InRange(a.DeletionRisk, 0.0, 1.0);
            Assert.InRange(a.Confidence, 0.0, 1.0);
            Assert.Contains("%", a.Summary);
        }
    }

    [Fact]
    public void FreshlyCopiedOldFile_IsTreatedAsAlive_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return; // creation time can only be set on Windows (and macOS); the logic itself is shared

        _s.Options.UseCreationTime = true;
        var copied = _s.File("Downloads/archive-from-backup.dat", 2000, 20_000);
        _s.Run(); // applies write times
        File.SetCreationTimeUtc(copied, DateTime.UtcNow.AddDays(-1)); // copied yesterday, content is 2000 days old

        var r = _s.Run();

        Assert.False(r.Has("Downloads/archive-from-backup.dat", ReasonCode.LongUntouched));
        Assert.Equal(CleanupTier.Keep, r.Tier("Downloads/archive-from-backup.dat"));
    }
}
