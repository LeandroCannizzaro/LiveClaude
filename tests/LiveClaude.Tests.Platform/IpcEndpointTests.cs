using System.Text;
using LiveClaude.Abstractions;
using Xunit;

namespace LiveClaude.Tests.Platform;

/// <summary>
/// The IPC endpoint, whichever transport the platform uses underneath. These are the assertions the
/// supervisor and the app actually depend on: a second listener is refused rather than silently
/// serving a duplicate set of servers, and bytes written on one side arrive on the other.
/// </summary>
public class IpcEndpointTests
{
    /// <summary>
    /// A private endpoint per test run, never the one the supervisor uses. LiveClaude is the kind of
    /// program you have running while working on it, and a test that binds the real endpoint either
    /// fails against the live supervisor or — worse — steals its clients.
    /// </summary>
    private static IIpcEndpointFactory NewEndpoint() =>
        PlatformLoader.Current.CreateIpcEndpoint($"test-{Guid.NewGuid():n}");

    [Fact]
    public void TheEndpointHasSomethingToShowInTheUi() =>
        Assert.False(string.IsNullOrWhiteSpace(PlatformLoader.Current.Ipc.EndpointDescription));

    /// <summary>
    /// Two supervisors would run two servers per directory and fight over the same bridge
    /// environments, so the second one has to be told no. This is the check
    /// <c>SupervisorWorker</c> relies on to stand down.
    /// </summary>
    [Fact]
    public void ASecondListenerOnTheSameEndpointIsRefused()
    {
        var endpoint = NewEndpoint();
        using var first = endpoint.Listen();

        Assert.Throws<IpcEndpointBusyException>(() => endpoint.Listen());
    }

    [Fact]
    public async Task NothingIsServedWhenNobodyIsListening()
    {
        // No listener in scope here on purpose.
        Assert.False(await NewEndpoint().IsServedAsync(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task AListenerIsFoundAndCanExchangeALine()
    {
        var endpoint = NewEndpoint();
        using var listener = endpoint.Listen();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var accepting = listener.AcceptAsync(cts.Token);

        await using var client = await endpoint.ConnectAsync(TimeSpan.FromSeconds(5), cts.Token);
        using var connection = await accepting;

        // One JSON object per line is the whole protocol; this proves the transport carries it.
        var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync("""{"kind":"req","method":"ping"}""");

        using var reader = new StreamReader(connection.Stream, new UTF8Encoding(false));
        var line = await reader.ReadLineAsync(cts.Token);

        Assert.Equal("""{"kind":"req","method":"ping"}""", line);
        Assert.True(connection.IsConnected);
    }
}
