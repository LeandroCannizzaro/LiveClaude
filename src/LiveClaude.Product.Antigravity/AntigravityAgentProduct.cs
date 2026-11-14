using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using Microsoft.Extensions.Logging;

[assembly: LiveClaudeProduct(typeof(LiveClaude.Product.Antigravity.AntigravityAgentProduct))]

namespace LiveClaude.Product.Antigravity;

/// <summary>Google Antigravity CLI (<c>agy --remote-control</c>) product.</summary>
public sealed class AntigravityAgentProduct : IAgentProduct, IAgentAuth
{
    public static readonly IReadOnlyList<string> ValidModes = ["accept-edits", "auto", "plan"];
    public static readonly IReadOnlyList<string> ValidEfforts = ["low", "medium", "high", "xhigh", "max"];

    public string Id => ProductIds.Antigravity;

    public string DisplayName => "Antigravity";

    public bool SupportsEnvironments => false;

    public bool SupportsWorktrees => false;

    public bool SupportsInteractiveLogin => true;

    public string? ResolveExecutable(AppConfig appConfig) =>
        AntigravityLocator.Locate(appConfig.Products.Antigravity.Path)?.Path;

    public IReadOnlyList<DiscoveredCli> ListInstalls(AppConfig appConfig) =>
        AntigravityLocator.FindAll(appConfig.Products.Antigravity.Path)
            .Select(i => new DiscoveredCli(i.Path, i.Version, i.Source))
            .ToList();

    public string? Validate(SessionConfig session, AppConfig appConfig)
    {
        var options = session.Antigravity;

        if (!string.IsNullOrWhiteSpace(options.Mode) &&
            !ValidModes.Contains(options.Mode.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return $"Invalid Antigravity mode '{options.Mode}'. Expected one of: {string.Join(", ", ValidModes)}.";
        }

        if (!string.IsNullOrWhiteSpace(options.Effort) &&
            !ValidEfforts.Contains(options.Effort.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return $"Invalid Antigravity effort '{options.Effort}'. Expected one of: {string.Join(", ", ValidEfforts)}.";
        }

        foreach (var dir in options.ExtraDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            if (!Directory.Exists(dir.Trim()))
                return $"Additional workspace directory not found: {dir.Trim()}";
        }

        return null;
    }

    public Task EnsureReadyAsync(SessionConfig session, AppConfig appConfig, ILogger logger, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public LaunchPlan BuildLaunch(SessionConfig session, AppConfig appConfig, LaunchContext context)
    {
        var executable = ResolveExecutable(appConfig)
            ?? appConfig.Products.Antigravity.Path
            ?? AntigravityLocator.FallbackExecutableName;

        var args = AntigravityArgs.BuildRemoteControl(session);
        var environment = new Dictionary<string, string>(session.Environment, StringComparer.OrdinalIgnoreCase);

        return new LaunchPlan(executable, args, session.Directory, environment);
    }

    public AgentAuthState GetAuthState(AppConfig appConfig)
    {
        // agy stores credentials in the OS keyring; there is no reliable cross-platform local probe
        // without spawning the CLI, so report Unknown and let interactive login happen on demand.
        return new AgentAuthState(AgentAuthStatus.Unknown, null);
    }

    public bool ShouldOfferLogin(AppConfig appConfig) =>
        appConfig.Products.Antigravity.OfferLoginWhenRequired;

    public LaunchPlan? BuildLoginLaunch(AppConfig appConfig, string workingDirectory)
    {
        var executable = ResolveExecutable(appConfig);
        if (executable is null)
            return null;

        return new LaunchPlan(
            executable,
            AntigravityArgs.BuildLogin(),
            ResolveWorkingDirectory(workingDirectory),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public IOutputInterpreter CreateOutputInterpreter() => new AntigravityOutputInterpreter();

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            return workingDirectory;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
