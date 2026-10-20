using Declutterer.Domain.Services.Cleanup;
using Declutterer.Integration.SystemInfo;

namespace Declutterer.Tests.Cleanup;

public class SystemSnapshotTests
{
    private static SystemSnapshot Snapshot(Action<SystemSnapshotData> setup)
    {
        var d = new SystemSnapshotData { HasInstalledAppInventory = true };
        setup(d);
        return new SystemSnapshot(d);
    }

    private static string P(params string[] parts) => string.Join(Path.DirectorySeparatorChar, parts);

    [Theory]
    [InlineData("Code", true)]            // folder of "Visual Studio Code"
    [InlineData("JetBrains", true)]       // vendor folder
    [InlineData("Discord", true)]
    [InlineData("1Password", true)]
    [InlineData("obs-studio", true)]      // "OBS Studio"
    [InlineData("OldTool", false)]
    [InlineData("go", false)]             // too short to match fuzzily
    [InlineData("Co", false)]
    public void MatchesInstalledApp_IsFuzzyButNotReckless(string folder, bool expected)
    {
        var s = Snapshot(d =>
        {
            d.InstalledApps.Add(new InstalledApp("Visual Studio Code", "Microsoft Corporation", Array.Empty<string>()));
            d.InstalledApps.Add(new InstalledApp("IntelliJ IDEA Community", "JetBrains s.r.o.", Array.Empty<string>()));
            d.InstalledApps.Add(new InstalledApp("Discord", "Discord Inc.", Array.Empty<string>()));
            d.InstalledApps.Add(new InstalledApp("1Password", "AgileBits", Array.Empty<string>()));
            d.InstalledApps.Add(new InstalledApp("OBS Studio", "OBS Project", Array.Empty<string>()));
        });

        Assert.Equal(expected, s.MatchesInstalledApp(folder, out _));
    }

    [Fact]
    public void NameHints_ProveInstallation_EvenWithoutAnUninstallEntry()
    {
        var s = Snapshot(d => d.InstalledNameHints.Add("Portable Cool Tool"));

        Assert.True(s.MatchesInstalledApp("PortableCoolTool", out _));
    }

    [Fact]
    public void InstallLocation_MatchesInsideEqualAndContainingPaths()
    {
        var loc = P("D:", "Games", "Cool");
        var s = Snapshot(d => d.InstalledApps.Add(new InstalledApp("Cool", null, new[] { loc })));

        Assert.True(s.TryGetInstalledAppAt(loc, out var app));
        Assert.Equal("Cool", app);
        Assert.True(s.TryGetInstalledAppAt(P(loc, "saves"), out _));
        Assert.True(s.TryGetInstalledAppAt(P("D:", "Games"), out _));     // contains an install location
        Assert.False(s.TryGetInstalledAppAt(P("D:", "GamesOther"), out _)); // not a prefix match on partial names
    }

    [Fact]
    public void RunningAndStartupExecutables_ProtectTheirFolderAndAncestors_ButNotSiblings()
    {
        var exe = P("D:", "Tools", "A", "a.exe");
        var s = Snapshot(d =>
        {
            d.RunningExecutables.Add(exe);
            d.StartupAndServiceExecutables.Add(P("D:", "Tools", "B", "b.exe"));
        });

        Assert.True(s.IsInRunningProcessPath(P("D:", "Tools", "A")));
        Assert.True(s.IsInRunningProcessPath(P("D:", "Tools")));
        Assert.False(s.IsInRunningProcessPath(P("D:", "Tools", "C")));
        Assert.True(s.IsReferencedByStartupOrService(P("D:", "Tools", "B")));
        Assert.False(s.IsReferencedByStartupOrService(P("D:", "Tools", "A")));
    }

