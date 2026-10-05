using System.Net.Sockets;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix.Ipc;

/// <summary>
/// The supervisor endpoint as a Unix domain socket.
///
/// Deliberately not .NET's named pipes. Those do work on Unix — they are mapped onto a socket at
/// <c>/tmp/CoreFxPipe_&lt;name&gt;</c> — but two things make them the wrong choice here:
/// <c>NamedPipeServerStreamAcl.Create</c>, which is how the Windows side restricts access, throws
/// <see cref="PlatformNotSupportedException"/>; and their socket sits directly in world-writable
/// <c>/tmp</c>, where any local user could connect and drive the supervisor. This one lives in a
/// private per-user directory created with mode 0700 (see <see cref="PosixRuntimeDirectory"/>).
///
/// A consequence worth stating: the endpoint is per-user, not per-machine. On a shared Linux box two
/// people each supervising their own projects is not a conflict, and the "another supervisor is
/// already running" guard scopes itself accordingly.
/// </summary>
public sealed class UnixSocketEndpointFactory : IIpcEndpointFactory
{
    private readonly string _socketPath;

    public UnixSocketEndpointFactory(IPathLayout paths, string name = IpcEndpointNames.Default)
    {
        RuntimeDirectory = paths.RuntimeDirectory;

        // Just "<name>.sock": the directory is already called liveclaude-<uid>, and every character
        // counts against a limit of 104.
        _socketPath = Path.Combine(RuntimeDirectory, $"{name}.sock");
    }

    private string RuntimeDirectory { get; }

    public string EndpointDescription => _socketPath;

    public IIpcListener Listen()
    {
        PosixRuntimeDirectory.ValidateSocketPath(_socketPath);
        PosixRuntimeDirectory.Prepare(RuntimeDirectory);

        // A socket file outlives the process that made it. One left behind by a supervisor that was
        // killed would make bind fail with "address in use" forever, so a stale file is removed —
        // but only after checking that nothing answers on it, which is what tells the two cases apart.
        if (File.Exists(_socketPath))
        {
            if (IsAlive())
                throw new IpcEndpointBusyException($"Another LiveClaude supervisor already owns {_socketPath}.");

            TryDelete(_socketPath);
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            socket.Listen(16);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            socket.Dispose();
            throw new IpcEndpointBusyException($"Another LiveClaude supervisor already owns {_socketPath}.", ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        // Belt and braces: the directory is already 0700, but a socket anyone can open is a socket
        // anyone can use to start processes as this user.
        Libc.chmod(_socketPath, Convert.ToUInt32("600", 8));

        return new UnixSocketListener(socket, _socketPath);
    }

    public async Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!File.Exists(_socketPath))
            throw new TimeoutException($"No supervisor is listening on {_socketPath}.");

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), cts.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException($"Connecting to {_socketPath} timed out.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<bool> IsServedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            await using var stream = await ConnectAsync(timeout, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or SocketException
                                     or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Synchronous probe used to tell a live endpoint from a leftover socket file.</summary>
    private bool IsAlive()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // bind will report it properly in a moment
        }
    }

    private sealed class UnixSocketListener : IIpcListener
    {
        private readonly Socket _socket;
        private readonly string _path;

        public UnixSocketListener(Socket socket, string path)
        {
            _socket = socket;
            _path = path;
        }

        public async Task<IIpcConnection> AcceptAsync(CancellationToken ct = default)
        {
            var accepted = await _socket.AcceptAsync(ct).ConfigureAwait(false);
            return new UnixSocketConnection(accepted);
        }

        public void Dispose()
        {
            _socket.Dispose();

            // Leave nothing behind for the next start to have to reason about.
            TryDelete(_path);
        }
    }

    private sealed class UnixSocketConnection : IIpcConnection
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;

        public UnixSocketConnection(Socket socket)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: false);
        }

        public Stream Stream => _stream;

        /// <summary>
        /// Socket.Connected is the state as of the last operation, so a peer that went away is only
        /// noticed on the next read. Poll reports a readable socket with nothing to read, which is
        /// exactly what a closed connection looks like.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    if (!_socket.Connected)
                        return false;

                    return !(_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    return false;
                }
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
            _socket.Dispose();
        }
    }
}
