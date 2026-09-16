using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;

namespace LiveClaude.Core.Ipc;

/// <summary>
/// Talks to a supervisor hosted elsewhere — a service, a scheduled task, a systemd unit, a
/// LaunchAgent — over the platform's IPC endpoint, reconnecting on its own so the desktop app
/// survives a restart of that host.
/// </summary>
public sealed class IpcSupervisorClient : ISupervisorApi
{
    private readonly IIpcEndpointFactory _endpoint;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    private Stream? _stream;
    private StreamWriter? _writer;
    private Task? _readLoop;
    private volatile bool _connected;

    public IpcSupervisorClient(IIpcEndpointFactory? endpoint = null, string host = "service")
    {
        _endpoint = endpoint ?? PlatformLoader.Current.Ipc;
        HostDescription = host;
    }

    public bool IsConnected => _connected;

    public string HostDescription { get; private set; }

    public event Action<SupervisorStatus>? StatusChanged;
    public event Action<string, string>? TerminalOutput;
    public event Action<bool>? ConnectionChanged;

    /// <summary>Tries to connect once; returns false when no supervisor is listening.</summary>
    public async Task<bool> TryConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            var stream = await _endpoint.ConnectAsync(timeout, ct).ConfigureAwait(false);

            _stream = stream;
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
            _connected = true;
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException
                                     or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            _connected = false;
            return false;
        }
    }

    /// <summary>Keeps trying to reconnect in the background until disposed.</summary>
    public void StartAutoReconnect(TimeSpan interval)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!_connected)
                    await TryConnectAsync(TimeSpan.FromSeconds(2), _cts.Token).ConfigureAwait(false);

                try
                {
                    await Task.Delay(interval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, CancellationToken.None);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_stream is null)
            return;

        // leaveOpen: disposing this reader would close the stream, and the writer would then throw
        // "Cannot access a closed pipe" while flushing on shutdown.
        using var reader = new StreamReader(_stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024, leaveOpen: true);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var envelope = JsonSerializer.Deserialize<IpcEnvelope>(line, IpcProtocol.Json);
                if (envelope is null)
                    continue;

                if (envelope.Kind == "evt")
                {
                    DispatchEvent(envelope);
                }
                else if (envelope.Id is not null && _pending.TryRemove(envelope.Id, out var tcs))
                {
                    tcs.TrySetResult(envelope);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException)
        {
            // fall through to disconnect handling
        }

        _connected = false;
        ConnectionChanged?.Invoke(false);

        foreach (var pending in _pending.Values)
            pending.TrySetException(new IOException("Connection to the LiveClaude supervisor was lost."));
        _pending.Clear();
    }

    private void DispatchEvent(IpcEnvelope envelope)
    {
        switch (envelope.Method)
        {
            case IpcProtocol.Events.Status:
                var status = envelope.Payload?.Deserialize<SupervisorStatus>(IpcProtocol.Json);
                if (status is not null)
                {
                    HostDescription = status.Host;
                    StatusChanged?.Invoke(status);
                }

                break;

            case IpcProtocol.Events.Terminal:
                var chunk = envelope.Payload?.Deserialize<TerminalChunk>(IpcProtocol.Json);
                if (chunk is not null)
                    TerminalOutput?.Invoke(chunk.Id, chunk.Data);
                break;
        }
    }

    private async Task<T?> RequestAsync<T>(string method, object? payload, CancellationToken ct)
    {
        if (!_connected || _writer is null)
            throw new InvalidOperationException("Not connected to the LiveClaude supervisor.");

        var id = Guid.NewGuid().ToString("n");
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new IpcEnvelope
        {
            Kind = "req",
            Id = id,
            Method = method,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, IpcProtocol.Json)
        };

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(JsonSerializer.Serialize(envelope, IpcProtocol.Json).AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        IpcEnvelope response;
        try
        {
            response = await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException($"The supervisor did not answer '{method}' in time.");
        }

        if (response.Error is not null)
            throw new InvalidOperationException(response.Error);

        return response.Payload is null ? default : response.Payload.Value.Deserialize<T>(IpcProtocol.Json);
    }

    public async Task<SupervisorStatus> GetStatusAsync(CancellationToken ct = default) =>
        await RequestAsync<SupervisorStatus>(IpcProtocol.Methods.Status, null, ct).ConfigureAwait(false) ?? new SupervisorStatus();

    public async Task<AppConfig> GetConfigAsync(CancellationToken ct = default) =>
        await RequestAsync<AppConfig>(IpcProtocol.Methods.GetConfig, null, ct).ConfigureAwait(false) ?? new AppConfig();

    public Task SaveSettingsAsync(AppConfig config, CancellationToken ct = default) =>
        RequestAsync<SupervisorStatus>(IpcProtocol.Methods.SaveSettings, config, ct);

    public async Task<SessionConfig> UpsertSessionAsync(SessionConfig session, CancellationToken ct = default) =>
        await RequestAsync<SessionConfig>(IpcProtocol.Methods.UpsertSession, session, ct).ConfigureAwait(false) ?? session;

    public Task DeleteSessionAsync(string id, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.DeleteSession, new InstanceRef { Id = id }, ct);

    public Task StartInstanceAsync(string id, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.StartInstance, new InstanceRef { Id = id }, ct);

    public Task StopInstanceAsync(string id, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.StopInstance, new InstanceRef { Id = id }, ct);

    public Task RestartInstanceAsync(string id, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.RestartInstance, new InstanceRef { Id = id }, ct);

    public async Task<string> AttachAsync(string id, CancellationToken ct = default)
    {
        var chunk = await RequestAsync<TerminalChunk>(IpcProtocol.Methods.Attach, new InstanceRef { Id = id }, ct).ConfigureAwait(false);
        return chunk?.Data ?? "";
    }

    public Task DetachAsync(string id, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.Detach, new InstanceRef { Id = id }, ct);

    public Task SendInputAsync(string id, string data, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.Input, new TerminalInput { Id = id, Data = data }, ct);

    public Task ResizeAsync(string id, int columns, int rows, CancellationToken ct = default) =>
        RequestAsync<bool>(IpcProtocol.Methods.Resize, new TerminalResize { Id = id, Columns = columns, Rows = rows }, ct);

    public async Task<IReadOnlyList<string>> GetLogTailAsync(string id, int lines, CancellationToken ct = default)
    {
        var response = await RequestAsync<LogTailResponse>(
            IpcProtocol.Methods.LogTail,
            new LogTailRequest { Id = id, Lines = lines },
            ct).ConfigureAwait(false);

        return response?.Lines ?? [];
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (_readLoop is not null)
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // shutting down
        }

        // The connection may already be gone; closing it is not worth an error dialog.
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException)
        {
            // the supervisor closed the pipe first
        }

        try
        {
            _stream?.Dispose();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException)
        {
            // already closed
        }

        _writeLock.Dispose();
        _cts.Dispose();
    }
}
