using LiveClaude.Abstractions;
using LiveClaude.Core.Model;
using LiveClaude.Core.Products;
using Microsoft.Extensions.Logging;

[assembly: LiveClaudeProduct(typeof(LiveClaude.Product.Cursor.CursorAgentProduct))]

namespace LiveClaude.Product.Cursor;

/// <summary>Cursor My Machines worker product (<c>agent worker start</c>).</summary>
public sealed class CursorAgentProduct : IAgentProduct, IAgentAuth
{
    private readonly WindowsBetterSqlite3Patcher _patcher;

    public CursorAgentProduct() : this(new WindowsBetterSqlite3Patcher())
    {
    }

    public CursorAgentProduct(WindowsBetterSqlite3Patcher patcher)
    {
        _patcher = patcher;
    }

    public string Id => ProductIds.Cursor;

    public string DisplayName => "Cursor";

    public bool SupportsEnvironments => false;

    public bool SupportsWorktrees => false;

    public bool SupportsInteractiveLogin => true;

    public string? ResolveExecutable(AppConfig appConfig) =>
        CursorAgentLocator.Locate(appConfig.Products.Cursor.Path)?.Path;

    public IReadOnlyList<DiscoveredCli> ListInstalls(AppConfig appConfig) =>
        CursorAgentLocator.FindAll(appConfig.Products.Cursor.Path)
            .Select(i => new DiscoveredCli(i.Path, i.Version, i.Source))
            .ToList();

    public string? Validate(SessionConfig session, AppConfig appConfig)
    {
        foreach (var dir in session.Cursor.ExtraWorkerDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            if (!Directory.Exists(dir.Trim()))
                return $"Extra worker directory not found: {dir.Trim()}";
        }

        var apiKeyPath = session.Cursor.ApiKeyPath ?? appConfig.Products.Cursor.ApiKeyPath;
        if (!string.IsNullOrWhiteSpace(apiKeyPath) && !File.Exists(apiKeyPath))
            return $"API key file not found: {apiKeyPath}";

        var tokenFile = session.Cursor.AuthTokenFile ?? appConfig.Products.Cursor.AuthTokenFile;
        if (!string.IsNullOrWhiteSpace(tokenFile) && !File.Exists(tokenFile))
            return $"Auth token file not found: {tokenFile}";

        return null;
    }

    public async Task EnsureReadyAsync(SessionConfig session, AppConfig appConfig, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (appConfig.Products.Cursor.AutoPatchWindowsSqlite)
            await _patcher.EnsurePatchedAsync(logger, cancellationToken).ConfigureAwait(false);
    }

    public LaunchPlan BuildLaunch(SessionConfig session, AppConfig appConfig, LaunchContext context)
    {
        var executable = ResolveExecutable(appConfig)
            ?? appConfig.Products.Cursor.Path
            ?? CursorAgentLocator.FallbackExecutableName;

        // Interactive login when requested and no key/token is configured.
        var hasKey = !string.IsNullOrWhiteSpace(session.Cursor.ApiKeyPath ?? appConfig.Products.Cursor.ApiKeyPath);
        var hasToken = !string.IsNullOrWhiteSpace(session.Cursor.AuthTokenFile ?? appConfig.Products.Cursor.AuthTokenFile);

        IReadOnlyList<string> args;
        if (session.Cursor.RunLoginInTerminal && !hasKey && !hasToken)
            args = CursorArgs.BuildLogin();
        else
            args = CursorArgs.BuildWorkerStart(session, appConfig);

        var environment = new Dictionary<string, string>(session.Environment, StringComparer.OrdinalIgnoreCase);
        return new LaunchPlan(executable, args, session.Directory, environment);
    }

    public AgentAuthState GetAuthState(AppConfig appConfig)
    {
        var apiKeyPath = appConfig.Products.Cursor.ApiKeyPath;
        if (!string.IsNullOrWhiteSpace(apiKeyPath) && File.Exists(apiKeyPath))
            return new AgentAuthState(AgentAuthStatus.SignedIn, null);

        var tokenFile = appConfig.Products.Cursor.AuthTokenFile;
        if (!string.IsNullOrWhiteSpace(tokenFile) && File.Exists(tokenFile))
            return new AgentAuthState(AgentAuthStatus.SignedIn, null);

        // PreferExistingLogin relies on a prior `agent login` with no local probe we can trust here.
        if (appConfig.Products.Cursor.PreferExistingLogin)
            return new AgentAuthState(AgentAuthStatus.Unknown, null);

        return new AgentAuthState(
            AgentAuthStatus.SignedOut,
            "Cursor Agent is not signed in. Run interactive login or set an API key / auth token file.");
    }

    public bool ShouldOfferLogin(AppConfig appConfig) =>
        appConfig.Products.Cursor.OfferLoginWhenRequired;

    public LaunchPlan? BuildLoginLaunch(AppConfig appConfig, string workingDirectory)
    {
        var executable = ResolveExecutable(appConfig);
        if (executable is null)
            return null;

        return new LaunchPlan(
            executable,
            CursorArgs.BuildLogin(),
            ResolveWorkingDirectory(workingDirectory),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    public IOutputInterpreter CreateOutputInterpreter() => new CursorOutputInterpreter();

    private static string ResolveWorkingDirectory(string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            return workingDirectory;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
