using System.Security.Principal;
using System.Text;

namespace LiveClaude.Core.Hosting;

public sealed record ScheduledTaskState(bool Installed, string? Status, string? RunAs);

/// <summary>
/// Registers the supervisor as a scheduled task in the interactive user session. This is the
/// recommended host: the task runs as the signed-in user, so Claude Code finds its credentials,
/// and Task Scheduler restarts it at logon, at boot and after a failure.
/// </summary>
public static class ScheduledTaskInstaller
{
    public const string TaskName = "LiveClaude Supervisor";

    public static async Task<ScheduledTaskState> QueryAsync(CancellationToken ct = default)
    {
        var result = await ProcessHelper.RunAsync("schtasks.exe", ["/Query", "/TN", TaskName, "/FO", "LIST"], ct: ct)
            .ConfigureAwait(false);

        if (!result.Success)
            return new ScheduledTaskState(false, null, null);

        string? status = null;
        string? runAs = null;

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            if (key.Equals("Status", StringComparison.OrdinalIgnoreCase))
                status = value;
            else if (key.Equals("Run As User", StringComparison.OrdinalIgnoreCase))
                runAs = value;
        }

        return new ScheduledTaskState(true, status, runAs);
    }

    /// <summary>
    /// Windows only lets an administrator register a task that triggers at system startup, so
    /// <paramref name="runAtBoot"/> requires elevation; a logon-only task installs as a normal user.
    /// </summary>
    public static bool RequiresElevation(bool runAtBoot) => runAtBoot && !ProcessHelper.IsElevated;

    /// <summary>True when a schtasks failure is the "you are not an administrator" one.</summary>
    public static bool IsAccessDenied(ProcessResult result) =>
        !result.Success && result.Combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

    public static async Task<ProcessResult> InstallAsync(
        string executablePath,
        bool runAtBoot = true,
        string? userName = null,
        string arguments = SupervisorLauncher.SuperviseArgument,
        CancellationToken ct = default)
    {
        var xml = BuildXml(executablePath, runAtBoot, userName, arguments);
        var xmlPath = Path.Combine(Path.GetTempPath(), $"liveclaude-task-{Guid.NewGuid():n}.xml");

        // schtasks /XML expects UTF-16 with a BOM.
        await File.WriteAllTextAsync(xmlPath, xml, new UnicodeEncoding(false, true), ct).ConfigureAwait(false);

        try
        {
            return await ProcessHelper.RunAsync(
                "schtasks.exe",
                ["/Create", "/TN", TaskName, "/XML", xmlPath, "/F"],
                ct: ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }
    }

    public static Task<ProcessResult> UninstallAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], ct: ct);

    public static Task<ProcessResult> RunAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("schtasks.exe", ["/Run", "/TN", TaskName], ct: ct);

    public static Task<ProcessResult> EndAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("schtasks.exe", ["/End", "/TN", TaskName], ct: ct);

    /// <summary>
    /// <paramref name="userName"/> matters when the install runs elevated: UAC may have been answered
    /// with a different administrator account, and the task must still belong to the person whose
    /// session the servers run in.
    /// </summary>
    public static string BuildXml(
        string executablePath,
        bool runAtBoot,
        string? userName = null,
        string arguments = SupervisorLauncher.SuperviseArgument)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? WindowsIdentity.GetCurrent().Name : userName.Trim();
        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        var bootTrigger = runAtBoot
            ? """
                  <BootTrigger>
                    <Enabled>true</Enabled>
                    <Delay>PT30S</Delay>
                  </BootTrigger>
              """
            : "";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Date>{now}</Date>
                <Author>{SecurityElementEscape(user)}</Author>
                <Description>Supervises Claude Code Remote Control servers configured in LiveClaude.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElementEscape(user)}</UserId>
                </LogonTrigger>
            {bootTrigger}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElementEscape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
                <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>999</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElementEscape(executablePath)}</Command>
                  <Arguments>{SecurityElementEscape(arguments)}</Arguments>
                  <WorkingDirectory>{SecurityElementEscape(Path.GetDirectoryName(executablePath) ?? ".")}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string SecurityElementEscape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
