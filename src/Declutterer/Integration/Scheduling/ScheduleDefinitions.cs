using System;
using System.Security;
using System.Text;

namespace Declutterer.Integration.Scheduling;

/// <summary>
/// The text of the scheduler definitions, as pure functions so it can be unit tested on every platform without executing anything.
/// All of them run the program with <c>--observe</c> as the current user, without elevation.
/// </summary>
public static class ScheduleDefinitions
{
    public const string ObserveArgument = "--observe";

    public const string WindowsTaskName = @"\Declutterer\Observation";
    public const string SystemdUnitName = "declutterer-observe";
    public const string LaunchdLabel = "com.declutterer.observe";

    /// <summary>Windows Task Scheduler definition (schema 1.2). Daily, only while the user is logged on, catches up after sleep.</summary>
    public static string WindowsTaskXml(string executablePath, DateTime firstRunLocal)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        sb.AppendLine("  <RegistrationInfo>");
        sb.AppendLine("    <Description>Declutterer observation check-in. Records that this computer was in use today and compares the watched folders' metadata. It never opens or changes your files.</Description>");
        sb.AppendLine($"    <URI>{SecurityElement.Escape(WindowsTaskName)}</URI>");
        sb.AppendLine("  </RegistrationInfo>");
        sb.AppendLine("  <Triggers>");
        sb.AppendLine("    <CalendarTrigger>");
        sb.AppendLine($"      <StartBoundary>{firstRunLocal:yyyy-MM-ddTHH:mm:ss}</StartBoundary>");
        sb.AppendLine("      <Enabled>true</Enabled>");
        sb.AppendLine("      <ScheduleByDay>");
        sb.AppendLine("        <DaysInterval>1</DaysInterval>");
        sb.AppendLine("      </ScheduleByDay>");
        sb.AppendLine("    </CalendarTrigger>");
        sb.AppendLine("  </Triggers>");
        sb.AppendLine("  <Principals>");
        sb.AppendLine("    <Principal id=\"Author\">");
        sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
        sb.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
        sb.AppendLine("    </Principal>");
        sb.AppendLine("  </Principals>");
        sb.AppendLine("  <Settings>");
        sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
        sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
        sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
        sb.AppendLine("    <Enabled>true</Enabled>");
        sb.AppendLine("    <Hidden>false</Hidden>");
        sb.AppendLine("    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>");
        sb.AppendLine("    <Priority>7</Priority>");
        sb.AppendLine("  </Settings>");
        sb.AppendLine("  <Actions Context=\"Author\">");
        sb.AppendLine("    <Exec>");
        sb.AppendLine($"      <Command>{SecurityElement.Escape(executablePath)}</Command>");
        sb.AppendLine($"      <Arguments>{ObserveArgument}</Arguments>");
        sb.AppendLine("    </Exec>");
        sb.AppendLine("  </Actions>");
        sb.AppendLine("</Task>");
        return sb.ToString();
    }

    /// <summary>systemd user service (a one-shot run).</summary>
    public static string SystemdService(string executablePath) =>
        "[Unit]\n" +
        "Description=Declutterer observation check-in (metadata only)\n" +
        "\n" +
        "[Service]\n" +
        "Type=oneshot\n" +
        $"ExecStart={SystemdQuote(executablePath)} {ObserveArgument}\n";

    /// <summary>systemd user timer: daily, and a missed run is made up for after the machine was off (Persistent).</summary>
    public static string SystemdTimer() =>
        "[Unit]\n" +
        "Description=Daily Declutterer observation check-in\n" +
        "\n" +
        "[Timer]\n" +
        "OnCalendar=daily\n" +
        "Persistent=true\n" +
        "RandomizedDelaySec=15min\n" +
        $"Unit={SystemdUnitName}.service\n" +
        "\n" +
        "[Install]\n" +
        "WantedBy=timers.target\n";

    /// <summary>launchd agent: daily at the given time; a run missed during sleep fires on wake.</summary>
    public static string LaunchdPlist(string executablePath, int hour, int minute) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
        "<plist version=\"1.0\">\n" +
        "<dict>\n" +
        "    <key>Label</key>\n" +
        $"    <string>{LaunchdLabel}</string>\n" +
        "    <key>ProgramArguments</key>\n" +
        "    <array>\n" +
        $"        <string>{SecurityElement.Escape(executablePath)}</string>\n" +
        $"        <string>{ObserveArgument}</string>\n" +
        "    </array>\n" +
        "    <key>StartCalendarInterval</key>\n" +
        "    <dict>\n" +
        "        <key>Hour</key>\n" +
        $"        <integer>{hour}</integer>\n" +
        "        <key>Minute</key>\n" +
        $"        <integer>{minute}</integer>\n" +
        "    </dict>\n" +
        "    <key>RunAtLoad</key>\n" +
        "    <false/>\n" +
        "    <key>ProcessType</key>\n" +
        "    <string>Background</string>\n" +
        "</dict>\n" +
        "</plist>\n";

    private static string SystemdQuote(string path) => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
