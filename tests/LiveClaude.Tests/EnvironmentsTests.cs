using System.IO;
using LiveClaude.Core.Claude;
using LiveClaude.Core.Model;
using Xunit;

namespace LiveClaude.Tests;

public class EnvironmentsParsingTests
{
    private const string Payload = """
        {
          "data": [
            {
              "id": "env_01A9nq4FaAqwigRsS1dcLGkW",
              "type": "environment",
              "name": "machine:C:\\repos\\doG:5216",
              "description": "Bridge environment (machine)",
              "created_at": "2026-09-15T08:00:27.869272Z",
              "updated_at": "2026-09-15T08:00:27.869272Z",
              "archived_at": null,
              "state": "active",
              "config": {
                "type": "bridge",
                "machine_name": "machine",
                "directory": "C:\\repos\\doG",
                "branch": "develop",
                "git_repo_url": "https://github.com/example/doG"
              },
              "metadata": { "worker_type": "claude_code" },
              "scope": "account"
            },
            {
              "id": "env_01JgHYvMUoLXX61CG3UJvvbG",
              "type": "environment",
              "name": "Default",
              "created_at": "2026-08-29T13:04:01.769755Z",
              "archived_at": "2026-09-01T10:00:00.000000Z",
              "state": "active",
              "config": { "type": "cloud" },
              "scope": "account"
            }
          ],
          "has_more": false
        }
        """;

    [Fact]
    public void BridgeEnvironmentsAreParsedWithTheirDirectory()
    {
        var environments = EnvironmentsClient.Parse(Payload);

        Assert.Equal(2, environments.Count);

        var bridge = environments[0];
        Assert.Equal("env_01A9nq4FaAqwigRsS1dcLGkW", bridge.Id);
        Assert.True(bridge.IsBridge);
        Assert.False(bridge.IsArchived);
        Assert.Equal(@"C:\repos\doG", bridge.Directory);
        Assert.Equal("machine", bridge.MachineName);
        Assert.Equal("develop", bridge.Branch);
        Assert.Equal(2026, bridge.CreatedUtc!.Value.Year);
    }

    [Fact]
    public void NonBridgeAndArchivedEntriesAreRecognised()
    {
        var other = EnvironmentsClient.Parse(Payload)[1];

        Assert.False(other.IsBridge);
        Assert.True(other.IsArchived);
    }

    [Fact]
    public void AnEmptyPayloadIsNotAnError() => Assert.Empty(EnvironmentsClient.Parse("""{"data":[]}"""));
}

