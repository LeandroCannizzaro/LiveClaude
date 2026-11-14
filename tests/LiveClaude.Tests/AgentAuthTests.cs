using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using LiveClaude.Product.Claude;
using LiveClaude.Product.Cursor;
using Xunit;

namespace LiveClaude.Tests;

public class AgentAuthTests
{
    [Fact]
    public void ClaudeProductExposesInteractiveLogin()
    {
        var product = new ClaudeAgentProduct();
        var auth = Assert.IsAssignableFrom<IAgentAuth>(product);

        Assert.True(auth.SupportsInteractiveLogin);
        Assert.Equal(ProductIds.Claude, product.Id);
    }

    [Fact]
    public void CursorProductExposesInteractiveLogin()
    {
        var product = new CursorAgentProduct();
        var auth = Assert.IsAssignableFrom<IAgentAuth>(product);

        Assert.True(auth.SupportsInteractiveLogin);
        Assert.Equal(ProductIds.Cursor, product.Id);
    }

    [Fact]
    public void ClaudeBuildLoginLaunchUsesLoginArgvWhenExecutableIsConfigured()
    {
        var exe = CreateTempExecutable("claude-login-test");
        try
        {
            var config = new AppConfig { Products = { Claude = { Path = exe } } };
            var plan = new ClaudeAgentProduct().BuildLoginLaunch(config, Path.GetTempPath());

            Assert.NotNull(plan);
            Assert.Equal(exe, plan!.ExecutablePath);
            Assert.Equal(["/login"], plan.Arguments);
            Assert.True(Directory.Exists(plan.WorkingDirectory));
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void CursorBuildLoginLaunchUsesLoginArgvWhenExecutableIsConfigured()
    {
        var exe = CreateTempExecutable("cursor-login-test");
        try
        {
            var config = new AppConfig { Products = { Cursor = { Path = exe } } };
            var plan = new CursorAgentProduct().BuildLoginLaunch(config, Path.GetTempPath());

            Assert.NotNull(plan);
            Assert.Equal(exe, plan!.ExecutablePath);
            Assert.Equal(["login"], plan.Arguments);
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public void CursorReportsSignedInWhenApiKeyFileExists()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"cursor-key-{Guid.NewGuid()}.txt");
        File.WriteAllText(keyPath, "test-key");
        try
        {
            var config = new AppConfig { Products = { Cursor = { ApiKeyPath = keyPath } } };
            var state = new CursorAgentProduct().GetAuthState(config);

            Assert.Equal(AgentAuthStatus.SignedIn, state.Status);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Fact]
    public void CursorReportsUnknownWhenPreferringExistingLoginWithoutKeyFiles()
    {
        var config = new AppConfig
        {
            Products = { Cursor = { PreferExistingLogin = true, ApiKeyPath = null, AuthTokenFile = null } }
        };

        var state = new CursorAgentProduct().GetAuthState(config);
        Assert.Equal(AgentAuthStatus.Unknown, state.Status);
    }

    [Fact]
    public void ShouldOfferLoginFollowsProductSettings()
    {
        var claude = new ClaudeAgentProduct();
        var cursor = new CursorAgentProduct();

        Assert.True(claude.ShouldOfferLogin(new AppConfig { Products = { Claude = { OfferLoginWhenRequired = true } } }));
        Assert.False(claude.ShouldOfferLogin(new AppConfig { Products = { Claude = { OfferLoginWhenRequired = false } } }));
        Assert.True(cursor.ShouldOfferLogin(new AppConfig { Products = { Cursor = { OfferLoginWhenRequired = true } } }));
        Assert.False(cursor.ShouldOfferLogin(new AppConfig { Products = { Cursor = { OfferLoginWhenRequired = false } } }));
    }

    private static string CreateTempExecutable(string prefix)
    {
        // Locators only require the file to exist (and be "executable" per platform checks).
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid()}.exe");
        File.WriteAllText(path, "");
        return path;
    }
}
