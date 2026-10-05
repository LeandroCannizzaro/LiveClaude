using LiveClaude.Product.Cursor;
using Xunit;

namespace LiveClaude.Tests;

public class WindowsBetterSqlite3PatcherTests
{
    [Fact]
    public async Task EnsurePatchedIsNoOpOffWindowsOrWithoutVersionsDir()
    {
        var patcher = new WindowsBetterSqlite3Patcher(
            versionsDirectoryFactory: () => null,
            stateDirectory: Path.Combine(Path.GetTempPath(), "liveclaude-patch-test-" + Guid.NewGuid().ToString("n")));

        // Should not throw when there is nothing to patch.
        await patcher.EnsurePatchedAsync(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    [Fact]
    public void MarkPatchedRoundTripsState()
    {
        var stateDir = Path.Combine(Path.GetTempPath(), "liveclaude-patch-test-" + Guid.NewGuid().ToString("n"));
        var patcher = new WindowsBetterSqlite3Patcher(stateDirectory: stateDir, versionsDirectoryFactory: () => null);

        patcher.MarkPatchedForTests("2026.01.01-abc", "deadbeef");
        var state = patcher.LoadState();

        Assert.NotNull(state);
        Assert.Equal("2026.01.01-abc", state!.AgentVersion);
        Assert.Equal("deadbeef", state.NodeSha256);
        Assert.True(state.Patched);
    }
}
