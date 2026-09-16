namespace LiveClaude.Abstractions;

/// <summary>
/// The app-to-supervisor channel: a named pipe on Windows, a Unix domain socket elsewhere. Only the
/// transport differs — the protocol above it is one JSON object per line either way.
/// </summary>
public interface IIpcEndpointFactory
{
    /// <summary>Shown in logs and in the dashboard, e.g. the pipe name or the socket path.</summary>
    string EndpointDescription { get; }

    /// <summary>
    /// Starts listening. Throws <see cref="IpcEndpointBusyException"/> when another supervisor
    /// already owns the endpoint — which is how a second supervisor learns to stand down instead of
    /// running a duplicate set of servers for the same directories.
    /// </summary>
    IIpcListener Listen();

    /// <summary>Connects to a supervisor. Throws <see cref="TimeoutException"/> when there is none.</summary>
    Task<Stream> ConnectAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>
    /// True when something is already serving this endpoint. Cheaper and quieter than
    /// <see cref="ConnectAsync"/> for the "is another supervisor running?" probe at startup.
    /// </summary>
    Task<bool> IsServedAsync(TimeSpan timeout, CancellationToken ct = default);
}

public interface IIpcListener : IDisposable
{
    /// <summary>Waits for the next client.</summary>
    Task<IIpcConnection> AcceptAsync(CancellationToken ct = default);
}

public interface IIpcConnection : IDisposable
{
    Stream Stream { get; }

    bool IsConnected { get; }
}

/// <summary>Names an IPC endpoint. The platform turns this into a pipe name or a socket file.</summary>
public static class IpcEndpointNames
{
    /// <summary>
    /// The endpoint the supervisor and the app use. It carries the protocol version so an old app
    /// never talks to a supervisor that speaks something else — it simply finds nothing there.
    /// </summary>
    public const string Default = "v1";
}

/// <summary>Another supervisor owns the endpoint on this machine (or for this user).</summary>
public sealed class IpcEndpointBusyException : Exception
{
    public IpcEndpointBusyException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
