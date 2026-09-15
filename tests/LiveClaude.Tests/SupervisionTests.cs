using System.IO;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Config;
using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;
using Xunit;

namespace LiveClaude.Tests;

public class BackoffPolicyTests
{
    private static BackoffSettings Settings() => new()
    {
        InitialSeconds = 10,
        MaxSeconds = 60,
        Multiplier = 2,
        ResetAfterHealthySeconds = 120
    };

    [Fact]
    public void DelayGrowsExponentiallyAndIsCapped()
    {
        var policy = new BackoffPolicy(Settings());

        Assert.Equal(10, policy.OnExit(TimeSpan.Zero).TotalSeconds);
        Assert.Equal(20, policy.OnExit(TimeSpan.Zero).TotalSeconds);
        Assert.Equal(40, policy.OnExit(TimeSpan.Zero).TotalSeconds);
        Assert.Equal(60, policy.OnExit(TimeSpan.Zero).TotalSeconds);
        Assert.Equal(60, policy.OnExit(TimeSpan.Zero).TotalSeconds);
    }

    [Fact]
    public void AHealthyRunResetsTheBackoff()
    {
        var policy = new BackoffPolicy(Settings());
        policy.OnExit(TimeSpan.Zero);
        policy.OnExit(TimeSpan.Zero);

        var delay = policy.OnExit(TimeSpan.FromMinutes(10));

        Assert.Equal(10, delay.TotalSeconds);
        Assert.Equal(1, policy.ConsecutiveFailures);
    }

    [Fact]
    public void TheRestartBudgetCanBeExhausted()
    {
        var settings = Settings();
        settings.MaxRestarts = 2;
        var policy = new BackoffPolicy(settings);

        policy.OnExit(TimeSpan.Zero);
        Assert.False(policy.GiveUp);

        policy.OnExit(TimeSpan.Zero);
        Assert.True(policy.GiveUp);
    }
}

public class OutputInterpreterTests
{
    [Fact]
    public void AnsiSequencesAreStripped() =>
        Assert.Equal("hello world", OutputInterpreter.StripAnsi("\u001b[1;32mhello\u001b[0m world"));

    [Fact]
    public void TheSessionUrlIsExtracted()
    {
        var url = OutputInterpreter.FindSessionUrl("Session: \u001b[4mhttps://claude.ai/code/abc123XYZ\u001b[0m ready");
        Assert.Equal("https://claude.ai/code/abc123XYZ", url);
    }

    [Theory]
    [InlineData("Enable Remote Control? (y/n)", OutputSignal.RemoteControlConfirmation)]
    [InlineData("Do you trust the files in this folder?", OutputSignal.TrustPrompt)]
    [InlineData("Waiting for connections...", OutputSignal.Ready)]
    [InlineData("Please run /login to sign in", OutputSignal.LoginRequired)]
    [InlineData("just some output", OutputSignal.None)]
    public void SignalsAreClassified(string text, OutputSignal expected) =>
        Assert.Equal(expected, OutputInterpreter.Classify(text));
}

public class ConfigStoreTests
{
    [Fact]
    public void ConfigurationSurvivesARoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liveclaude-test-{Guid.NewGuid():n}.json");
        var store = new ConfigStore(path);

        try
        {
            var config = new AppConfig
            {
                ClaudePath = @"C:\tools\claude.exe",
                Sessions =
                {
                    new SessionConfig
                    {
                        Name = "Api",
                        Directory = @"C:\repos\api",
                        Spawn = SpawnMode.Worktree,
                        PermissionMode = PermissionMode.BypassPermissions,
                        Capacity = 4
                    }
                }
            };

            store.Save(config);
            var loaded = store.Load();

            Assert.Equal(@"C:\tools\claude.exe", loaded.ClaudePath);
            var session = Assert.Single(loaded.Sessions);
            Assert.Equal("Api", session.Name);
            Assert.Equal(SpawnMode.Worktree, session.Spawn);
            Assert.Equal(PermissionMode.BypassPermissions, session.PermissionMode);
            Assert.Equal(4, session.Capacity);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ACorruptFileDoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liveclaude-bad-{Guid.NewGuid():n}.json");
        File.WriteAllText(path, "{ not json");

        try
        {
            var config = new ConfigStore(path).Load();
            Assert.Empty(config.Sessions);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
                File.Delete(file);
        }
    }
}

public class SessionValidationTests
{
    [Fact]
    public void AMissingDirectoryIsReported()
    {
        var session = new SessionConfig { Name = "x", Directory = @"C:\does\not\exist\nope" };
        Assert.Contains("Directory not found", session.Validate());
    }

    [Fact]
    public void AValidSessionPasses()
    {
        var session = new SessionConfig { Name = "x", Directory = Path.GetTempPath() };
        Assert.Null(session.Validate());
    }
}
