using LiveClaude.Core.Config;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using Xunit;

namespace LiveClaude.Tests;

public class ConfigMigrationTests
{
    [Fact]
    public void V1ClaudePathAndSessionsMigrateToProducts()
    {
        var config = new AppConfig { Version = 1 };
        var json = """
            {
              "Version": 1,
              "ClaudePath": "C:/tools/claude.exe",
              "TrackEnvironments": false,
              "Sessions": [ { "Id": "abc", "Name": "demo", "Directory": "C:/repos/demo" } ]
            }
            """;

        // Simulate deserialize then migrate: Products empty, sessions without ProductId.
        config.Sessions.Add(new SessionConfig { Id = "abc", Name = "demo", Directory = @"C:\repos\demo", ProductId = "" });

        Assert.True(ConfigMigration.Apply(config, json));
        Assert.Equal(2, config.Version);
        Assert.Equal(@"C:/tools/claude.exe", config.Products.Claude.Path);
        Assert.False(config.Products.Claude.TrackEnvironments);
        Assert.Equal(ProductIds.Claude, config.Sessions[0].ProductId);
    }
}
