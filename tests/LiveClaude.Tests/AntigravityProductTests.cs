using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using LiveClaude.Product.Antigravity;
using Xunit;

namespace LiveClaude.Tests;

public class AntigravityProductTests
{
    [Fact]
    public void BuildRemoteControlIncludesRequiredFlagAndDefaults()
    {
        var session = new SessionConfig
        {
            ProductId = ProductIds.Antigravity,
            Name = "agy-box",
            Directory = Path.GetTempPath()
        };

        var args = AntigravityArgs.BuildRemoteControl(session);

        Assert.Equal("--remote-control", args[0]);
        Assert.Contains("--continue", args);
        Assert.DoesNotContain("--sandbox", args);
        Assert.DoesNotContain("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void BuildRemoteControlMapsAllOptionsAndConversationWinsOverContinue()
    {
        var session = new SessionConfig
        {
            ProductId = ProductIds.Antigravity,
            Name = "agy-box",
            Directory = Path.GetTempPath(),
            ExtraArgs = "--new-project",
            Antigravity = new AntigravitySessionOptions
            {
                Mode = "auto",
                Model = "gemini-3-pro",
                Effort = "high",
                Agent = "coder",
                Project = "my-proj",
                ExtraDirs = ["C:\\repo-a", "C:\\repo-b"],
                Sandbox = true,
                DangerouslySkipPermissions = true,
                ContinuePrevious = true,
                ConversationId = "conv-123"
            }
        };

        var args = AntigravityArgs.BuildRemoteControl(session);

        Assert.Equal(
            [
                "--remote-control",
                "--mode", "auto",
                "--model", "gemini-3-pro",
                "--effort", "high",
                "--agent", "coder",
                "--project", "my-proj",
                "--add-dir", "C:\\repo-a",
                "--add-dir", "C:\\repo-b",
                "--sandbox",
                "--dangerously-skip-permissions",
                "--conversation", "conv-123",
                "--new-project"
            ],
            args);
    }

    [Fact]
    public void OutputInterpreterClassifiesSignalsAndExtractsUrl()
    {
        var interpreter = new AntigravityOutputInterpreter();

        Assert.Equal(OutputSignal.Ready, interpreter.Classify("\x1b[32mRemote control connected at https://antigravity.google/session/abc123\x1b[0m"));
        Assert.Equal("https://antigravity.google/session/abc123", interpreter.FindSessionUrl("Visit https://antigravity.google/session/abc123."));
        Assert.Equal(OutputSignal.LoginRequired, interpreter.Classify("Please sign in at https://accounts.google.com/o/oauth2"));
        Assert.Equal(OutputSignal.TrustPrompt, interpreter.Classify("Do you trust the files in this folder?"));
        Assert.Equal(OutputSignal.FatalError, interpreter.Classify("unknown flag: --bogus"));
        Assert.Equal(OutputSignal.None, interpreter.Classify("Thinking..."));
    }

    [Fact]
    public void ProductValidatesModeEffortAndExtraDirs()
    {
        var product = new AntigravityAgentProduct();
        var appConfig = new AppConfig();

        Assert.Equal(ProductIds.Antigravity, product.Id);
        Assert.False(product.SupportsEnvironments);
        Assert.False(product.SupportsWorktrees);
        Assert.True(product.SupportsInteractiveLogin);

        var valid = new SessionConfig
        {
            ProductId = ProductIds.Antigravity,
            Name = "ok",
            Directory = Path.GetTempPath(),
            Antigravity = new AntigravitySessionOptions { Mode = "plan", Effort = "medium", ExtraDirs = [Path.GetTempPath()] }
        };
        Assert.Null(product.Validate(valid, appConfig));

        var badMode = valid.Clone();
        badMode.Antigravity.Mode = "invalid";
        Assert.NotNull(product.Validate(badMode, appConfig));

        var badEffort = valid.Clone();
        badEffort.Antigravity.Effort = "ultra";
        Assert.NotNull(product.Validate(badEffort, appConfig));

        var missingDir = valid.Clone();
        missingDir.Antigravity.ExtraDirs = [Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"))];
        Assert.NotNull(product.Validate(missingDir, appConfig));
    }

    [Fact]
    public void ProductAuthHonorsConfiguredExecutableAndOfferLoginSetting()
    {
        var tempDir = Directory.CreateTempSubdirectory("liveclaude-agy-test-");
        try
        {
            var exe = Path.Combine(tempDir.FullName, OperatingSystem.IsWindows() ? "agy.exe" : "agy");
            File.WriteAllText(exe, "");

            var product = new AntigravityAgentProduct();
            var config = new AppConfig { Products = { Antigravity = { Path = exe, OfferLoginWhenRequired = false } } };

            Assert.Equal(AgentAuthStatus.Unknown, product.GetAuthState(config).Status);
            Assert.False(product.ShouldOfferLogin(config));

            var loginPlan = product.BuildLoginLaunch(config, Path.GetTempPath());
            Assert.NotNull(loginPlan);
            Assert.Equal(exe, loginPlan.ExecutablePath);
            Assert.Empty(loginPlan.Arguments);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
