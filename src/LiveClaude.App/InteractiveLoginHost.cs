using LiveClaude.Core.Model;
using LiveClaude.Core.Products;

namespace LiveClaude.App;

/// <summary>
/// Product-agnostic entry point for interactive agent sign-in. Resolves a login <see cref="Abstractions.LaunchPlan"/>
/// from <see cref="IAgentAuth"/> and opens it in the embedded terminal (UI process identity).
/// </summary>
public sealed class InteractiveLoginHost
{
    private readonly Func<AppConfig> _config;
    private readonly Action<string, string[], string?> _openTerminal;

    public InteractiveLoginHost(Func<AppConfig> config, Action<string, string[], string?> openTerminal)
    {
        _config = config;
        _openTerminal = openTerminal;
    }

    /// <summary>Products that can run an interactive login in the terminal.</summary>
    public IReadOnlyList<IAgentProduct> LoginCapableProducts =>
        ProductHost.All
            .Where(p => p.AsAuth() is { SupportsInteractiveLogin: true })
            .ToList();

    /// <summary>
    /// Starts interactive login for <paramref name="productId"/>. Returns false when the product
    /// cannot login or the CLI is missing (after showing a warning).
    /// </summary>
    public async Task<bool> StartLoginAsync(string productId, string? workingDirectory = null)
    {
        var product = ProductHost.TryGet(productId);
        var auth = product.AsAuth();
        if (product is null || auth is null || !auth.SupportsInteractiveLogin)
        {
            await Dialogs.ShowWarningAsync(
                "This agent product does not support interactive sign-in from LiveClaude.",
                "Sign in");
            return false;
        }

        var config = _config();
        var directory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : workingDirectory.Trim();

        var plan = auth.BuildLoginLaunch(config, directory);
        if (plan is null)
        {
            await Dialogs.ShowWarningAsync(
                $"{product.DisplayName} CLI was not found. Set its path in Settings → Products, or install it first.",
                $"Sign in — {product.DisplayName}");
            return false;
        }

        _openTerminal(plan.WorkingDirectory, plan.Arguments.ToArray(), plan.ExecutablePath);
        return true;
    }

    /// <summary>
    /// Asks once per product that should be offered login (CLI present, signed out/expired).
    /// </summary>
    public async Task OfferPendingLoginsAsync(IEnumerable<string>? productIds = null)
    {
        var config = _config();
        var ids = productIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var product in LoginCapableProducts)
        {
            if (ids is { Count: > 0 } && !ids.Contains(product.Id))
                continue;

            var auth = product.AsAuth()!;
            if (!auth.ShouldOfferLogin(config))
                continue;

            if (product.ResolveExecutable(config) is null)
                continue;

            var state = auth.GetAuthState(config);
            if (state.Status is not (AgentAuthStatus.SignedOut or AgentAuthStatus.Expired))
                continue;

            var detail = string.IsNullOrWhiteSpace(state.UserMessage)
                ? $"{product.DisplayName} is not signed in on this machine."
                : state.UserMessage;

            var yes = await Dialogs.ConfirmAsync(
                $"{detail}{Environment.NewLine}{Environment.NewLine}Sign in now in the embedded terminal?",
                $"Sign in — {product.DisplayName}");

            if (yes)
                await StartLoginAsync(product.Id);
        }
    }
}
