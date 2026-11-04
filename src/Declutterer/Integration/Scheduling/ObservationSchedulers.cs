using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Declutterer.Abstractions;

namespace Declutterer.Integration.Scheduling;

/// <summary>Windows Task Scheduler, through schtasks.exe with an XML definition. Current user, least privilege.</summary>
public sealed class WindowsTaskScheduler : IObservationScheduler
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _runner;
    private readonly string _executable;
    private readonly string _workDirectory;
    private readonly Func<DateTime> _now;

    public WindowsTaskScheduler(IProcessRunner runner, string executablePath, string workDirectory, Func<DateTime>? localNow = null)
    {
        _runner = runner;
        _executable = executablePath;
        _workDirectory = workDirectory;
        _now = localNow ?? (() => DateTime.Now);
    }

    public bool IsSupported => true;

    private DateTime FirstRun() => _now().Date.AddDays(1).AddHours(9).AddMinutes(30);

    public ScheduleDescription Describe() => new("Windows Task Scheduler",
        "A daily task that runs Declutterer in the background (no window) for a moment while you are logged on, and makes up for a missed run after sleep.",
        $"\"{_executable}\" {ScheduleDefinitions.ObserveArgument}",
        new[]
        {
            $"Task: {ScheduleDefinitions.WindowsTaskName}",
            "Runs as you, without administrator rights, only while you are logged on.",
            "It only reads folder metadata for the items on your watchlist and records that today the computer was in use.",
            "Remove it any time here, or in Task Scheduler.",
        });

    public async Task<ScheduleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("schtasks", new[] { "/Query", "/TN", ScheduleDefinitions.WindowsTaskName }, Timeout, cancellationToken).ConfigureAwait(false);
        return new ScheduleStatus(result.Succeeded, result.Succeeded ? null : "not installed");
    }

    public async Task<ScheduleOperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_workDirectory);
        var file = Path.Combine(_workDirectory, "declutterer-observe-task.xml");
        try
        {
            // schtasks reads the definition as UTF-16, as the declaration says
            File.WriteAllText(file, ScheduleDefinitions.WindowsTaskXml(_executable, FirstRun()), Encoding.Unicode);
            var result = await _runner.RunAsync("schtasks", new[] { "/Create", "/TN", ScheduleDefinitions.WindowsTaskName, "/XML", file, "/F" }, Timeout, cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? new ScheduleOperationResult(true, "The daily check-in was added to Task Scheduler.")
                : new ScheduleOperationResult(false, "Task Scheduler refused it: " + Trim(result));
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { /* a leftover temp file is harmless */ }
        }
    }

    public async Task<ScheduleOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("schtasks", new[] { "/Delete", "/TN", ScheduleDefinitions.WindowsTaskName, "/F" }, Timeout, cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? new ScheduleOperationResult(true, "The daily check-in was removed from Task Scheduler.")
            : new ScheduleOperationResult(false, "Task Scheduler could not remove it: " + Trim(result));
    }

    internal static string Trim(ProcessRunResult r) => (string.IsNullOrWhiteSpace(r.StandardError) ? r.StandardOutput : r.StandardError).Trim();
}

/// <summary>A systemd user timer (no root needed).</summary>
public sealed class SystemdUserScheduler : IObservationScheduler
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _runner;
    private readonly string _executable;
    private readonly string _unitDirectory;

    public SystemdUserScheduler(IProcessRunner runner, string executablePath, string? unitDirectory = null)
    {
        _runner = runner;
        _executable = executablePath;
        _unitDirectory = unitDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "systemd", "user");
    }

    public bool IsSupported => true;

    private string ServiceFile => Path.Combine(_unitDirectory, ScheduleDefinitions.SystemdUnitName + ".service");
    private string TimerFile => Path.Combine(_unitDirectory, ScheduleDefinitions.SystemdUnitName + ".timer");

    public ScheduleDescription Describe() => new("systemd user timer",
        "A daily systemd user timer that runs Declutterer in the background for a moment and makes up for a missed run after the machine was off.",
        $"\"{_executable}\" {ScheduleDefinitions.ObserveArgument}",
        new[]
        {
            $"Files: {ServiceFile}, {TimerFile}",
            "Runs as you, without root.",
            "It only reads folder metadata for the items on your watchlist and records that today the computer was in use.",
            "Remove it any time here (it disables the timer and deletes both files).",
        });

    public async Task<ScheduleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("systemctl", new[] { "--user", "is-enabled", ScheduleDefinitions.SystemdUnitName + ".timer" }, Timeout, cancellationToken).ConfigureAwait(false);
        var enabled = result.Succeeded && result.StandardOutput.Trim() == "enabled";
        return new ScheduleStatus(enabled, enabled ? null : "not installed");
    }

    public async Task<ScheduleOperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_unitDirectory);
        File.WriteAllText(ServiceFile, ScheduleDefinitions.SystemdService(_executable));
        File.WriteAllText(TimerFile, ScheduleDefinitions.SystemdTimer());

        var reload = await _runner.RunAsync("systemctl", new[] { "--user", "daemon-reload" }, Timeout, cancellationToken).ConfigureAwait(false);
        var enable = reload.Succeeded
            ? await _runner.RunAsync("systemctl", new[] { "--user", "enable", "--now", ScheduleDefinitions.SystemdUnitName + ".timer" }, Timeout, cancellationToken).ConfigureAwait(false)
            : reload;

        if (enable.Succeeded)
            return new ScheduleOperationResult(true, "The daily check-in was added as a systemd user timer.");

        DeleteFiles(); // leave nothing half-installed behind
        return new ScheduleOperationResult(false, "systemd refused it: " + WindowsTaskScheduler.Trim(enable));
    }

    public async Task<ScheduleOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        await _runner.RunAsync("systemctl", new[] { "--user", "disable", "--now", ScheduleDefinitions.SystemdUnitName + ".timer" }, Timeout, cancellationToken).ConfigureAwait(false);
        DeleteFiles();
        var reload = await _runner.RunAsync("systemctl", new[] { "--user", "daemon-reload" }, Timeout, cancellationToken).ConfigureAwait(false);
        return reload.Succeeded
            ? new ScheduleOperationResult(true, "The daily check-in was removed.")
            : new ScheduleOperationResult(false, "The files were deleted but systemd did not reload: " + WindowsTaskScheduler.Trim(reload));
    }

    private void DeleteFiles()
    {
        foreach (var file in new[] { ServiceFile, TimerFile })
        {
            try { File.Delete(file); } catch (IOException) { /* nothing more to do */ }
        }
    }
}

