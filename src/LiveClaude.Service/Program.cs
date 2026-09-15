using System.Diagnostics;
using LiveClaude.Core.Config;
using LiveClaude.Core.Hosting;
using LiveClaude.Core.Logging;
using LiveClaude.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "--supervise";

// Supervising must happen without a console of our own (see ConsoleBridge); every other command is
// a one-shot CLI call whose output belongs in the terminal the user typed it into.
if (command is not ("--service" or "--supervise" or "--console"))
    ConsoleBridge.AttachToParent(allocateIfMissing: true);

return command switch
{
    "--service" => await RunHostAsync(args, asService: true),
    "--supervise" => await RunHostAsync(args, asService: false),
    "install-service" => await InstallServiceAsync(args),
    "uninstall-service" => await UninstallServiceAsync(),
    "install-task" => await InstallTaskAsync(args),
    "uninstall-task" => await UninstallTaskAsync(),
    "status" => await ShowStatusAsync(),
    "--help" or "-h" or "help" => ShowHelp(),
    _ => ShowHelp(unknown: command)
};

static async Task<int> RunHostAsync(string[] args, bool asService)
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSingleton(sp => new SupervisorWorker(
        sp.GetRequiredService<ILogger<SupervisorWorker>>(),
        sp.GetRequiredService<ILoggerFactory>(),
        asService ? "service" : "task"));

    builder.Services.AddHostedService(sp => sp.GetRequiredService<SupervisorWorker>());

    if (asService)
    {
        builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceInstaller.ServiceName);
        builder.Logging.AddEventLog(settings => settings.SourceName = WindowsServiceInstaller.ServiceName);
    }

    // The supervisor is windowless, so its own diagnostics go to a file next to the instance logs.
    ConfigStore.EnsureDirectories();
    builder.Logging.AddProvider(new FileLoggerProvider(
        Path.Combine(ConfigStore.LogDirectory, "supervisor.log")));

    await builder.Build().RunAsync();
    return 0;
}

static async Task<int> InstallServiceAsync(string[] args)
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
        Console.WriteLine($"No --account given; the service will run as LocalSystem.");
        Console.WriteLine("Claude Code credentials live in the user profile, so LocalSystem usually cannot sign in.");
        Console.WriteLine($"Recommended: install-service --account \"{ProcessHelper.CurrentUserName}\" --password \"<windows password>\"");
    }

    var exe = CurrentExecutablePath();
    var result = await WindowsServiceInstaller.InstallAsync(exe, account, password);

    Console.WriteLine(result.Combined);
    if (!result.Success)
        return result.ExitCode;

    var start = await WindowsServiceInstaller.StartAsync();
    Console.WriteLine(start.Combined);
    Console.WriteLine($"Service '{WindowsServiceInstaller.ServiceName}' installed and started.");
    return 0;
}

static async Task<int> UninstallServiceAsync()
{
    if (!ProcessHelper.IsElevated)
    {
        Console.Error.WriteLine("uninstall-service requires an elevated prompt (Run as administrator).");
        return 5;
    }

    await WindowsServiceInstaller.StopAsync();
    var result = await WindowsServiceInstaller.UninstallAsync();
    Console.WriteLine(result.Combined);
    return result.Success ? 0 : result.ExitCode;
}

static async Task<int> InstallTaskAsync(string[] args)
{
    var noBoot = args.Contains("--no-boot", StringComparer.OrdinalIgnoreCase);

    // When this runs elevated, UAC may have been answered with a different administrator account;
    // --user keeps the task registered for the person whose session the servers run in.
    var user = GetOption(args, "--user");

    var result = await ScheduledTaskInstaller.InstallAsync(CurrentExecutablePath(), runAtBoot: !noBoot, userName: user);
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

static async Task<int> UninstallTaskAsync()
{
    await ScheduledTaskInstaller.EndAsync();
    var result = await ScheduledTaskInstaller.UninstallAsync();
    Console.WriteLine(result.Combined);
    return result.Success ? 0 : result.ExitCode;
}

static async Task<int> ShowStatusAsync()
{
    var service = WindowsServiceInstaller.Query();
    var task = await ScheduledTaskInstaller.QueryAsync();
    var store = new ConfigStore();
    var config = store.Load();

    Console.WriteLine($"Config       : {store.Path}");
    Console.WriteLine($"Sessions     : {config.Sessions.Count}");
    Console.WriteLine($"Service      : {(service.Installed ? service.Status : "not installed")}");
    Console.WriteLine($"Scheduled task: {(task.Installed ? task.Status : "not installed")}");
    Console.WriteLine($"Logs         : {ConfigStore.LogDirectory}");
    return 0;
}

static int ShowHelp(string? unknown = null)
{
    if (unknown is not null)
        Console.Error.WriteLine($"Unknown command '{unknown}'.");

    Console.WriteLine("""
        LiveClaude supervisor

        Usage:
          LiveClaude.Service.exe [--supervise]          Supervise windowless (scheduled task host)
          LiveClaude.Service.exe --service              Run as a Windows service
          LiveClaude.Service.exe install-service [--account DOMAIN\user --password ***]
          LiveClaude.Service.exe uninstall-service
          LiveClaude.Service.exe install-task [--no-boot]
          LiveClaude.Service.exe uninstall-task
          LiveClaude.Service.exe status
        """);

    return unknown is null ? 0 : 64;
}

static string? GetOption(string[] args, string name)
{
    var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string CurrentExecutablePath()
{
    var path = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        return path;

    return Process.GetCurrentProcess().MainModule?.FileName
           ?? Path.Combine(AppContext.BaseDirectory, "LiveClaude.Service.exe");
}
