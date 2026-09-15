using System.ServiceProcess;

namespace LiveClaude.Core.Hosting;

public sealed record ServiceState(bool Installed, string? Status, string? Account, string? StartMode);

public sealed record ServiceInstallResult(bool Success, string Message);

/// <summary>
/// Installs the supervisor as a Windows service through sc.exe, including the failure actions that
/// make Windows restart it on its own and the account right it needs to log on.
/// </summary>
public static class WindowsServiceInstaller
{
    public const string ServiceName = "LiveClaude";
    public const string DisplayName = "LiveClaude Supervisor";
    public const string Description =
        "Keeps Claude Code Remote Control servers running for the configured project directories.";

    public static ServiceState Query()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            var status = controller.Status.ToString();
            return new ServiceState(true, status, null, null);
        }
        catch (InvalidOperationException)
        {
            return new ServiceState(false, null, null, null);
        }
    }

    /// <summary>
    /// Builds the sc.exe command line exactly as it would be typed. sc.exe parses the raw command
    /// line itself: every option is "key= value", the space after the equals sign is required, and
    /// the binPath value quotes the executable inside the quoted value so a path with spaces works.
    /// </summary>
    public static string BuildCreateCommandLine(
        string executablePath,
        string? account = null,
        string? password = null,
        string arguments = SupervisorLauncher.ServiceArgument)
    {
        var line = $"create {ServiceName} binPath= \"\\\"{executablePath}\\\" {arguments}\" start= auto " +
                   $"DisplayName= \"{DisplayName}\"";

        if (!string.IsNullOrWhiteSpace(account))
        {
            line += $" obj= \"{account.Trim()}\"";
            line += $" password= \"{password ?? string.Empty}\"";
        }

        return line;
    }

    public static string BuildFailureCommandLine() =>
        // Restart after 5s, 10s, then every 30s; reset the counter once a day.
        $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000";

    /// <summary>
    /// Creates the service. <paramref name="account"/> should normally be the interactive user
    /// (DOMAIN\user): LocalSystem cannot read the Claude Code credentials stored in the user profile.
    /// Must run elevated.
    /// </summary>
    public static async Task<ServiceInstallResult> InstallAsync(
        string executablePath,
        string? account = null,
        string? password = null,
        string arguments = SupervisorLauncher.ServiceArgument,
        CancellationToken ct = default)
    {
        if (!ProcessHelper.IsElevated)
            return new ServiceInstallResult(false, "Installing a Windows service needs administrator rights.");

        var notes = new List<string>();

        // Without this the service is created and then fails to start with error 1069.
        if (!string.IsNullOrWhiteSpace(account) && !IsBuiltInAccount(account))
        {
            ServiceAccountRights.TryGrantServiceLogon(account.Trim(), out var rightsMessage);
            notes.Add(rightsMessage);
        }

        var create = await ProcessHelper
            .RunRawAsync("sc.exe", BuildCreateCommandLine(executablePath, account, password, arguments), ct)
            .ConfigureAwait(false);

        if (!create.Success)
            return new ServiceInstallResult(false, Describe(create, notes));

        await ProcessHelper.RunRawAsync("sc.exe", $"description {ServiceName} \"{Description}\"", ct).ConfigureAwait(false);
        await ProcessHelper.RunRawAsync("sc.exe", BuildFailureCommandLine(), ct).ConfigureAwait(false);
        await ProcessHelper.RunRawAsync("sc.exe", $"failureflag {ServiceName} 1", ct).ConfigureAwait(false);

        notes.Add("Service created.");
        return new ServiceInstallResult(true, string.Join(" ", notes));
    }

    public static Task<ProcessResult> UninstallAsync(CancellationToken ct = default) =>
        ProcessHelper.RunRawAsync("sc.exe", $"delete {ServiceName}", ct);

    public static Task<ProcessResult> StartAsync(CancellationToken ct = default) =>
        ProcessHelper.RunRawAsync("sc.exe", $"start {ServiceName}", ct);

    public static Task<ProcessResult> StopAsync(CancellationToken ct = default) =>
        ProcessHelper.RunRawAsync("sc.exe", $"stop {ServiceName}", ct);

    /// <summary>Turns the usual sc.exe and service-control failures into something actionable.</summary>
    public static string Explain(ProcessResult result)
    {
        var text = result.Combined;

        if (text.Contains("1069", StringComparison.Ordinal))
            return "The service could not log on (1069). The account needs 'Log on as a service' and a correct password; " +
                   "a blank password is never accepted for a user account.";

        if (text.Contains("1057", StringComparison.Ordinal))
            return "The account name or the password is wrong (1057). Use DOMAIN\\user or .\\user for a local account.";

        if (text.Contains("FAILED 5", StringComparison.Ordinal) || text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            return "Access denied (5): the install has to run elevated.";

        if (text.Contains("1073", StringComparison.Ordinal))
            return "The service already exists (1073). Remove it first, then install again.";

        if (result.ExitCode == 1639)
            return "sc.exe rejected the command line (1639).";

        return text.Trim();
    }

    private static bool IsBuiltInAccount(string account)
    {
        var name = account.Trim().TrimStart('.', '\\');
        return name.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(ProcessResult result, IEnumerable<string> notes) =>
        string.Join(" ", notes.Append(Explain(result))).Trim();
}
