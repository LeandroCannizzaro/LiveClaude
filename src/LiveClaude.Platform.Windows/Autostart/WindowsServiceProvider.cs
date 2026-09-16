using System.ServiceProcess;
using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Windows.Autostart;

/// <summary>
/// Installs the supervisor as a Windows service through sc.exe, including the failure actions that
/// make Windows restart it on its own and the account right it needs to log on.
/// </summary>
public sealed class WindowsServiceProvider : IAutostartProvider
{
    public const string ServiceName = "LiveClaude";
    public const string DisplayName_ = "LiveClaude Supervisor";
    public const string Description =
        "Keeps Claude Code Remote Control servers running for the configured project directories.";

    private readonly IProcessLauncher _processes = new WindowsProcessLauncher();

    public string Kind => "service";

    public string DisplayName => "Windows service";

    public AutostartScope Scope => AutostartScope.System;

    public string Summary =>
        "Starts at boot, before anyone signs in. Run it under your own account: LocalSystem has a " +
        "different profile and cannot read the Claude Code credentials.";

    public bool RequiresElevation(AutostartOptions options) => true;

    public bool SupportsAccount => true;

    /// <summary>
    /// Windows never lets a user account log on as a service with a blank password: without one the
    /// service is created and then refuses to start with error 1069.
    /// </summary>
    public bool RequiresPassword => true;

    public string? StartBeforeSignInRequirement => null;

    public Task<AutostartState> QueryAsync(CancellationToken ct = default)
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return Task.FromResult(new AutostartState(true, controller.Status.ToString(), null));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(new AutostartState(false, null, null));
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
        string arguments = "--service")
    {
        var line = $"create {ServiceName} binPath= \"\\\"{executablePath}\\\" {arguments}\" start= auto " +
                   $"DisplayName= \"{DisplayName_}\"";

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
    /// Creates the service. The account should normally be the interactive user (DOMAIN\user):
    /// LocalSystem cannot read the Claude Code credentials stored in the user profile.
    /// Must run elevated.
    /// </summary>
    public async Task<AutostartResult> InstallAsync(SupervisorCommand command, AutostartOptions options, CancellationToken ct = default)
    {
        if (!_processes.IsElevated)
            return AutostartResult.Failed("Installing a Windows service needs administrator rights.");

        var notes = new List<string>();
        var account = options.UserName;

        // Without this the service is created and then fails to start with error 1069.
        if (!string.IsNullOrWhiteSpace(account) && !IsBuiltInAccount(account))
        {
            ServiceAccountRights.TryGrantServiceLogon(account.Trim(), out var rightsMessage);
            notes.Add(rightsMessage);
        }

        var create = await WindowsProcessLauncher
            .RunRawAsync("sc.exe", BuildCreateCommandLine(command.ExecutablePath, account, options.Password, command.Arguments), ct)
            .ConfigureAwait(false);

        if (!create.Success)
            return AutostartResult.Failed(string.Join(" ", notes.Append(Explain(create))).Trim());

        await WindowsProcessLauncher.RunRawAsync("sc.exe", $"description {ServiceName} \"{Description}\"", ct).ConfigureAwait(false);
        await WindowsProcessLauncher.RunRawAsync("sc.exe", BuildFailureCommandLine(), ct).ConfigureAwait(false);
        await WindowsProcessLauncher.RunRawAsync("sc.exe", $"failureflag {ServiceName} 1", ct).ConfigureAwait(false);

        notes.Add("Service created.");
        return AutostartResult.Ok(string.Join(" ", notes));
    }

    public async Task<AutostartResult> UninstallAsync(CancellationToken ct = default) =>
        ToResult(await WindowsProcessLauncher.RunRawAsync("sc.exe", $"delete {ServiceName}", ct).ConfigureAwait(false));

    public async Task<AutostartResult> StartAsync(CancellationToken ct = default) =>
        ToResult(await WindowsProcessLauncher.RunRawAsync("sc.exe", $"start {ServiceName}", ct).ConfigureAwait(false));

    public async Task<AutostartResult> StopAsync(CancellationToken ct = default) =>
        ToResult(await WindowsProcessLauncher.RunRawAsync("sc.exe", $"stop {ServiceName}", ct).ConfigureAwait(false));

    private static AutostartResult ToResult(ProcessResult result) =>
        result.Success ? AutostartResult.Ok(result.Combined) : AutostartResult.Failed(Explain(result));

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
}
