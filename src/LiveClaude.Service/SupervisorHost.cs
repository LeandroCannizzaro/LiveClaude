using LiveClaude.Abstractions;
using LiveClaude.Core.Config;
using LiveClaude.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LiveClaude.Service;

/// <summary>
/// Builds the supervisor host. Shared so the same code can run from the supervisor executable or
/// from the desktop application: a ClickOnce install does not ship the runtime configuration of a
/// referenced executable, so on those installs only the app's own executable can host it.
/// </summary>
public static class SupervisorHost
{
    public static IHost Build(string[] args, bool asService)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSingleton(sp => new SupervisorWorker(
            sp.GetRequiredService<ILogger<SupervisorWorker>>(),
            sp.GetRequiredService<ILoggerFactory>(),
            asService ? "service" : "task",
            sp.GetRequiredService<IHostApplicationLifetime>()));

        builder.Services.AddHostedService(sp => sp.GetRequiredService<SupervisorWorker>());

        // Both of these check first whether they apply: AddWindowsService only installs its lifetime
        // when the process really was started by the service control manager, AddSystemd only when
        // systemd started it. Calling both unconditionally is how one executable serves a Windows
        // service, a systemd unit, a scheduled task, a LaunchAgent and a plain foreground run.
        builder.Services.AddWindowsService(options =>
            options.ServiceName = ServiceIdentity.ServiceName);
        builder.Services.AddSystemd();

        // The Windows event log is the one piece with no counterpart: journald already captures a
        // systemd unit's output, and launchd writes it to the plist's log paths. The guard is what
        // .NET's platform analyzer wants anyway — EventLog is annotated Windows-only.
        if (asService && OperatingSystem.IsWindows())
            builder.Logging.AddEventLog(settings => settings.SourceName = ServiceIdentity.ServiceName);

        // The supervisor may have no console at all, so its own diagnostics go to a file next to the
        // instance logs.
        ConfigStore.EnsureDirectories();
        builder.Logging.AddProvider(new FileLoggerProvider(
            Path.Combine(ConfigStore.LogDirectory, "supervisor.log")));

        return builder.Build();
    }

    public static Task RunAsync(string[] args, bool asService) => Build(args, asService).RunAsync();
}

/// <summary>Names the supervisor registers itself under. Kept together so nothing drifts.</summary>
public static class ServiceIdentity
{
    public const string ServiceName = "LiveClaude";

    public const string SuperviseArgument = "--supervise";

    public const string ServiceArgument = "--service";
}
