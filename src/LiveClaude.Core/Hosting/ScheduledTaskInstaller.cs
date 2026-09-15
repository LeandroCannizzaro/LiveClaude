using System.Security.Principal;
using System.Text;

namespace LiveClaude.Core.Hosting;

/// <summary>
/// What could be learned about the task. <see cref="Installed"/> is null when Windows would not say:
/// a task registered by another administrator is not readable by a standard account, and reporting
/// that as "not installed" sent people installing it again and again.
/// </summary>
public sealed record ScheduledTaskState(bool? Installed, string? Status, string? RunAs, string? Detail = null)
{
    public string Describe() => Installed switch
    {
        true => Status ?? "installed",
        false => "not installed",
        _ => "installed, but this account cannot read it"
    };
}

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
        // CSV without headers: the columns are positional, so this reads the same on a localised
        // Windows, where "Status:" in the LIST format becomes "Stato:" and the like.
        var result = await ProcessHelper
            .RunAsync("schtasks.exe", ["/Query", "/TN", TaskName, "/FO", "CSV", "/NH"], ct: ct)
            .ConfigureAwait(false);

        if (result.Success)
            return new ScheduledTaskState(true, ParseCsvStatus(result.StandardOutput), await ReadRunAsAsync(ct).ConfigureAwait(false));

        return Classify(result);
    }

    /// <summary>Turns a failed query into "missing" or "cannot tell", never guessing "missing".</summary>
    public static ScheduledTaskState Classify(ProcessResult result)
    {
        var text = result.Combined;

        // "The system cannot find the file specified" is how schtasks reports an unknown task name.
        if (text.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Impossibile trovare", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("non esiste", StringComparison.OrdinalIgnoreCase))
        {
            return new ScheduledTaskState(false, null, null, text.Trim());
        }

        if (IsAccessDenied(result))
        {
            return new ScheduledTaskState(
                null, null, null,
                "Windows will not let this account read the task. That happens when it was registered " +
                "by a different administrator account through the elevation prompt; the task still runs.");
        }

        return new ScheduledTaskState(null, null, null, text.Trim());
    }

    private static string? ParseCsvStatus(string output)
    {
        // "\LiveClaude Supervisor","N/A","Ready"
        var line = output.Split('\n').FirstOrDefault(l => l.Contains(','));
        if (line is null)
            return null;

        var columns = line.Split("\",\"");
        return columns.Length >= 3 ? columns[^1].Trim('"', '\r', '\n', ' ') : null;
    }

    /// <summary>Reads the principal from the task XML, which is not localised.</summary>
    private static async Task<string?> ReadRunAsAsync(CancellationToken ct)
    {
        var result = await ProcessHelper.RunAsync("schtasks.exe", ["/Query", "/TN", TaskName, "/XML"], ct: ct)
            .ConfigureAwait(false);

        if (!result.Success)
            return null;

        var xml = result.StandardOutput;
        var start = xml.IndexOf("<UserId>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += "<UserId>".Length;
        var end = xml.IndexOf("</UserId>", start, StringComparison.OrdinalIgnoreCase);
        return end > start ? xml[start..end].Trim() : null;
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