public class EnvironmentClassifierTests
{
    private static RemoteEnvironment Bridge(string id, string directory) => new()
    {
        Id = id,
        Name = $"machine:{directory}:abcd",
        ConfigType = "bridge",
        MachineName = "machine",
        Directory = directory,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static SessionConfig Session(string directory) => new() { Name = "doG", Directory = directory };

    [Fact]
    public void TheEnvironmentOfARunningServerIsInUse()
    {
        var environment = Bridge("env_live", @"C:\repos\doG");
        var instances = new[]
        {
            new InstanceSnapshot { Id = "1", Name = "doG", State = InstanceState.Running, EnvironmentId = "env_live" }
        };

        Assert.Equal(
            EnvironmentUsage.InUse,
            EnvironmentClassifier.Classify(environment, instances, [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void ALeftoverForADirectoryWeSuperviseIsStale()
    {
        var environment = Bridge("env_dead", @"C:\repos\doG");
        var instances = new[]
        {
            new InstanceSnapshot { Id = "1", Name = "doG", State = InstanceState.Running, EnvironmentId = "env_live" }
        };

        Assert.Equal(
            EnvironmentUsage.Stale,
            EnvironmentClassifier.Classify(environment, instances, [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void TrailingSeparatorsDoNotChangeTheMatch()
    {
        var environment = Bridge("env_dead", @"C:\repos\doG\");

        Assert.Equal(
            EnvironmentUsage.Stale,
            EnvironmentClassifier.Classify(environment, [], [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void AStoppedInstanceDoesNotProtectItsEnvironment()
    {
        var environment = Bridge("env_dead", @"C:\repos\doG");
        var instances = new[]
        {
            new InstanceSnapshot { Id = "1", Name = "doG", State = InstanceState.Stopped, EnvironmentId = "env_dead" }
        };

        Assert.Equal(
            EnvironmentUsage.Stale,
            EnvironmentClassifier.Classify(environment, instances, [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void DuplicatesKeepTheNewestRegistrationPerDirectory()
    {
        var older = Bridge("env_old", @"C:\repos\doG");
        older.CreatedUtc = DateTimeOffset.UtcNow.AddHours(-3);
        var middle = Bridge("env_mid", @"C:\repos\doG");
        middle.CreatedUtc = DateTimeOffset.UtcNow.AddHours(-2);
        var newest = Bridge("env_new", @"C:\repos\doG");
        newest.CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var elsewhere = Bridge("env_solo", @"C:\repos\api");

        var duplicates = EnvironmentClassifier.FindDuplicates([older, middle, newest, elsewhere], []);

        Assert.Equal(["env_old", "env_mid"], duplicates.Select(e => e.Id).OrderByDescending(id => id));
        Assert.DoesNotContain(duplicates, e => e.Id == "env_new");
        Assert.DoesNotContain(duplicates, e => e.Id == "env_solo");
    }

    [Fact]
    public void ARunningServerKeepsItsEnvironmentEvenWhenItIsNotTheNewest()
    {
        var live = Bridge("env_live", @"C:\repos\doG");
        live.CreatedUtc = DateTimeOffset.UtcNow.AddHours(-4);
        var newer = Bridge("env_newer", @"C:\repos\doG");
        newer.CreatedUtc = DateTimeOffset.UtcNow;

        var instances = new[]
        {
            new InstanceSnapshot { Id = "1", Name = "doG", State = InstanceState.Running, EnvironmentId = "env_live" }
        };

        var duplicates = EnvironmentClassifier.FindDuplicates([live, newer], instances);

        Assert.Equal("env_newer", Assert.Single(duplicates).Id);
    }

    [Fact]
    public void ASingleEnvironmentPerDirectoryIsNeverADuplicate() =>
        Assert.Empty(EnvironmentClassifier.FindDuplicates([Bridge("env_one", @"C:\repos\doG")], []));

    [Fact]
    public void OtherDirectoriesAndNonBridgeEntriesAreLeftAlone()
    {
        var elsewhere = Bridge("env_other", @"D:\someone-else");
        var cloud = new RemoteEnvironment { Id = "env_cloud", ConfigType = "cloud" };

        Assert.Equal(EnvironmentUsage.Unrelated, EnvironmentClassifier.Classify(elsewhere, [], [Session(@"C:\repos\doG")]));
        Assert.Equal(EnvironmentUsage.Unrelated, EnvironmentClassifier.Classify(cloud, [], [Session(@"C:\repos\doG")]));
    }
}

public class ClaudeCredentialsTests
{
    [Fact]
    public void TheOauthTokenIsReadFromTheCredentialsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liveclaude-creds-{Guid.NewGuid():n}.json");
        var expiry = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeMilliseconds();
        File.WriteAllText(path, $$"""
            { "claudeAiOauth": { "accessToken": "sk-ant-oat01-test", "expiresAt": {{expiry}}, "subscriptionType": "max" } }
            """);

        try
        {
            var credentials = ClaudeCredentials.Load(path);

            Assert.NotNull(credentials);
            Assert.Equal("sk-ant-oat01-test", credentials!.AccessToken);
            Assert.Equal("max", credentials.SubscriptionType);
            Assert.False(credentials.IsExpired);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnExpiredTokenIsReportedAsSuch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"liveclaude-creds-{Guid.NewGuid():n}.json");
        var expiry = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        File.WriteAllText(path, $$"""
            { "claudeAiOauth": { "accessToken": "sk-ant-oat01-old", "expiresAt": {{expiry}} } }
            """);

        try
        {
            Assert.True(ClaudeCredentials.Load(path)!.IsExpired);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsNotAnError() =>
        Assert.Null(ClaudeCredentials.Load(Path.Combine(Path.GetTempPath(), "liveclaude-missing.json")));
}
