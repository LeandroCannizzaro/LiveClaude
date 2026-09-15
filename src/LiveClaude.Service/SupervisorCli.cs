using System.Diagnostics;
using LiveClaude.Core.Config;
using LiveClaude.Core.Hosting;

namespace LiveClaude.Service;

/// <summary>
/// The install/uninstall/status commands. Shared, because on a ClickOnce install the desktop
/// application is the only executable that can run: it re-launches itself elevated with these same
/// verbs instead of the supervisor executable.
/// </summary>
public static class SupervisorCli
{
    public static readonly string[] Verbs =
    [
        "install-service", "uninstall-service", "start-service", "stop-service",
        "install-task", "uninstall-task", "status"
    ];

    public static bool IsVerb(string? candidate) =>
        candidate is not null && Verbs.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args) => args.FirstOrDefault()?.ToLowerInvariant() switch
    {
        "install-service" => await InstallServiceAsync(args),
        "uninstall-service" => await UninstallServiceAsync(),
        "start-service" => await ControlServiceAsync(start: true),
        "stop-service" => await ControlServiceAsync(start: false),
        "install-task" => await InstallTaskAsync(args),
        "uninstall-task" => await UninstallTaskAsync(),
        "status" => await ShowStatusAsync(),
        _ => 64
    };

    private static async Task<int> InstallServiceAsync(string[] args)
    {
        if (!ProcessHelper.IsElevated)
        {
            Console.Error.WriteLine("install-service requires an elevated prompt (Run as administrator).");
            return 5;
        }

        var account = GetOption(args, "--account");
        var password = GetOption(args, "--password");

        if (string.IsNullOrWhiteSpace(account))
        {
            Console.WriteLine("No --account given; the service will run as LocalSystem.");
            Console.WriteLine("Claude Code credentials live in the user profile, so LocalSystem usually cannot sign in.");
            Console.WriteLine($"Recommended: install-service --account \"{ProcessHelper.CurrentUserName}\" --password \"<windows password>\"");
        }
        else if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Warning: a user account with a blank password cannot log on as a service (error 1069).");
        }

        var (exe, arguments) = ResolveTarget(args, asService: true);
        var install = await WindowsServiceInstaller.InstallAsync(exe, account, password, arguments);

        Console.WriteLine(install.Message);
        if (!install.Success)
            return 1;

        var start = await WindowsServiceInstaller.StartAsync();
        if (!start.Success)
        {
            Console.Error.WriteLine(WindowsServiceInstaller.Explain(start));
            return start.ExitCode;
        }

        Console.WriteLine($"Service '{WindowsServiceInstaller.ServiceName}' installed and started.");
        return 0;
    }

    private static async Task<int> UninstallServiceAsync()
    {
        if (!ProcessHelper.IsElevated)
        {
            Console.Error.WriteLine("uninstall-service requires an elevated prompt (Run as administrator).");
            return 5;
        }

        await WindowsServiceInstaller.StopAsync();
        var result = await WindowsServiceInstaller.UninstallAsync();
        Console.WriteLine(result.Success ? "Service removed." : WindowsServiceInstaller.Explain(result));
        return result.Success ? 0 : result.ExitCode;
    }

    private static async Task<int> ControlServiceAsync(bool start)
    {
        var result = start
            ? await WindowsServiceInstaller.StartAsync()
            : await WindowsServiceInstaller.StopAsync();

        if (result.Success)
        {
            Console.WriteLine(start ? "Service started." : "Service stopped.");
            return 0;
        }

        Console.Error.WriteLine(WindowsServiceInstaller.Explain(result));
        return result.ExitCode;
    }

    private static async Task<int> InstallTaskAsync(string[] args)
    {
        var noBoot = args.Contains("--no-boot", StringComparer.OrdinalIgnoreCase);

        // When this runs elevated, UAC may have been answered with a different administrator account;
        // --user keeps the task registered for the person whose session the servers run in.
        var user = GetOption(args, "--user");
        var (exe, arguments) = ResolveTarget(args, asService: false);

        var result = await ScheduledTaskInstaller.InstallAsync(exe, runAtBoot: !noBoot, userName: user, arguments: arguments);
        Console.WriteLine(result.Combined);

        if (!result.Success)
        {
            if (ScheduledTaskInstaller.IsAccessDenied(result) && !noBoot)
                Console.Error.WriteLine("Registering the boot trigger needs administrator rights. Retry with --no-boot for a logon-only task.");

            return result.ExitCode;
        }

        var run = await ScheduledTaskInstaller.RunAsync();
        Console.WriteLine(run.Combined);
        Console.WriteLine($"Scheduled task '{ScheduledTaskInstaller.TaskName}' installed and started.");
        return 0;
    }

    private static async Task<int> UninstallTaskAsync()
    {
        await ScheduledTaskInstaller.EndAsync();
        var result = await ScheduledTaskInstaller.UninstallAsync();
        Console.WriteLine(result.Combined);
        return result.Success ? 0 : result.ExitCode;
    }

    private static async Task<int> ShowStatusAsync()
    {
        var service = WindowsServiceInstaller.Query();
        var task = await ScheduledTaskInstaller.QueryAsync();
        var store = new ConfigStore();
        var config = store.Load();

        Console.WriteLine($"Config        : {store.Path}");
        Console.WriteLine($"Sessions      : {config.Sessions.Count}");
        Console.WriteLine($"Service       : {(service.Installed ? service.Status : "not installed")}");
        Console.WriteLine($"Scheduled task: {(task.Installed ? task.Status : "not installed")}");
        Console.WriteLine($"Logs          : {ConfigStore.LogDirectory}");
        return 0;
    }

    /// <summary>
    /// What to register. Defaults to this executable, and <c>--exe</c> / <c>--args</c> let the caller
    /// register the desktop application instead, which is what a ClickOnce install needs.
    /// </summary>
    private static (string Exe, string Arguments) ResolveTarget(string[] args, bool asService)
    {
        var exe = GetOption(args, "--exe");
        var arguments = GetOption(args, "--args");

        if (!string.IsNullOrWhiteSpace(exe))
        {
            return (exe, string.IsNullOrWhiteSpace(arguments)
                ? asService ? SupervisorLauncher.ServiceArgument : SupervisorLauncher.SuperviseArgument
                : arguments);
        }

        var command = SupervisorLauncher.Resolve(AppContext.BaseDirectory, asService);
        return (command.ExecutablePath, command.Arguments);
    }

    public static string? GetOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static string CurrentExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return path;

        return Process.GetCurrentProcess().MainModule?.FileName
               ?? Path.Combine(AppContext.BaseDirectory, SupervisorDeployment.SupervisorExecutable);
    }
}
