using System.Collections.Concurrent;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Config;
using LiveClaude.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveClaude.Core.Supervision;

/// <summary>Describes who is currently supervising: the Windows service, a scheduled task, or the app.</summary>
public sealed class SupervisorStatus
{
    public string Host { get; set; } = "";
    public string Version { get; set; } = "";
    public string? ClaudePath { get; set; }
    public string? ClaudeVersion { get; set; }
    public bool PseudoConsoleSupported { get; set; }

    /// <summary>
    /// True when the supervisor process owns a console. Windows then gives that console to every
    /// child, the pseudo console is ignored and no output can be captured — the host has to be a
    /// windowless process.
    /// </summary>
    public bool HostOwnsConsole { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public List<InstanceSnapshot> Instances { get; set; } = new();
}

/// <summary>
/// Owns every supervised instance and keeps them in sync with the configuration file. Hosted by the
/// Windows service, by the scheduled task, or in-process by the desktop app.
/// </summary>
public sealed class Supervisor : IAsyncDisposable
{
    private readonly ConfigStore _store;
    private readonly ILogger<Supervisor> _logger;
    private readonly ConcurrentDictionary<string, SupervisedInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    private AppConfig _config;
    private ClaudeInstall? _claude;

    public Supervisor(ConfigStore? store = null, ILogger<Supervisor>? logger = null, string host = "app")
    {
        _store = store ?? new ConfigStore();
        _logger = logger ?? NullLogger<Supervisor>.Instance;
        _config = _store.Load();
        Host = host;
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public string Host { get; }

    public DateTimeOffset StartedUtc { get; }

    public ConfigStore Store => _store;

    public AppConfig Config => _config;

    public event Action<SupervisorStatus>? StatusChanged;

    public event Action<string, string>? TerminalOutput;

    public SupervisorStatus GetStatus() => new()
    {
        Host = Host,
        Version = typeof(Supervisor).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        ClaudePath = _claude?.Path,
        ClaudeVersion = _claude?.Version,
        PseudoConsoleSupported = Pty.PtyProcess.IsSupported,
        HostOwnsConsole = Pty.PtyProcess.HasOwnConsole,
        StartedUtc = StartedUtc,
        Instances = _instances.Values
            .Select(i => i.Snapshot())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
    };

    /// <summary>Loads the configuration, creates every instance and starts the ones marked auto-start.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ConfigStore.EnsureDirectories();
        _config = _store.Load();
        _claude = ClaudeLocator.Locate(_config.ClaudePath);

        if (_claude is null)
            _logger.LogWarning("Claude CLI not found. Set 'ClaudePath' in the configuration or install the native build.");
        else
            _logger.LogInformation("Using Claude CLI at {Path} ({Version}).", _claude.Path, _claude.Version ?? "unknown version");

        await ReconcileAsync(_config, startAutoStart: true, ct).ConfigureAwait(false);
    }

    public async Task StopAllAsync()
    {
        foreach (var instance in _instances.Values)
            await instance.StopAsync().ConfigureAwait(false);

        RaiseStatus();
    }

    /// <summary>Applies a configuration: adds, updates and removes instances, restarting what changed.</summary>
    public async Task ReconcileAsync(AppConfig config, bool startAutoStart = false, CancellationToken ct = default)
    {
        await _reconcileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _config = config;
            _claude = ClaudeLocator.Locate(config.ClaudePath);
            var claudePath = _claude?.Path ?? config.ClaudePath ?? "claude.exe";

            foreach (var session in config.Sessions)
            {
                if (_instances.TryGetValue(session.Id, out var existing))
                {
                    existing.UpdateRuntime(config, claudePath);
                    var needsRestart = existing.UpdateConfig(session);

                    if (!session.Enabled)
                        await existing.StopAsync().ConfigureAwait(false);
                    else if (needsRestart && existing.IsRunning)
                        await existing.RestartAsync().ConfigureAwait(false);
                    else if (startAutoStart && session.AutoStart && !existing.IsRunning)
                        await existing.StartAsync().ConfigureAwait(false);
                }
                else
                {
                    var instance = new SupervisedInstance(session, config, claudePath, _logger);
                    instance.Changed += _ => RaiseStatus();
                    instance.Output += (id, data) => TerminalOutput?.Invoke(id, data);
                    _instances[session.Id] = instance;

                    if (session.Enabled && session.AutoStart && startAutoStart)
                        await instance.StartAsync().ConfigureAwait(false);
                }
            }

            var removed = _instances.Keys
                .Where(id => config.Sessions.All(s => !string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var id in removed)
            {
                if (_instances.TryRemove(id, out var instance))
                    await instance.DisposeAsync().ConfigureAwait(false);
            }

            RaiseStatus();
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    public async Task<SessionConfig> UpsertSessionAsync(SessionConfig session, CancellationToken ct = default)
    {
        var config = _store.Load();
        var index = config.Sessions.FindIndex(s => string.Equals(s.Id, session.Id, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
            config.Sessions[index] = session;
        else
            config.Sessions.Add(session);

        _store.Save(config);
        await ReconcileAsync(config, startAutoStart: false, ct).ConfigureAwait(false);
        return session;
    }

    public async Task DeleteSessionAsync(string id, CancellationToken ct = default)
    {
        var config = _store.Load();
        config.Sessions.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        _store.Save(config);

        if (_instances.TryRemove(id, out var instance))
            await instance.DisposeAsync().ConfigureAwait(false);

        new InstanceStateStore().Delete(id);
        await ReconcileAsync(config, startAutoStart: false, ct).ConfigureAwait(false);
    }

    public async Task SaveSettingsAsync(AppConfig config, CancellationToken ct = default)
    {
        var current = _store.Load();
        current.ClaudePath = config.ClaudePath;
        current.HealthCheckSeconds = config.HealthCheckSeconds;
        current.LogTailLines = config.LogTailLines;
        current.LogMaxSizeMb = config.LogMaxSizeMb;
        current.UsePseudoConsole = config.UsePseudoConsole;
        current.Backoff = config.Backoff;
        _store.Save(current);
        await ReconcileAsync(current, startAutoStart: false, ct).ConfigureAwait(false);
    }

    public Task StartInstanceAsync(string id) =>
        _instances.TryGetValue(id, out var instance) ? instance.StartAsync() : Task.CompletedTask;

    public Task StopInstanceAsync(string id) =>
        _instances.TryGetValue(id, out var instance) ? instance.StopAsync() : Task.CompletedTask;

    public Task RestartInstanceAsync(string id) =>
        _instances.TryGetValue(id, out var instance) ? instance.RestartAsync() : Task.CompletedTask;

    public void SendInput(string id, string text)
    {
        if (_instances.TryGetValue(id, out var instance))
            instance.WriteInput(text);
    }

    public void Resize(string id, int columns, int rows)
    {
        if (_instances.TryGetValue(id, out var instance))
            instance.Resize((short)columns, (short)rows);
    }

    public IReadOnlyList<string> GetLogTail(string id, int lines) =>
        _instances.TryGetValue(id, out var instance) ? instance.LogTail(lines) : [];

    /// <summary>Terminal bytes buffered for this instance, so a client that attaches sees the current screen.</summary>
    public string GetRecentTerminalOutput(string id) =>
        _instances.TryGetValue(id, out var instance) ? instance.RecentTerminalOutput : "";

    private void RaiseStatus() => StatusChanged?.Invoke(GetStatus());

    public async ValueTask DisposeAsync()
    {
        foreach (var instance in _instances.Values)
            await instance.DisposeAsync().ConfigureAwait(false);

        _instances.Clear();
        _reconcileLock.Dispose();
    }
}
