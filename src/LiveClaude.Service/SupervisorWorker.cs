using LiveClaude.Core.Config;
using LiveClaude.Core.Ipc;
using LiveClaude.Core.Supervision;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LiveClaude.Service;

/// <summary>
/// Hosts the supervisor and the IPC endpoint, and watches the configuration file so changes made by
/// the desktop app are picked up even when the app talks to a different supervisor instance.
/// </summary>
public sealed class SupervisorWorker : BackgroundService
{
    private readonly ILogger<SupervisorWorker> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _host;
    private readonly IHostApplicationLifetime? _lifetime;

    private Supervisor? _supervisor;
    private IpcServer? _ipc;
    private FileSystemWatcher? _watcher;
    private DateTime _lastConfigWrite = DateTime.MinValue;

    public SupervisorWorker(
        ILogger<SupervisorWorker> logger,
        ILoggerFactory loggerFactory,
        string host,
        IHostApplicationLifetime? lifetime = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _host = host;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConfigStore.EnsureDirectories();

        // Two supervisors would run two servers per directory and fight over the same environments.
        if (await AnotherSupervisorIsRunningAsync(stoppingToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "Another LiveClaude supervisor is already running on this machine, so this one is stopping. " +
                "Close the app's own supervisor, or stop the scheduled task or service, to run just one.");

            _lifetime?.StopApplication();
            return;
        }

        var store = new ConfigStore();
        _supervisor = new Supervisor(store, _loggerFactory.CreateLogger<Supervisor>(), _host);
        _ipc = new IpcServer(_supervisor, _loggerFactory.CreateLogger<IpcServer>());
        _ipc.Start();

        _logger.LogInformation("LiveClaude supervisor starting as '{Host}'. Config: {Config}", _host, store.Path);

        if (LiveClaude.Core.Pty.PtyProcess.HasOwnConsole)
        {
            _logger.LogWarning(
                "This process owns a console, so Windows will attach the CLI to it instead of the pseudo console " +
                "and no output can be captured. Run the supervisor windowless (--supervise / --service).");
        }

        await _supervisor.StartAsync(stoppingToken).ConfigureAwait(false);

        StartConfigWatcher(store);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LiveClaude supervisor stopping.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();

        if (_supervisor is not null)
            await _supervisor.StopAllAsync().ConfigureAwait(false);

        if (_ipc is not null)
            await _ipc.DisposeAsync().ConfigureAwait(false);

        if (_supervisor is not null)
            await _supervisor.DisposeAsync().ConfigureAwait(false);

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Probes the IPC endpoint: an answer means a supervisor already owns this machine.</summary>
    private static async Task<bool> AnotherSupervisorIsRunningAsync(CancellationToken ct)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(
                ".", LiveClaude.Core.Ipc.IpcProtocol.PipeName, System.IO.Pipes.PipeDirection.InOut);

            await client.ConnectAsync(750, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void StartConfigWatcher(ConfigStore store)
    {
        var directory = Path.GetDirectoryName(store.Path);
        if (directory is null)
            return;

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(store.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += async (_, _) => await ReloadAsync(store).ConfigureAwait(false);
        _watcher.Created += async (_, _) => await ReloadAsync(store).ConfigureAwait(false);
        _watcher.Renamed += async (_, _) => await ReloadAsync(store).ConfigureAwait(false);
    }

    private async Task ReloadAsync(ConfigStore store)
    {
        // FileSystemWatcher fires several times per save; debounce.
        var now = DateTime.UtcNow;
        if ((now - _lastConfigWrite).TotalMilliseconds < 750)
            return;
        _lastConfigWrite = now;

        await Task.Delay(250).ConfigureAwait(false);

        try
        {
            var config = store.Load();
            if (_supervisor is not null)
                await _supervisor.ReconcileAsync(config, startAutoStart: true).ConfigureAwait(false);

            _logger.LogInformation("Configuration reloaded: {Count} session(s).", config.Sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reloading the configuration failed.");
        }
    }
}
