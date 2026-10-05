using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using Microsoft.Extensions.Logging;

namespace LiveClaude.Core.Products;

/// <summary>
/// One supervised agent product (Claude Code remote-control, Cursor My Machines worker, …).
/// Implementations live in late-loaded <c>LiveClaude.Product.*.dll</c> assemblies.
/// </summary>
public interface IAgentProduct
{
    /// <summary>Stable id stored on sessions: <c>claude</c>, <c>cursor</c>, …</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>True when the product can resolve Anthropic-style bridge environments.</summary>
    bool SupportsEnvironments { get; }

    /// <summary>True when the dashboard should poll <c>git worktree list</c> for this product.</summary>
    bool SupportsWorktrees { get; }

    /// <summary>Resolves the CLI executable path, or null when nothing usable was found.</summary>
    string? ResolveExecutable(AppConfig appConfig);

    /// <summary>Configured + discovered CLI installs for the Settings UI.</summary>
    IReadOnlyList<DiscoveredCli> ListInstalls(AppConfig appConfig);

    /// <summary>
    /// Product-specific validation. Returns a human-readable error, or null when the session is
    /// usable. Common checks (name, directory exists) stay on <see cref="SessionConfig"/>.
    /// </summary>
    string? Validate(SessionConfig session, AppConfig appConfig);

    /// <summary>
    /// Pre-start hook (no-op for Claude; Windows better-sqlite3 patch for Cursor). Called before
    /// every launch attempt.
    /// </summary>
    Task EnsureReadyAsync(SessionConfig session, AppConfig appConfig, ILogger logger, CancellationToken cancellationToken = default);

    LaunchPlan BuildLaunch(SessionConfig session, AppConfig appConfig, LaunchContext context);

    IOutputInterpreter CreateOutputInterpreter();
}

/// <summary>A CLI install shown in Settings.</summary>
public sealed record DiscoveredCli(string Path, string? Version, string Source);

/// <summary>Well-known product ids persisted on <see cref="SessionConfig.ProductId"/>.</summary>
public static class ProductIds
{
    public const string Claude = "claude";
    public const string Cursor = "cursor";
}
