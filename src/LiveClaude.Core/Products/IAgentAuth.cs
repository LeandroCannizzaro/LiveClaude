using LiveClaude.Abstractions;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Products;

/// <summary>Whether a product considers the current user signed in for supervised use.</summary>
public enum AgentAuthStatus
{
    /// <summary>No reliable local probe; do not auto-offer login.</summary>
    Unknown = 0,

    SignedIn,
    SignedOut,
    Expired
}

/// <summary>Product-owned auth snapshot. The shell must not interpret how it was derived.</summary>
public sealed record AgentAuthState(AgentAuthStatus Status, string? UserMessage);

/// <summary>
/// Interactive sign-in for an agent product. Implemented by product plugins; Core/App only call
/// through this contract and never hardcode vendor login argv or credential paths.
/// </summary>
public interface IAgentAuth
{
    /// <summary>True when the product can launch an interactive CLI login in the embedded terminal.</summary>
    bool SupportsInteractiveLogin { get; }

    /// <summary>Product-owned check (credentials file, key path, …).</summary>
    AgentAuthState GetAuthState(AppConfig appConfig);

    /// <summary>
    /// When true, the shell may offer sign-in at startup / on auth failures for this product.
    /// </summary>
    bool ShouldOfferLogin(AppConfig appConfig);

    /// <summary>
    /// Launch plan for interactive login in the UI-process terminal, or null when the CLI is missing
    /// or login is not available.
    /// </summary>
    LaunchPlan? BuildLoginLaunch(AppConfig appConfig, string workingDirectory);
}
