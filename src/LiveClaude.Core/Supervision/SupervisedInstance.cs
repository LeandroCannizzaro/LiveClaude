using System.Text;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Config;
using LiveClaude.Core.Logging;
using LiveClaude.Core.Model;
using LiveClaude.Core.Pty;
using Microsoft.Extensions.Logging;

namespace LiveClaude.Core.Supervision;

/// <summary>
/// One supervised <c>claude remote-control</c> server: owns its pseudo console, restarts it with
/// backoff when it dies, and exposes both the live terminal stream and a stripped log tail.
/// </summary>
public sealed class SupervisedInstance : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly InstanceStateStore _stateStore;
    private readonly RollingLogWriter _log;
    private readonly object _gate = new();
    private readonly Queue<string> _tail = new();
    private readonly StringBuilder _recentOutput = new();

    private AppConfig _appConfig;
    private SessionConfig _config;
    private string _claudePath;
    private BackoffPolicy _backoff;
    private PersistedInstanceState _state;

    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private PtyProcess? _pty;
    private short _columns = 140;
    private short _rows = 40;

    // Snapshot fields
    private InstanceState _stateValue = InstanceState.Stopped;
    private DateTimeOffset? _startedUtc;
    private DateTimeOffset? _lastExitUtc;
    private int? _lastExitCode;
    private DateTimeOffset? _nextRetryUtc;
    private string? _sessionUrl;
    private string? _lastOutput;
    private string? _attentionReason;
    private string? _lastError;
    private int _restartCount;

    public SupervisedInstance(
        SessionConfig config,
        AppConfig appConfig,
        string claudePath,
        ILogger logger,
        InstanceStateStore? stateStore = null)
    {
        _config = config;
        _appConfig = appConfig;
        _claudePath = claudePath;
        _logger = logger;
        _stateStore = stateStore ?? new InstanceStateStore();
        _state = _stateStore.Load(config.Id);
        _backoff = new BackoffPolicy(appConfig.Backoff);
        _log = new RollingLogWriter(
            Path.Combine(ConfigStore.LogDirectory, $"{Sanitize(config.Name)}-{config.Id[..6]}.log"),
            appConfig.LogMaxSizeMb);

        if (!config.Enabled)
            _stateValue = InstanceState.Disabled;
    }

    public string Id => _config.Id;

    public SessionConfig Config
    {
        get { lock (_gate) return _config; }
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>Raised whenever the snapshot changes in a way the UI should see.</summary>
    public event Action<SupervisedInstance>? Changed;

    /// <summary>Raw pseudo-console output, escape sequences included, for the embedded terminal.</summary>
    public event Action<string, string>? Output;

    public InstanceSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new InstanceSnapshot
            {
                Id = _config.Id,
                Name = _config.Name,
                Directory = _config.Directory,
                State = _stateValue,
                ProcessId = _pty is { HasExited: false } p ? p.ProcessId : null,
                StartedUtc = _startedUtc,
                LastExitUtc = _lastExitUtc,
                LastExitCode = _lastExitCode,
                RestartCount = _restartCount,
                NextRetryUtc = _nextRetryUtc,
                SessionUrl = _sessionUrl ?? _state.LastSessionUrl,
                LastOutput = _lastOutput,
                AttentionReason = _attentionReason,
                LastError = _lastError
            };
        }
    }

    public IReadOnlyList<string> LogTail(int lines)
    {
        lock (_gate)
        {
            if (_tail.Count >= lines)
                return _tail.TakeLast(lines).ToArray();
        }

        return _log.Tail(lines);
    }

    /// <summary>The recent raw terminal bytes, replayed to a client that attaches mid-session.</summary>
    public string RecentTerminalOutput
    {
        get { lock (_gate) return _recentOutput.ToString(); }
    }

    public void UpdateRuntime(AppConfig appConfig, string claudePath)
    {
        lock (_gate)
        {
            _appConfig = appConfig;
            _claudePath = claudePath;
            _backoff = new BackoffPolicy(appConfig.Backoff);
        }
    }

    /// <summary>Applies a new configuration. Returns true when the change requires a restart.</summary>
    public bool UpdateConfig(SessionConfig config)
    {
        lock (_gate)
        {
            var needsRestart = RequiresRestart(_config, config);
            _config = config;
            if (!config.Enabled)
                _stateValue = InstanceState.Disabled;
            else if (_stateValue == InstanceState.Disabled)
                _stateValue = InstanceState.Stopped;
            return needsRestart;
        }
    }

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
                return Task.CompletedTask;

            if (!_config.Enabled)
            {
                _stateValue = InstanceState.Disabled;
                Notify();
                return Task.CompletedTask;
            }

            _loopCts = new CancellationTokenSource();
            _loop = Task.Run(() => RunLoopAsync(_loopCts.Token));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        PtyProcess? pty;

        lock (_gate)
        {
            cts = _loopCts;
            loop = _loop;
            pty = _pty;
        }

        cts?.Cancel();
        pty?.Kill();

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                _logger.LogWarning("Instance {Name} did not stop cleanly within 15s.", _config.Name);
            }
        }

        lock (_gate)
        {
            _loop = null;
            _loopCts?.Dispose();
            _loopCts = null;
            _stateValue = _config.Enabled ? InstanceState.Stopped : InstanceState.Disabled;
            _nextRetryUtc = null;
            _attentionReason = null;
        }

        Notify();
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _backoff.Reset();
        await StartAsync().ConfigureAwait(false);
    }

    public void WriteInput(string text)
    {
        PtyProcess? pty;
        lock (_gate)
            pty = _pty;

        try
        {
            pty?.Write(text);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Writing to {Name} failed.", _config.Name);
        }
    }

    public void Resize(short columns, short rows)
    {
        lock (_gate)
        {
            _columns = columns;
            _rows = rows;
            _pty?.Resize(columns, rows);
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var validationError = Config.Validate();
            if (validationError is not null)
            {
                SetFailed(validationError);
                return;
            }

            if (!File.Exists(_claudePath))
            {
                SetFailed($"Claude CLI not found at '{_claudePath}'. Set the path in Settings.");
                return;
            }

            var startedAt = DateTimeOffset.UtcNow;
            int exitCode;

            try
            {
                exitCode = await RunOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Write($"[supervisor] launch failed: {ex.Message}");
                _logger.LogError(ex, "Launching instance {Name} failed.", Config.Name);
                lock (_gate)
                {
                    _lastError = ex.Message;
                    _stateValue = InstanceState.Backoff;
                }
                exitCode = -1;
            }

            var uptime = DateTimeOffset.UtcNow - startedAt;

            lock (_gate)
            {
                _lastExitUtc = DateTimeOffset.UtcNow;
                _lastExitCode = exitCode;
                _startedUtc = null;
                _state.LastStopUtc = DateTimeOffset.UtcNow;
                _state.LastSessionUrl = _sessionUrl ?? _state.LastSessionUrl;
                _stateStore.Save(_config.Id, _state);
            }

            if (ct.IsCancellationRequested)
                break;

            var delay = _backoff.OnExit(uptime);
            _restartCount++;
            _log.Write($"[supervisor] exited with code {exitCode} after {uptime.TotalSeconds:F0}s; restarting in {delay.TotalSeconds:F0}s (attempt {_backoff.ConsecutiveFailures}).");

            if (_backoff.GiveUp)
            {
                SetFailed($"Gave up after {_backoff.ConsecutiveFailures} failed restarts. Check the logs.");
                return;
            }

            lock (_gate)
            {
                _stateValue = InstanceState.Backoff;
                _nextRetryUtc = DateTimeOffset.UtcNow + delay;
            }

            Notify();

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        lock (_gate)
        {
            _stateValue = Config.Enabled ? InstanceState.Stopped : InstanceState.Disabled;
            _nextRetryUtc = null;
        }

        Notify();
    }

    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var config = Config;
        var args = ClaudeArgs.BuildRemoteControl(config, _state.LastStopUtc);
        var commandLine = ClaudeArgs.ToCommandLine(_claudePath, args);

        _log.Write($"[supervisor] starting: {commandLine} (cwd: {config.Directory})");

        var environment = new Dictionary<string, string>(config.Environment, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(config.Model))
            environment["ANTHROPIC_MODEL"] = config.Model!;

        var pty = PtyProcess.Start(new PtyOptions
        {
            ExecutablePath = _claudePath,
            Arguments = args,
            WorkingDirectory = config.Directory,
            Environment = environment,
            Columns = _columns,
            Rows = _rows
        });

        lock (_gate)
        {
            _pty = pty;
            _startedUtc = DateTimeOffset.UtcNow;
            _stateValue = InstanceState.Starting;
            _attentionReason = null;
            _lastError = null;
            _sessionUrl = null;
            _recentOutput.Clear();
        }

        Notify();

        var reader = Task.Run(() => PumpOutputAsync(pty), CancellationToken.None);

        try
        {
            var exitCode = await pty.Exited.WaitAsync(ct).ConfigureAwait(false);
            await reader.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            pty.Kill();
            throw;
        }
        catch (TimeoutException)
        {
            return pty.HasExited ? await pty.Exited.ConfigureAwait(false) : -1;
        }
        finally
        {
            lock (_gate)
            {
                _pty = null;
            }

            pty.Dispose();
        }
    }

    private async Task PumpOutputAsync(PtyProcess pty)
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[8192 * 2];

        try
        {
            while (true)
            {
                var read = await pty.Output.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (read <= 0)
                    break;

                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                if (count == 0)
                    continue;

                var chunk = new string(chars, 0, count);
                HandleChunk(chunk);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // pipe closed together with the process
        }
    }

    private void HandleChunk(string chunk)
    {
        Output?.Invoke(Id, chunk);

        var stateChanged = false;

        lock (_gate)
        {
            _recentOutput.Append(chunk);
            if (_recentOutput.Length > 64 * 1024)
                _recentOutput.Remove(0, _recentOutput.Length - 64 * 1024);
        }

        foreach (var line in OutputInterpreter.ToLogLines(chunk))
        {
            _log.Write(line);
            lock (_gate)
            {
                _tail.Enqueue(line);
                while (_tail.Count > Math.Max(50, _appConfig.LogTailLines))
                    _tail.Dequeue();
                _lastOutput = line;
            }

            stateChanged = true;
        }

        var url = OutputInterpreter.FindSessionUrl(chunk);
        if (url is not null)
        {
            lock (_gate)
            {
                _sessionUrl = url;
                _state.LastSessionUrl = url;
                _stateStore.Save(_config.Id, _state);
            }

            stateChanged = true;
        }

        switch (OutputInterpreter.Classify(chunk))
        {
            case OutputSignal.Ready:
                lock (_gate)
                {
                    if (_stateValue is InstanceState.Starting or InstanceState.NeedsAttention)
                    {
                        _stateValue = InstanceState.Running;
                        _attentionReason = null;
                        stateChanged = true;
                    }
                }

                break;

            case OutputSignal.TrustPrompt:
            case OutputSignal.RemoteControlConfirmation:
            case OutputSignal.LoginRequired:
                lock (_gate)
                {
                    _stateValue = InstanceState.NeedsAttention;
                    _attentionReason = OutputInterpreter.Describe(OutputInterpreter.Classify(chunk));
                    stateChanged = true;
                }

                break;

            case OutputSignal.FatalError:
                lock (_gate)
                {
                    _lastError = _lastOutput;
                    stateChanged = true;
                }

                break;
        }

        if (stateChanged)
            Notify();
    }

    private void SetFailed(string reason)
    {
        lock (_gate)
        {
            _stateValue = InstanceState.Failed;
            _lastError = reason;
            _nextRetryUtc = null;
        }

        _log.Write($"[supervisor] failed: {reason}");
        _logger.LogError("Instance {Name} failed: {Reason}", Config.Name, reason);
        Notify();
    }

    private void Notify() => Changed?.Invoke(this);

    private static bool RequiresRestart(SessionConfig a, SessionConfig b) =>
        !string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
        a.Spawn != b.Spawn ||
        a.PermissionMode != b.PermissionMode ||
        a.Capacity != b.Capacity ||
        a.CreateSessionInDir != b.CreateSessionInDir ||
        a.Sandbox != b.Sandbox ||
        a.Verbose != b.Verbose ||
        a.Model != b.Model ||
        a.SessionId != b.SessionId ||
        a.SessionNamePrefix != b.SessionNamePrefix ||
        a.ExtraArgs != b.ExtraArgs ||
        a.Enabled != b.Enabled;

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "instance" : cleaned;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _log.Dispose();
    }
}
