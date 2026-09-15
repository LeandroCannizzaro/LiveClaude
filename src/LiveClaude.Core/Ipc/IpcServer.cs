using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiveClaude.Core.Ipc;

/// <summary>
/// Exposes a <see cref="Supervisor"/> over a named pipe so the desktop app can drive a supervisor
/// that lives in the Windows service (or in the scheduled task) and stream its terminals.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly Supervisor _supervisor;
    private readonly ILogger _logger;
    private readonly List<ClientConnection> _clients = new();
    private readonly object _clientsGate = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public IpcServer(Supervisor supervisor, ILogger? logger = null)
    {
        _supervisor = supervisor;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>True when another supervisor already owns the pipe, so this one serves no clients.</summary>
    public bool EndpointUnavailable { get; private set; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _supervisor.StatusChanged += OnStatusChanged;
        _supervisor.TerminalOutput += OnTerminalOutput;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                var client = new ClientConnection(pipe, this, _logger);
                lock (_clientsGate)
                    _clients.Add(client);

                _ = client.RunAsync(ct).ContinueWith(_ =>
                {
                    lock (_clientsGate)
                        _clients.Remove(client);
                    client.Dispose();
                }, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (UnauthorizedAccessException)
            {
                // The pipe name is taken: another supervisor owns it. Retrying every second would
                // only fill the log, and this one keeps supervising without an endpoint.
                pipe?.Dispose();
                _logger.LogWarning(
                    "Another LiveClaude supervisor already owns the '{Pipe}' endpoint, so this one will not serve the app. " +
                    "Two supervisors means two sets of servers: stop the scheduled task or the service if that is not intended.",
                    IpcProtocol.PipeName);
                EndpointUnavailable = true;
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _logger.LogError(ex, "IPC accept loop error.");
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // The service may run under a different account than the desktop app, so authenticated
        // users get read/write access explicitly instead of relying on the default owner-only ACL.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024,
            pipeSecurity: security);
    }

    private void OnStatusChanged(SupervisorStatus status) =>
        Broadcast(IpcProtocol.Events.Status, status);

    private void OnTerminalOutput(string id, string data)
    {
        List<ClientConnection> targets;
        lock (_clientsGate)
            targets = _clients.Where(c => c.IsAttachedTo(id)).ToList();

        if (targets.Count == 0)
            return;

        var payload = new TerminalChunk { Id = id, Data = data };
        foreach (var client in targets)
            client.TrySend(IpcProtocol.Events.Terminal, payload);
    }

    private void Broadcast<T>(string method, T payload)
    {
        List<ClientConnection> targets;
        lock (_clientsGate)
            targets = _clients.ToList();

        foreach (var client in targets)
            client.TrySend(method, payload);
    }

    private async Task<object?> HandleAsync(string method, JsonElement? payload, ClientConnection client)
    {
        switch (method)
        {
            case IpcProtocol.Methods.Ping:
                return "pong";

            case IpcProtocol.Methods.Status:
                return _supervisor.GetStatus();

            case IpcProtocol.Methods.GetConfig:
                return _supervisor.Store.Load();

            case IpcProtocol.Methods.SaveSettings:
                await _supervisor.SaveSettingsAsync(Deserialize<AppConfig>(payload)).ConfigureAwait(false);
                return _supervisor.GetStatus();

            case IpcProtocol.Methods.UpsertSession:
                return await _supervisor.UpsertSessionAsync(Deserialize<SessionConfig>(payload)).ConfigureAwait(false);

            case IpcProtocol.Methods.DeleteSession:
                await _supervisor.DeleteSessionAsync(Deserialize<InstanceRef>(payload).Id).ConfigureAwait(false);
                return true;

            case IpcProtocol.Methods.StartInstance:
                await _supervisor.StartInstanceAsync(Deserialize<InstanceRef>(payload).Id).ConfigureAwait(false);
                return true;

            case IpcProtocol.Methods.StopInstance:
                await _supervisor.StopInstanceAsync(Deserialize<InstanceRef>(payload).Id).ConfigureAwait(false);
                return true;

            case IpcProtocol.Methods.RestartInstance:
                await _supervisor.RestartInstanceAsync(Deserialize<InstanceRef>(payload).Id).ConfigureAwait(false);
                return true;

            case IpcProtocol.Methods.Attach:
            {
                var id = Deserialize<InstanceRef>(payload).Id;
                client.Attach(id);
                return new TerminalChunk { Id = id, Data = _supervisor.GetRecentTerminalOutput(id) };
            }

            case IpcProtocol.Methods.Detach:
                client.Detach(Deserialize<InstanceRef>(payload).Id);
                return true;

            case IpcProtocol.Methods.Input:
            {
                var input = Deserialize<TerminalInput>(payload);
                _supervisor.SendInput(input.Id, input.Data);
                return true;
            }

            case IpcProtocol.Methods.Resize:
            {
                var resize = Deserialize<TerminalResize>(payload);
                _supervisor.Resize(resize.Id, resize.Columns, resize.Rows);
                return true;
            }

            case IpcProtocol.Methods.LogTail:
            {
                var request = Deserialize<LogTailRequest>(payload);
                return new LogTailResponse { Lines = _supervisor.GetLogTail(request.Id, request.Lines).ToList() };
            }

            default:
                throw new InvalidOperationException($"Unknown method '{method}'.");
        }
    }

    private static T Deserialize<T>(JsonElement? payload) where T : new() =>
        payload is null ? new T() : payload.Value.Deserialize<T>(IpcProtocol.Json) ?? new T();

    public async ValueTask DisposeAsync()
    {
        _supervisor.StatusChanged -= OnStatusChanged;
        _supervisor.TerminalOutput -= OnTerminalOutput;
        _cts?.Cancel();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // shutting down anyway
            }
        }

        lock (_clientsGate)
        {
            foreach (var client in _clients)
                client.Dispose();
            _clients.Clear();
        }

        _cts?.Dispose();
    }

    private sealed class ClientConnection : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly IpcServer _server;
        private readonly ILogger _logger;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly HashSet<string> _attached = new(StringComparer.OrdinalIgnoreCase);

        public ClientConnection(NamedPipeServerStream pipe, IpcServer server, ILogger logger)
        {
            _pipe = pipe;
            _server = server;
            _logger = logger;
            _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = false };
        }

        public bool IsAttachedTo(string id)
        {
            lock (_attached)
                return _attached.Contains(id);
        }

        public void Attach(string id)
        {
            lock (_attached)
                _attached.Add(id);
        }

        public void Detach(string id)
        {
            lock (_attached)
                _attached.Remove(id);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            using var reader = new StreamReader(_pipe, new UTF8Encoding(false));

            // Push the current status straight away so the UI has something to show.
            TrySend(IpcProtocol.Events.Status, _server._supervisor.GetStatus());

            while (!ct.IsCancellationRequested && _pipe.IsConnected)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    break;
                }

                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                IpcEnvelope? request;
                try
                {
                    request = JsonSerializer.Deserialize<IpcEnvelope>(line, IpcProtocol.Json);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Malformed IPC message discarded.");
                    continue;
                }

                if (request is null)
                    continue;

                try
                {
                    var result = await _server.HandleAsync(request.Method, request.Payload, this).ConfigureAwait(false);
                    await SendAsync(new IpcEnvelope
                    {
                        Kind = "res",
                        Id = request.Id,
                        Method = request.Method,
                        Payload = ToElement(result)
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "IPC method {Method} failed.", request.Method);
                    await SendAsync(new IpcEnvelope
                    {
                        Kind = "res",
                        Id = request.Id,
                        Method = request.Method,
                        Error = ex.Message
                    }).ConfigureAwait(false);
                }
            }
        }

        public void TrySend<T>(string method, T payload)
        {
            _ = SendAsync(new IpcEnvelope
            {
                Kind = "evt",
                Method = method,
                Payload = ToElement(payload)
            });
        }

        private async Task SendAsync(IpcEnvelope envelope)
        {
            if (!_pipe.IsConnected)
                return;

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(envelope, IpcProtocol.Json)).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // client went away
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private static JsonElement? ToElement<T>(T value) =>
            value is null ? null : JsonSerializer.SerializeToElement(value, IpcProtocol.Json);

        public void Dispose()
        {
            try
            {
                _writer.Dispose();
                _pipe.Dispose();
            }
            catch (IOException)
            {
                // ignored
            }

            _writeLock.Dispose();
        }
    }
}
