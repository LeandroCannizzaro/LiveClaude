using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using Microsoft.Extensions.Logging;

[assembly: LiveClaudeProduct(typeof(LiveClaude.Product.Claude.ClaudeAgentProduct))]

namespace LiveClaude.Product.Claude;

/// <summary>Claude Code <c>remote-control</c> product.</summary>
public sealed class ClaudeAgentProduct : IAgentProduct
{
    public string Id => ProductIds.Claude;

    public string DisplayName => "Claude Code";

    public bool SupportsEnvironments => true;

    public bool SupportsWorktrees => true;

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

    public IOutputInterpreter CreateOutputInterpreter() => new ClaudeOutputInterpreter();
}
