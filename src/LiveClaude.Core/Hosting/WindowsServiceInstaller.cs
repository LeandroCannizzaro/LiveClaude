using System.ServiceProcess;

namespace LiveClaude.Core.Hosting;

public sealed record ServiceState(bool Installed, string? Status, string? Account, string? StartMode);

/// <summary>
/// Installs the supervisor as a Windows service through sc.exe, including the failure actions that
/// make Windows restart it on its own.
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
    /// Creates the service. <paramref name="account"/> should normally be the interactive user
    /// (DOMAIN\user): LocalSystem cannot read the Claude Code credentials stored in the user profile.
    /// </summary>
    public static async Task<ProcessResult> InstallAsync(
        string executablePath,
        string? account = null,
        string? password = null,
        CancellationToken ct = default)
    {
        var binPath = $"\"{executablePath}\" --service";

        var args = new List<string>
        {
            "create", ServiceName,
            $"binPath= {binPath}",
            "start= auto",
            $"DisplayName= {DisplayName}"
        };

        if (!string.IsNullOrWhiteSpace(account))
        {
            args.Add($"obj= {account}");
            args.Add($"password= {password ?? ""}");
        }

        var create = await ProcessHelper.RunAsync("sc.exe", args, ct: ct).ConfigureAwait(false);
        if (!create.Success)
            return create;

        await ProcessHelper.RunAsync("sc.exe", ["description", ServiceName, Description], ct: ct).ConfigureAwait(false);

        // Restart after 5s, 10s then every 30s; reset the counter once a day.
        await ProcessHelper.RunAsync(
            "sc.exe",
            ["failure", ServiceName, "reset= 86400", "actions= restart/5000/restart/10000/restart/30000"],
            ct: ct).ConfigureAwait(false);

        await ProcessHelper.RunAsync("sc.exe", ["failureflag", ServiceName, "1"], ct: ct).ConfigureAwait(false);

        return create;
    }

    public static Task<ProcessResult> UninstallAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("sc.exe", ["delete", ServiceName], ct: ct);

    public static Task<ProcessResult> StartAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("sc.exe", ["start", ServiceName], ct: ct);

    public static Task<ProcessResult> StopAsync(CancellationToken ct = default) =>
        ProcessHelper.RunAsync("sc.exe", ["stop", ServiceName], ct: ct);
}
