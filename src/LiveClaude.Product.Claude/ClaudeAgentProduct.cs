using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using Microsoft.Extensions.Logging;

[assembly: LiveClaudeProduct(typeof(LiveClaude.Product.Claude.ClaudeAgentProduct))]

namespace LiveClaude.Product.Claude;

/// <summary>Claude Code <c>remote-control</c> product.</summary>
public sealed class ClaudeAgentProduct : IAgentProduct, IAgentAuth
{
    public string Id => ProductIds.Claude;

    public string DisplayName => "Claude Code";

    public bool SupportsEnvironments => true;

    public bool SupportsWorktrees => true;

    public bool SupportsInteractiveLogin => true;

    public string? ResolveExecutable(AppConfig appConfig) =>
        ClaudeLocator.Locate(appConfig.Products.Claude.Path)?.Path;

    public IReadOnlyList<DiscoveredCli> ListInstalls(AppConfig appConfig) =>
        ClaudeLocator.FindAll(appConfig.Products.Claude.Path)
            .Select(i => new DiscoveredCli(i.Path, i.Version, i.Source))
            .ToList();

    public string? Validate(SessionConfig session, AppConfig appConfig) =>
        session.ValidateClaudeOptions();

    public Task EnsureReadyAsync(SessionConfig session, AppConfig appConfig, ILogger logger, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public LaunchPlan BuildLaunch(SessionConfig session, AppConfig appConfig, LaunchContext context)
    {
        var executable = ResolveExecutable(appConfig)
            ?? appConfig.Products.Claude.Path
            ?? ClaudeLocator.FallbackExecutableName;

        var args = ClaudeArgs.BuildRemoteControl(session, context.LastStopUtc, context.NowUtc);
        var environment = new Dictionary<string, string>(session.Environment, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(session.Model))
            environment["ANTHROPIC_MODEL"] = session.Model!;

        return new LaunchPlan(executable, args, session.Directory, environment);
    }

    public AgentAuthState GetAuthState(AppConfig appConfig)
    {
        var credentials = PlatformLoader.Current.Claude.LoadCredentials();
        if (credentials is null)
        {
            return new AgentAuthState(
                AgentAuthStatus.SignedOut,
                "Claude Code is not signed in on this machine.");
        }

        if (credentials.IsExpired)
        {
            return new AgentAuthState(
                AgentAuthStatus.Expired,
                "The Claude Code token has expired. Sign in again to refresh it.");
        }

        return new AgentAuthState(AgentAuthStatus.SignedIn, null);
    }

    public bool ShouldOfferLogin(AppConfig appConfig) =>
        appConfig.Products.Claude.OfferLoginWhenRequired;

    public LaunchPlan? BuildLoginLaunch(AppConfig appConfig, string workingDirectory)
    {
        var executable = ResolveExecutable(appConfig);
        if (executable is null)
            return null;

        return new LaunchPlan(
            executable,
            ClaudeArgs.BuildLogin(),
            ResolveWorkingDirectory(workingDirectory),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public IOutputInterpreter CreateOutputInterpreter() => new ClaudeOutputInterpreter();

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            return workingDirectory;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