    [Fact]
    public void RecentItems_MarkTheFileAndEveryFolderAbove_WithTheNewestOpenTime()
    {
        var older = DateTime.UtcNow.AddDays(-100);
        var newer = DateTime.UtcNow.AddDays(-3);
        var s = Snapshot(d =>
        {
            d.RecentItems.Add(new RecentItem(P("D:", "Docs", "a", "one.docx"), older));
            d.RecentItems.Add(new RecentItem(P("D:", "Docs", "b", "two.docx"), newer));
        });

        Assert.True(s.TryGetRecentOpen(P("D:", "Docs", "a", "one.docx"), out var t1));
        Assert.Equal(older, t1);
        Assert.True(s.TryGetRecentOpen(P("D:", "Docs"), out var t2));
        Assert.Equal(newer, t2);
        Assert.False(s.TryGetRecentOpen(P("D:", "Docs", "c"), out _));
    }

    [Fact]
    public void FileNameMentionsInstalledApp_RequiresAMeaningfulToken()
    {
        var s = Snapshot(d => d.InstalledApps.Add(new InstalledApp("Zoom Workplace (64-bit)", "Zoom Video", Array.Empty<string>())));

        Assert.True(s.FileNameMentionsInstalledApp("ZoomInstallerFull.msi", out _));
        Assert.False(s.FileNameMentionsInstalledApp("holiday.msi", out _));
        Assert.False(s.FileNameMentionsInstalledApp("a.msi", out _));
    }

    [Fact]
    public void ContainerRoots_AreExactMatches_IncludingTempAppDataAndSyncRoots()
    {
        var s = Snapshot(d =>
        {
            d.TempRoots.Add(P("C:", "Users", "me", "AppData", "Local", "Temp"));
            d.AppDataRoots.Add(P("C:", "Users", "me", "AppData", "Roaming"));
            d.SyncedRoots.Add(P("C:", "Users", "me", "OneDrive"));
            d.ContainerRoots.Add(P("C:", "Users", "me", "Downloads"));
        });

        Assert.True(s.IsContainerRoot(P("C:", "Users", "me", "AppData", "Local", "Temp"), out _));
        Assert.True(s.IsContainerRoot(P("C:", "Users", "me", "AppData", "Roaming"), out _));
        Assert.True(s.IsContainerRoot(P("C:", "Users", "me", "OneDrive"), out _));
        Assert.True(s.IsContainerRoot(P("C:", "Users", "me", "Downloads") + Path.DirectorySeparatorChar, out _));
        Assert.False(s.IsContainerRoot(P("C:", "Users", "me", "Downloads", "old"), out _));
        Assert.False(s.IsContainerRoot(P("C:", "Users", "me", "AppData", "Local", "Temp", "x"), out _));
    }

    [Fact]
    public void TempAppDataAndSyncedRoots_AreRecognised_CaseInsensitively()
    {
        var s = Snapshot(d =>
        {
            d.TempRoots.Add(P("C:", "Users", "me", "AppData", "Local", "Temp"));
            d.AppDataRoots.Add(P("C:", "Users", "me", "AppData", "Roaming"));
            d.SyncedRoots.Add(P("C:", "Users", "me", "OneDrive"));
        });

        Assert.True(s.IsUnderTempRoot(P("c:", "users", "me", "appdata", "local", "temp", "x")));
        Assert.True(s.IsUnderAppDataRoot(P("C:", "Users", "me", "AppData", "Roaming", "Foo"), out _));
        Assert.False(s.IsUnderAppDataRoot(P("C:", "Users", "me", "AppDataX", "Foo"), out _));
        Assert.True(s.IsInSyncedFolder(P("C:", "Users", "me", "OneDrive", "Docs"), out _));
    }
}

public class WindowsSnapshotParsingTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Foo\\foo.exe\" /uninstall /silent", "C:\\Program Files\\Foo\\foo.exe")]
    [InlineData("C:\\Program Files\\Foo\\foo.exe,0", "C:\\Program Files\\Foo\\foo.exe")]
    [InlineData("C:\\Foo Bar\\Update.exe --uninstall", "C:\\Foo Bar\\Update.exe")]
    [InlineData("MsiExec.exe /X{GUID}", null)]
    [InlineData("system32\\svchost.exe -k netsvcs", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractExecutablePath_HandlesRealRegistryShapes(string? raw, string? expected)
    {
        Assert.Equal(expected, RegistryPathParser.ExtractExecutablePath(raw));
    }

    [Fact]
    public void ExtractExecutablePath_ExpandsSystemRootForServices()
    {
        var path = RegistryPathParser.ExtractExecutablePath("\\SystemRoot\\System32\\drivers\\foo.sys", "C:\\Windows");

        Assert.Equal("C:\\Windows\\System32\\drivers\\foo.sys", path);
    }

    [Fact]
    public void ExtractDirectory_ReturnsTheContainingFolder()
    {
        var dir = RegistryPathParser.ExtractDirectory("\"C:\\Program Files\\Foo\\foo.exe\" /x");

        Assert.NotNull(dir);
        Assert.EndsWith("Foo", dir);
    }

    // ── .lnk files ──────────────────────────────────────────────────────────

    private static byte[] BuildLnk(string basePath, string suffix, bool unicode)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        // ShellLinkHeader (0x4C bytes)
        w.Write(0x4Cu);
        w.Write(new byte[16]);              // CLSID
        w.Write(0x2u);                      // LinkFlags: HasLinkInfo
        w.Write(new byte[0x4C - 4 - 16 - 4]);

        // LinkInfo
        uint headerSize = unicode ? 0x24u : 0x1Cu;
        var baseAnsi = System.Text.Encoding.Latin1.GetBytes(basePath + "\0");
        var suffixAnsi = System.Text.Encoding.Latin1.GetBytes(suffix + "\0");
        var baseUni = System.Text.Encoding.Unicode.GetBytes(basePath + "\0");
        var suffixUni = System.Text.Encoding.Unicode.GetBytes(suffix + "\0");

        uint baseOff = headerSize;
        uint suffixOff = baseOff + (uint)baseAnsi.Length;
        uint baseUniOff = suffixOff + (uint)suffixAnsi.Length;
        uint suffixUniOff = baseUniOff + (uint)baseUni.Length;
        uint size = unicode ? suffixUniOff + (uint)suffixUni.Length : suffixOff + (uint)suffixAnsi.Length;

        w.Write(size);
        w.Write(headerSize);
        w.Write(1u);                        // VolumeIDAndLocalBasePath
        w.Write(0u);                        // VolumeIDOffset
        w.Write(baseOff);
        w.Write(0u);                        // CommonNetworkRelativeLinkOffset
        w.Write(suffixOff);
        if (unicode)
        {
            w.Write(baseUniOff);
            w.Write(suffixUniOff);
        }
        w.Write(baseAnsi);
        w.Write(suffixAnsi);
        if (unicode)
        {
            w.Write(baseUni);
            w.Write(suffixUni);
        }
        return ms.ToArray();
    }

    [Fact]
    public void LnkParser_ReadsAnsiTargets()
    {
        var bytes = BuildLnk("C:\\Users\\me\\Documents", "report.docx", unicode: false);

        Assert.Equal("C:\\Users\\me\\Documents\\report.docx", LnkParser.TryGetTargetPath(bytes));
    }

    [Fact]
    public void LnkParser_PrefersUnicodeTargets()
    {
        var bytes = BuildLnk("C:\\Users\\me\\Dokumente", "Übersicht ☃.docx", unicode: true);

        Assert.Equal("C:\\Users\\me\\Dokumente\\Übersicht ☃.docx", LnkParser.TryGetTargetPath(bytes));
    }

    [Fact]
    public void LnkParser_ReturnsPathWithoutSuffix_ForFolders()
    {
        var bytes = BuildLnk("C:\\Users\\me\\Projects", "", unicode: true);

        Assert.Equal("C:\\Users\\me\\Projects", LnkParser.TryGetTargetPath(bytes));
    }

    [Fact]
    public void LnkParser_NeverThrows_OnGarbageOrTruncatedInput()
    {
        var good = BuildLnk("C:\\a", "b.txt", unicode: true);

        Assert.Null(LnkParser.TryGetTargetPath(Array.Empty<byte>()));
        Assert.Null(LnkParser.TryGetTargetPath(new byte[200]));
        for (int len = 0; len < good.Length; len += 7)
            LnkParser.TryGetTargetPath(good.AsSpan(0, len)); // must not throw
        var random = new byte[512];
        new Random(7).NextBytes(random);
        Assert.Null(LnkParser.TryGetTargetPath(random));
    }
}