/// <summary>A launchd user agent in ~/Library/LaunchAgents.</summary>
public sealed class LaunchdScheduler : IObservationScheduler
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _runner;
    private readonly string _executable;
    private readonly string _agentDirectory;
    private readonly string _domain;

    public LaunchdScheduler(IProcessRunner runner, string executablePath, int userId, string? agentDirectory = null)
    {
        _runner = runner;
        _executable = executablePath;
        _domain = $"gui/{userId}";
        _agentDirectory = agentDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
    }

    public bool IsSupported => true;

    private string PlistFile => Path.Combine(_agentDirectory, ScheduleDefinitions.LaunchdLabel + ".plist");

    public ScheduleDescription Describe() => new("launchd agent",
        "A daily launchd agent that runs Declutterer in the background for a moment; a run missed during sleep happens when the Mac wakes.",
        $"\"{_executable}\" {ScheduleDefinitions.ObserveArgument}",
        new[]
        {
            $"File: {PlistFile}",
            "Runs as you, in your login session.",
            "It only reads folder metadata for the items on your watchlist and records that today the computer was in use.",
            "Remove it any time here (it unloads the agent and deletes the file).",
        });

    public async Task<ScheduleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("launchctl", new[] { "print", $"{_domain}/{ScheduleDefinitions.LaunchdLabel}" }, Timeout, cancellationToken).ConfigureAwait(false);
        return new ScheduleStatus(result.Succeeded, result.Succeeded ? null : "not installed");
    }

    public async Task<ScheduleOperationResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_agentDirectory);
        File.WriteAllText(PlistFile, ScheduleDefinitions.LaunchdPlist(_executable, 10, 15));

        var result = await _runner.RunAsync("launchctl", new[] { "bootstrap", _domain, PlistFile }, Timeout, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            return new ScheduleOperationResult(true, "The daily check-in was added as a launchd agent.");

        try { File.Delete(PlistFile); } catch (IOException) { /* nothing more to do */ }
        return new ScheduleOperationResult(false, "launchd refused it: " + WindowsTaskScheduler.Trim(result));
    }

    public async Task<ScheduleOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync("launchctl", new[] { "bootout", $"{_domain}/{ScheduleDefinitions.LaunchdLabel}" }, Timeout, cancellationToken).ConfigureAwait(false);
        try { File.Delete(PlistFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* report the failed removal below */ }

        if (!result.Succeeded)
            return new ScheduleOperationResult(false, "launchd could not unload it: " + WindowsTaskScheduler.Trim(result));
        if (File.Exists(PlistFile))
            return new ScheduleOperationResult(false, "The launchd job was unloaded, but its plist could not be removed.");
        return new ScheduleOperationResult(true, "The daily check-in was removed.");
    }
}

/// <summary>A platform without a supported scheduler.</summary>
public sealed class UnsupportedScheduler : IObservationScheduler
{
    public bool IsSupported => false;

    public ScheduleDescription Describe() => new("none", "Scheduling is not supported on this platform.", string.Empty, Array.Empty<string>());

    public Task<ScheduleStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScheduleStatus(false, "not supported on this platform"));

    public Task<ScheduleOperationResult> InstallAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScheduleOperationResult(false, "Scheduling is not supported on this platform."));

    public Task<ScheduleOperationResult> UninstallAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScheduleOperationResult(true, "Nothing to remove."));
}

public static class ObservationSchedulerFactory
{
    /// <summary>Picks the scheduler of the running platform for the running program.</summary>
    public static IObservationScheduler Create(IProcessRunner runner, string? executablePath = null)
    {
        var exe = executablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return new UnsupportedScheduler();

        if (OperatingSystem.IsWindows())
            return new WindowsTaskScheduler(runner, exe, Path.Combine(Path.GetTempPath(), "Declutterer"));
        if (OperatingSystem.IsMacOS())
            return new LaunchdScheduler(runner, exe, GetUserId());
        if (OperatingSystem.IsLinux())
            return new SystemdUserScheduler(runner, exe);
        return new UnsupportedScheduler();
    }

    private static int GetUserId()
    {
        // launchd's gui domain is keyed by the numeric user id (UID is a shell variable on macOS, usually not in the environment)
        try
        {
            return checked((int)GetUid());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OverflowException)
        {
            return 501; // the first account on every Mac
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();
}
