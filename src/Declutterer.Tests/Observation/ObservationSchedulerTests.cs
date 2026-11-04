using System.Text;
using System.Xml.Linq;
using Declutterer.Abstractions;
using Declutterer.Integration.Scheduling;
using Declutterer.Tests.Helpers;

namespace Declutterer.Tests.Observation;

public class ObservationSchedulerTests : IDisposable
{
    private readonly TempTestDirectory _t = new();
    private readonly FakeRunner _runner = new();

    public void Dispose() => _t.Dispose();

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string File, string[] Args)> Calls { get; } = new();
        public Func<string, string[], ProcessRunResult> Respond { get; set; } = (_, _) => new ProcessRunResult(0, "", "");
        public Action<string, string[]>? OnCall { get; set; }

        public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            Calls.Add((fileName, args));
            OnCall?.Invoke(fileName, args);
            return Task.FromResult(Respond(fileName, args));
        }
    }

    private const string Exe = "/opt/Declutterer App/Declutterer";

    // ── definitions: pure text, checked structurally ─────────────────────────────────────────

    [Fact]
    public void TheWindowsTaskXml_IsWellFormed_RunsAsTheUserWithoutElevation_AndPassesTheFlag()
    {
        var xml = ScheduleDefinitions.WindowsTaskXml(@"C:\Program Files\Declutterer & Co\Declutterer.exe", new DateTime(2026, 5, 1, 9, 30, 0));

        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(@"C:\Program Files\Declutterer & Co\Declutterer.exe", doc.Descendants(ns + "Command").Single().Value); // escaped, round-trips
        Assert.Equal("--observe", doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("LeastPrivilege", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("InteractiveToken", doc.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("1", doc.Descendants(ns + "DaysInterval").Single().Value);
        Assert.Equal("2026-05-01T09:30:00", doc.Descendants(ns + "StartBoundary").Single().Value);
        Assert.Equal("true", doc.Descendants(ns + "StartWhenAvailable").Single().Value); // makes up for sleep
        Assert.Equal("IgnoreNew", doc.Descendants(ns + "MultipleInstancesPolicy").Single().Value);
        Assert.Equal(ScheduleDefinitions.WindowsTaskName, doc.Descendants(ns + "URI").Single().Value);
    }

    [Fact]
    public void TheSystemdUnits_RunTheFlagOnce_DailyAndCatchingUp()
    {
        var service = ScheduleDefinitions.SystemdService(Exe);
        var timer = ScheduleDefinitions.SystemdTimer();

        Assert.Contains("Type=oneshot", service);
        Assert.Contains("ExecStart=\"/opt/Declutterer App/Declutterer\" --observe", service); // quoted: the path has a space
        Assert.Contains("OnCalendar=daily", timer);
        Assert.Contains("Persistent=true", timer);
        Assert.Contains("Unit=declutterer-observe.service", timer);
        Assert.Contains("WantedBy=timers.target", timer);
    }

    [Fact]
    public void TheLaunchdPlist_IsWellFormed_AndDaily()
    {
        var plist = ScheduleDefinitions.LaunchdPlist("/Applications/Declutterer & Co/Declutterer", 10, 15);

        var doc = XDocument.Parse(plist.Replace("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n", ""));
        var strings = doc.Descendants("string").Select(e => e.Value).ToArray();
        Assert.Contains("com.declutterer.observe", strings);
        Assert.Contains("/Applications/Declutterer & Co/Declutterer", strings);
        Assert.Contains("--observe", strings);
        Assert.Equal(new[] { "10", "15" }, doc.Descendants("integer").Select(e => e.Value).ToArray());
        Assert.Equal("true", doc.Descendants("false").Any() ? "true" : "false");
    }

    // ── nothing happens without an explicit action ───────────────────────────────────────────

    [Fact]
    public void DescribingAndConstructing_RunNothingAndWriteNothing()
    {
        var unitDir = Path.Combine(_t.Path, "units");
        var systemd = new SystemdUserScheduler(_runner, Exe, unitDir);
        var windows = new WindowsTaskScheduler(_runner, @"C:\x\Declutterer.exe", Path.Combine(_t.Path, "work"));
        var launchd = new LaunchdScheduler(_runner, "/x/Declutterer", 501, Path.Combine(_t.Path, "agents"));

        foreach (var scheduler in new IObservationScheduler[] { systemd, windows, launchd })
        {
            var d = scheduler.Describe();
            Assert.False(string.IsNullOrWhiteSpace(d.Summary));
            Assert.Contains("--observe", d.Command);
            Assert.NotEmpty(d.Details);
            Assert.Contains(d.Details, line => line.Contains("without", StringComparison.OrdinalIgnoreCase) || line.Contains("Runs as you"));
        }

        Assert.Empty(_runner.Calls);
        Assert.Empty(Directory.GetFileSystemEntries(_t.Path));
    }

    // ── systemd ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Systemd_Install_WritesBothUnits_ReloadsAndEnablesTheTimer()
    {
        var unitDir = Path.Combine(_t.Path, "units");
        var scheduler = new SystemdUserScheduler(_runner, Exe, unitDir);

        var result = await scheduler.InstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(ScheduleDefinitions.SystemdService(Exe), File.ReadAllText(Path.Combine(unitDir, "declutterer-observe.service")));
        Assert.Equal(ScheduleDefinitions.SystemdTimer(), File.ReadAllText(Path.Combine(unitDir, "declutterer-observe.timer")));
        Assert.Equal(new[] { "--user daemon-reload", "--user enable --now declutterer-observe.timer" }, _runner.Calls.Select(c => string.Join(' ', c.Args)).ToArray());
        Assert.All(_runner.Calls, c => Assert.Equal("systemctl", c.File));
    }

    [Fact]
    public async Task Systemd_AFailedInstall_LeavesNothingBehind()
    {
        var unitDir = Path.Combine(_t.Path, "units");
        _runner.Respond = (_, args) => args.Contains("enable") ? new ProcessRunResult(1, "", "Failed to connect to bus") : new ProcessRunResult(0, "", "");

        var result = await new SystemdUserScheduler(_runner, Exe, unitDir).InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("Failed to connect to bus", result.Message);
        Assert.Empty(Directory.GetFiles(unitDir));
    }

    [Fact]
    public async Task Systemd_Uninstall_DisablesDeletesAndReloads()
    {
        var unitDir = Path.Combine(_t.Path, "units");
        var scheduler = new SystemdUserScheduler(_runner, Exe, unitDir);
        await scheduler.InstallAsync();
        _runner.Calls.Clear();

        var result = await scheduler.UninstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.Empty(Directory.GetFiles(unitDir));
        Assert.Equal(new[] { "--user disable --now declutterer-observe.timer", "--user daemon-reload" }, _runner.Calls.Select(c => string.Join(' ', c.Args)).ToArray());
    }

    [Theory]
    [InlineData(0, "enabled\n", true)]
    [InlineData(1, "disabled\n", false)]
    [InlineData(0, "static\n", false)]
    [InlineData(-1, "", false)]
    public async Task Systemd_Status_ReadsIsEnabled(int exit, string stdout, bool installed)
    {
        _runner.Respond = (_, _) => new ProcessRunResult(exit, stdout, "");

        var status = await new SystemdUserScheduler(_runner, Exe, Path.Combine(_t.Path, "units")).GetStatusAsync();

        Assert.Equal(installed, status.Installed);
    }

    // ── windows ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Windows_Install_ImportsAUtf16Definition_ThenDeletesTheTempFile()
    {
        var work = Path.Combine(_t.Path, "work");
        string? seenFile = null;
        byte[]? seenBytes = null;
        _runner.OnCall = (_, args) =>
        {
            var i = Array.IndexOf(args, "/XML");
            if (i >= 0)
            {
                seenFile = args[i + 1];
                seenBytes = File.ReadAllBytes(seenFile);
            }
        };
        var scheduler = new WindowsTaskScheduler(_runner, @"C:\Declutterer\Declutterer.exe", work, () => new DateTime(2026, 5, 1, 12, 0, 0));

        var result = await scheduler.InstallAsync();

        Assert.True(result.Success, result.Message);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("schtasks", call.File);
        Assert.Equal(new[] { "/Create", "/TN", @"\Declutterer\Observation", "/XML", seenFile!, "/F" }, call.Args);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, seenBytes!.Take(2).ToArray()); // UTF-16 LE with a byte order mark, as the XML declares
        Assert.Contains("2026-05-02T09:30:00", Encoding.Unicode.GetString(seenBytes!)); // first run: tomorrow morning
        Assert.False(File.Exists(seenFile!));
    }

    [Fact]
    public async Task Windows_UninstallAndStatus_UseSchtasks()
    {
        var scheduler = new WindowsTaskScheduler(_runner, @"C:\x.exe", _t.Path);

        await scheduler.UninstallAsync();
        _runner.Respond = (_, _) => new ProcessRunResult(1, "", "ERROR: The system cannot find the file specified.");
        var status = await scheduler.GetStatusAsync();

        Assert.Equal(new[] { "/Delete /TN \\Declutterer\\Observation /F", "/Query /TN \\Declutterer\\Observation" }, _runner.Calls.Select(c => string.Join(' ', c.Args)).ToArray());
        Assert.False(status.Installed);
    }

    [Fact]
    public async Task Windows_AFailedImport_IsReported_WithTheReason()
    {
        _runner.Respond = (_, _) => new ProcessRunResult(1, "", "ERROR: Access is denied.");

        var result = await new WindowsTaskScheduler(_runner, @"C:\x.exe", _t.Path).InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("Access is denied", result.Message);
    }

    // ── launchd ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Launchd_InstallBootstrapsTheAgent_AndUninstallBootsItOutAndDeletesTheFile()
    {
        var dir = Path.Combine(_t.Path, "agents");
        var scheduler = new LaunchdScheduler(_runner, "/Applications/Declutterer", 502, dir);

        var install = await scheduler.InstallAsync();
        var plist = Path.Combine(dir, "com.declutterer.observe.plist");
        Assert.True(install.Success, install.Message);
        Assert.True(File.Exists(plist));
        Assert.Equal(new[] { "bootstrap", "gui/502", plist }, _runner.Calls[0].Args);

        var remove = await scheduler.UninstallAsync();
        Assert.True(remove.Success, remove.Message);
        Assert.False(File.Exists(plist));
        Assert.Equal(new[] { "bootout", "gui/502/com.declutterer.observe" }, _runner.Calls[1].Args);
    }

    [Fact]
    public async Task Launchd_UninstallReportsFailureWhenBootoutFails_EvenIfThePlistWasDeleted()
    {
        var dir = Path.Combine(_t.Path, "agents");
        var scheduler = new LaunchdScheduler(_runner, "/Applications/Declutterer", 502, dir);
        Assert.True((await scheduler.InstallAsync()).Success);

        _runner.Respond = (_, _) => new ProcessRunResult(5, "", "Could not unload job");
        var result = await scheduler.UninstallAsync();

        Assert.False(result.Success);
        Assert.Contains("could not unload it", result.Message);
        Assert.False(File.Exists(Path.Combine(dir, "com.declutterer.observe.plist")));
    }

    [Fact]
    public async Task Launchd_AFailedBootstrap_RemovesTheFile()
    {
        var dir = Path.Combine(_t.Path, "agents");
        _runner.Respond = (_, _) => new ProcessRunResult(5, "", "Bootstrap failed: 5: Input/output error");

        var result = await new LaunchdScheduler(_runner, "/Applications/Declutterer", 501, dir).InstallAsync();

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(dir));
    }

    // ── the real runner and the factory ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheProcessRunner_CapturesOutputAndExitCode_WithoutAShell()
    {
        if (OperatingSystem.IsWindows())
            return;

        var ok = await new ProcessRunner().RunAsync("/bin/sh", new[] { "-c", "echo out; echo err >&2; exit 3" }, TimeSpan.FromSeconds(10));
        Assert.Equal(3, ok.ExitCode);
        Assert.Equal("out", ok.StandardOutput.Trim());
        Assert.Equal("err", ok.StandardError.Trim());

        // arguments are never interpreted: a "; touch" stays one literal argument
        var literal = await new ProcessRunner().RunAsync("/bin/echo", new[] { "a; touch " + Path.Combine(_t.Path, "pwned") }, TimeSpan.FromSeconds(10));
        Assert.Contains("; touch", literal.StandardOutput);
        Assert.False(File.Exists(Path.Combine(_t.Path, "pwned")));
    }

    [Fact]
    public async Task TheProcessRunner_ReportsAMissingProgram_AndATimeout_WithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
            return;

        var missing = await new ProcessRunner().RunAsync("/definitely/not/here", Array.Empty<string>(), TimeSpan.FromSeconds(5));
        Assert.Equal(-1, missing.ExitCode);

        var slow = await new ProcessRunner().RunAsync("/bin/sleep", new[] { "30" }, TimeSpan.FromMilliseconds(300));
        Assert.Equal(-1, slow.ExitCode);
        Assert.Contains("timed out", slow.StandardError);
    }

    [Fact]
    public void TheFactory_PicksTheSchedulerOfThisPlatform()
    {
        var scheduler = ObservationSchedulerFactory.Create(_runner, Exe);

        Assert.True(scheduler.IsSupported);
        Assert.Equal(OperatingSystem.IsWindows() ? "Windows Task Scheduler" : OperatingSystem.IsMacOS() ? "launchd agent" : "systemd user timer", scheduler.Describe().Mechanism);
        Assert.False(ObservationSchedulerFactory.Create(_runner, "").IsSupported);
    }
}
