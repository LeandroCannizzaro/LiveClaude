using LiveClaude.Core.Claude;
using LiveClaude.Core.Model;
using Xunit;

namespace LiveClaude.Tests;

public class ClaudeArgsTests
{
    private static SessionConfig Session() => new()
    {
        Name = "LivePlatform",
        Directory = @"C:\repos\liveplatform",
        Spawn = SpawnMode.SameDir,
        Capacity = 8,
        PermissionMode = PermissionMode.AcceptEdits
    };

    [Fact]
    public void ColdStartBuildsAFullCommandLine()
    {
        var args = ClaudeArgs.BuildRemoteControl(Session());

        Assert.Equal("remote-control", args[0]);
        Assert.Contains("--name", args);
        Assert.Contains("LivePlatform", args);
        Assert.Contains("--spawn", args);
        Assert.Contains("same-dir", args);
        Assert.Contains("--capacity", args);
        Assert.Contains("8", args);
        Assert.Contains("--create-session-in-dir", args);
        Assert.Contains("--permission-mode", args);
        Assert.Contains("acceptEdits", args);
        Assert.DoesNotContain("--continue", args);
    }

    [Fact]
    public void RestartInsideTheWindowReattachesInsteadOfCreatingANewSession()
    {
        var now = DateTimeOffset.UtcNow;
        var args = ClaudeArgs.BuildRemoteControl(Session(), now.AddHours(-1), now);

        Assert.Contains("--continue", args);
        Assert.DoesNotContain("--spawn", args);
        Assert.DoesNotContain("--name", args);
    }

    [Fact]
    public void RestartAfterTheWindowStartsAFreshSession()
    {
        var now = DateTimeOffset.UtcNow;
        var args = ClaudeArgs.BuildRemoteControl(Session(), now.AddHours(-5), now);

        Assert.DoesNotContain("--continue", args);
        Assert.Contains("--name", args);
    }

    [Fact]
    public void AFixedSessionIdWinsOverEverySpawnFlag()
    {
        var session = Session();
        session.SessionId = "abc-123";

        var args = ClaudeArgs.BuildRemoteControl(session, DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Contains("--session-id", args);
        Assert.Contains("abc-123", args);
        Assert.DoesNotContain("--continue", args);
        Assert.DoesNotContain("--spawn", args);
    }

    [Fact]
    public void SessionSpawnModeDropsTheCapacityFlag()
    {
        var session = Session();
        session.Spawn = SpawnMode.Session;

        var args = ClaudeArgs.BuildRemoteControl(session);

        Assert.Contains("session", args);
        Assert.DoesNotContain("--capacity", args);
    }

    [Fact]
    public void ServersThatArchiveTheirSessionsNeverReattach()
    {
        var session = Session();
        session.CreateSessionInDir = false;

        Assert.False(ClaudeArgs.CanReattach(session, DateTimeOffset.UtcNow.AddMinutes(-1)));
    }


    [Fact]
    public void ExtraArgumentsKeepQuotedGroupsTogether()
    {
        var parts = ClaudeArgs.SplitArguments("--debug-file \"C:\\temp\\my log.txt\" --verbose");

        Assert.Equal(["--debug-file", @"C:\temp\my log.txt", "--verbose"], parts);
    }
}
