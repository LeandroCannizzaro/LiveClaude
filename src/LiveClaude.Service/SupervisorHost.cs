using LiveClaude.Core.Config;
using LiveClaude.Core.Hosting;
using LiveClaude.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
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

        if (asService)
        {
            builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceInstaller.ServiceName);
            builder.Logging.AddEventLog(settings => settings.SourceName = WindowsServiceInstaller.ServiceName);
        }

        // The supervisor is windowless, so its own diagnostics go to a file next to the instance logs.
        ConfigStore.EnsureDirectories();
        builder.Logging.AddProvider(new FileLoggerProvider(
            Path.Combine(ConfigStore.LogDirectory, "supervisor.log")));

        return builder.Build();
    }

    public static Task RunAsync(string[] args, bool asService) => Build(args, asService).RunAsync();
}
