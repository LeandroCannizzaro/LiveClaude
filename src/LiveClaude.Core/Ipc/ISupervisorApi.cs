using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;

namespace LiveClaude.Core.Ipc;

/// <summary>
/// What the desktop app can do to a supervisor, whether it lives in this process or in the service.
/// </summary>
public interface ISupervisorApi : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>"service", "task" or "in-process".</summary>
    string HostDescription { get; }

    event Action<SupervisorStatus>? StatusChanged;
    event Action<string, string>? TerminalOutput;
    event Action<bool>? ConnectionChanged;

    Task<SupervisorStatus> GetStatusAsync(CancellationToken ct = default);
    Task<AppConfig> GetConfigAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(AppConfig config, CancellationToken ct = default);
    Task<SessionConfig> UpsertSessionAsync(SessionConfig session, CancellationToken ct = default);
    Task DeleteSessionAsync(string id, CancellationToken ct = default);
    Task StartInstanceAsync(string id, CancellationToken ct = default);
    Task StopInstanceAsync(string id, CancellationToken ct = default);
    Task RestartInstanceAsync(string id, CancellationToken ct = default);

    /// <summary>Attaches to an instance's terminal and returns the buffered screen content.</summary>
    Task<string> AttachAsync(string id, CancellationToken ct = default);

    Task DetachAsync(string id, CancellationToken ct = default);
    Task SendInputAsync(string id, string data, CancellationToken ct = default);
    Task ResizeAsync(string id, int columns, int rows, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetLogTailAsync(string id, int lines, CancellationToken ct = default);
}

/// <summary>Drives a supervisor hosted in the current process (app-managed mode, and for tests).</summary>
public sealed class LocalSupervisorApi : ISupervisorApi
{
    private readonly Supervisor _supervisor;

    public LocalSupervisorApi(Supervisor supervisor)
    {
        _supervisor = supervisor;
        _supervisor.StatusChanged += s => StatusChanged?.Invoke(s);
        _supervisor.TerminalOutput += (id, data) => TerminalOutput?.Invoke(id, data);
    }

    public bool IsConnected => true;

    public string HostDescription => "in-process";

    public event Action<SupervisorStatus>? StatusChanged;
    public event Action<string, string>? TerminalOutput;
    public event Action<bool>? ConnectionChanged;

    public Task<SupervisorStatus> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(_supervisor.GetStatus());

    public Task<AppConfig> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(_supervisor.Store.Load());

    public Task SaveSettingsAsync(AppConfig config, CancellationToken ct = default) =>
        _supervisor.SaveSettingsAsync(config, ct);

    public Task<SessionConfig> UpsertSessionAsync(SessionConfig session, CancellationToken ct = default) =>
        _supervisor.UpsertSessionAsync(session, ct);

    public Task DeleteSessionAsync(string id, CancellationToken ct = default) =>
        _supervisor.DeleteSessionAsync(id, ct);

    public Task StartInstanceAsync(string id, CancellationToken ct = default) =>
        _supervisor.StartInstanceAsync(id);

    public Task StopInstanceAsync(string id, CancellationToken ct = default) =>
        _supervisor.StopInstanceAsync(id);

    public Task RestartInstanceAsync(string id, CancellationToken ct = default) =>
        _supervisor.RestartInstanceAsync(id);

    public Task<string> AttachAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_supervisor.GetRecentTerminalOutput(id));

    public Task DetachAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

    public Task SendInputAsync(string id, string data, CancellationToken ct = default)
    {
        _supervisor.SendInput(id, data);
        return Task.CompletedTask;
    }

    public Task ResizeAsync(string id, int columns, int rows, CancellationToken ct = default)
    {
        _supervisor.Resize(id, columns, rows);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetLogTailAsync(string id, int lines, CancellationToken ct = default) =>
        Task.FromResult(_supervisor.GetLogTail(id, lines));

    public ValueTask DisposeAsync()
    {
        ConnectionChanged?.Invoke(false);
        return ValueTask.CompletedTask;
    }
}
