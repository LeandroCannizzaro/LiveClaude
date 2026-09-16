using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Windows.Ipc;

/// <summary>
/// The supervisor endpoint as a named pipe. The ACL is the platform-specific part: the service may
/// run under a different account than the desktop app, so authenticated users are granted read/write
/// explicitly instead of relying on the default owner-only descriptor.
/// </summary>
public sealed class WindowsIpcEndpointFactory : IIpcEndpointFactory
{
    /// <summary>
    /// Endpoint names this process is already serving.
    ///
    /// Windows refuses a second process that tries to create the same pipe — that is the check this
    /// relies on to spot a second supervisor — but it allows the *same* process to open as many
    /// instances of its own pipe as it likes, which is how a named pipe serves several clients at
    /// once. Without this set, calling Listen twice in one process would quietly succeed here and
    /// throw on Linux and macOS, where binding the socket a second time fails. Two supervisors in one
    /// process is a bug on every platform, so it is reported as one on every platform.
    /// </summary>
    private static readonly HashSet<string> Claimed = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _pipeName;

    public WindowsIpcEndpointFactory(string name = IpcEndpointNames.Default)
    {
        _pipeName = "LiveClaude." + name;
    }

    public string EndpointDescription => $@"\\.\pipe\{_pipeName}";

    public IIpcListener Listen()
    {
        lock (Claimed)
        {
            if (Claimed.Contains(_pipeName))
                throw new IpcEndpointBusyException($"This process is already serving {EndpointDescription}.");

            // The first instance is created eagerly: creating it is what fails when another process
            // already owns the name, and the caller needs that answer now rather than on first connect.
            NamedPipeServerStream first;
            try
            {
                first = CreatePipe();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IpcEndpointBusyException(
                    $"Another LiveClaude supervisor already owns {EndpointDescription}.", ex);
            }

            Claimed.Add(_pipeName);
            return new NamedPipeListener(first, _pipeName);
        }
    }

    private static void Release(string pipeName)
    {
        lock (Claimed)
            Claimed.Remove(pipeName);
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> IsServedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await client.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private NamedPipeServerStream CreatePipe() => CreatePipe(_pipeName);

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
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
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024,
            pipeSecurity: security);
    }

    private sealed class NamedPipeListener : IIpcListener
    {
        private readonly string _pipeName;
        private NamedPipeServerStream? _pending;

        public NamedPipeListener(NamedPipeServerStream first, string pipeName)
        {
            _pending = first;
            _pipeName = pipeName;
        }

        public async Task<IIpcConnection> AcceptAsync(CancellationToken ct = default)
        {
            // Each accepted client keeps its own pipe instance, so the next one needs a fresh server.
            var pipe = Interlocked.Exchange(ref _pending, null) ?? CreatePipe(_pipeName);

            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                return new NamedPipeConnection(pipe);
            }
            catch (UnauthorizedAccessException ex)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw new IpcEndpointBusyException("Another supervisor took the endpoint.", ex);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _pending, null)?.Dispose();
            Release(_pipeName);
        }
    }

    private sealed class NamedPipeConnection : IIpcConnection
    {
        private readonly NamedPipeServerStream _pipe;

        public NamedPipeConnection(NamedPipeServerStream pipe)
        {
            _pipe = pipe;
        }

        public Stream Stream => _pipe;

        public bool IsConnected => _pipe.IsConnected;

        public void Dispose() => _pipe.Dispose();
    }
}
