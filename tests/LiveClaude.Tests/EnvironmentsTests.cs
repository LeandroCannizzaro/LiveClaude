using System.IO;
using System.Net;
using LiveClaude.Core.Claude;
using LiveClaude.Abstractions;
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

public class EnvironmentsFailureTests
{
    private const string ConflictBody = """
        {"type":"error","error":{"type":"invalid_request_error",
         "message":"Environment has 1 active sessions. Use force=true to delete anyway."},
         "request_id":"req_011Cf56CwQWMJ3Y2iEPtSb6G"}
        """;

    [Fact]
    public void AConflictAboutActiveSessionsAsksForForce()
    {
        var failure = EnvironmentsClient.Describe(HttpStatusCode.Conflict, ConflictBody, "req_abc");

        Assert.True(failure.RequiresForce);
        Assert.Equal(409, failure.StatusCode);
        Assert.Equal("req_abc", failure.RequestId);
        Assert.Contains("1 active sessions", failure.Message);
        Assert.Contains("forcing removes them", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AConflictForAnotherReasonDoesNotOfferForce()
    {
        var body = """{"type":"error","error":{"message":"Environment is locked."}}""";

        var failure = EnvironmentsClient.Describe(HttpStatusCode.Conflict, body, null);

        Assert.False(failure.RequiresForce);
        Assert.Contains("Environment is locked.", failure.Message);
    }

    [Fact]
    public void AnAuthFailurePointsAtLogin()
    {
        var failure = EnvironmentsClient.Describe(HttpStatusCode.Unauthorized, """{"error":{"message":"bad token"}}""", null);

        Assert.False(failure.RequiresForce);
        Assert.Contains("/login", failure.Message);
    }

    [Fact]
    public void ANonJsonBodyStillProducesAMessage()
    {
        var failure = EnvironmentsClient.Describe(HttpStatusCode.BadGateway, "<html>gateway</html>", null);

        Assert.Equal(502, failure.StatusCode);
        Assert.Contains("502", failure.Message);
    }

    [Theory]
    [InlineData(false, "https://api.anthropic.com/v1/environments/env_123")]
    [InlineData(true, "https://api.anthropic.com/v1/environments/env_123?force=true")]
    public void TheForceFlagGoesOnTheQueryString(bool force, string expected) =>
        Assert.Equal(expected, EnvironmentsClient.BuildDeleteUrl("env_123", force));
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

    /// <summary>
    /// The regression that let a running server's own environment be deleted: resolving the
    /// environment id over the API is best effort, and when it has not happened yet the directory
    /// of a live server must still protect what that server registered.
    /// </summary>
    [Fact]
    public void ARunningServerProtectsItsDirectoryEvenBeforeItsEnvironmentIsIdentified()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        var environment = Bridge("env_just_registered", @"C:\repos\doG");
        environment.CreatedUtc = started.AddSeconds(20);

        var instances = new[]
        {
            new InstanceSnapshot
            {
                Id = "1",
                Name = "doG",
                Directory = @"C:\repos\doG",
                State = InstanceState.Running,
                StartedUtc = started,
                EnvironmentId = null
            }
        };

        var classification = EnvironmentClassifier.Describe(environment, instances, [Session(@"C:\repos\doG")]);

        Assert.Equal(EnvironmentUsage.InUse, classification.Usage);
        Assert.Equal("doG", classification.OwnerName);
        Assert.Contains("Stop 'doG'", classification.ProtectionNote);
    }

    [Fact]
    public void AnEnvironmentRegisteredAgainAfterADeleteIsAlsoProtected()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-30);
        var reRegistered = Bridge("env_reborn", @"C:\repos\doG");
        reRegistered.CreatedUtc = DateTimeOffset.UtcNow;

        var instances = new[]
        {
            new InstanceSnapshot
            {
                Id = "1",
                Name = "doG",
                Directory = @"C:\repos\doG",
                State = InstanceState.Running,
                StartedUtc = started,
                EnvironmentId = "env_deleted_a_moment_ago"
            }
        };

        Assert.Equal(EnvironmentUsage.InUse, EnvironmentClassifier.Classify(reRegistered, instances, []));
    }

    [Fact]
    public void LeftoversFromEarlierRunsStayDeletableWhileAServerRuns()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        var old = Bridge("env_yesterday", @"C:\repos\doG");
        old.CreatedUtc = started.AddHours(-6);

        var instances = new[]
        {
            new InstanceSnapshot
            {
                Id = "1",
                Name = "doG",
                Directory = @"C:\repos\doG",
                State = InstanceState.Running,
                StartedUtc = started,
                EnvironmentId = "env_live"
            }
        };

        Assert.Equal(EnvironmentUsage.Stale, EnvironmentClassifier.Classify(old, instances, [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void AStoppedSessionReleasesItsDirectory()
    {
        var environment = Bridge("env_any", @"C:\repos\doG");
        environment.CreatedUtc = DateTimeOffset.UtcNow;

        var instances = new[]
        {
            new InstanceSnapshot
            {
                Id = "1",
                Name = "doG",
                Directory = @"C:\repos\doG",
                State = InstanceState.Stopped,
                StartedUtc = null
            }
        };

        Assert.Equal(EnvironmentUsage.Stale, EnvironmentClassifier.Classify(environment, instances, [Session(@"C:\repos\doG")]));
    }

    [Fact]
    public void DuplicateSelectionNeverIncludesWhatARunningServerMayHaveRegistered()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);
        var older = Bridge("env_old", @"C:\repos\doG");
        older.CreatedUtc = started.AddHours(-2);
        var current = Bridge("env_current", @"C:\repos\doG");
        current.CreatedUtc = started.AddSeconds(10);

        var instances = new[]
        {
            new InstanceSnapshot
            {
                Id = "1",
                Name = "doG",
                Directory = @"C:\repos\doG",
                State = InstanceState.Running,
                StartedUtc = started,
                EnvironmentId = null
            }
        };

        var duplicates = EnvironmentClassifier.FindDuplicates([older, current], instances);

        Assert.Equal("env_old", Assert.Single(duplicates).Id);
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
            var credentials = ClaudeCredentialsFile.Read(path);

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
            Assert.True(ClaudeCredentialsFile.Read(path)!.IsExpired);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsNotAnError() =>
        Assert.Null(ClaudeCredentialsFile.Read(Path.Combine(Path.GetTempPath(), "liveclaude-missing.json")));
}
