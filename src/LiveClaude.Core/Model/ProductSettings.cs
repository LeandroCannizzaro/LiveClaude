namespace LiveClaude.Core.Model;

/// <summary>Per-product settings under <see cref="AppConfig.Products"/>.</summary>
public sealed class ProductsConfig
{
    public ClaudeProductSettings Claude { get; set; } = new();
    public CursorProductSettings Cursor { get; set; } = new();

    public ProductsConfig Clone() => new()
    {
        Claude = Claude.Clone(),
        Cursor = Cursor.Clone()
    };
}

public sealed class ClaudeProductSettings
{
    /// <summary>Explicit path to the Claude CLI. When null the locator discovers it.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Ask the API which bridge environment a server registered, so the Environments tab can tell
    /// the live one from the leftovers. Turn off to keep LiveClaude entirely offline for Claude.
    /// </summary>
    public bool TrackEnvironments { get; set; } = true;

    public ClaudeProductSettings Clone() => new()
    {
        Path = Path,
        TrackEnvironments = TrackEnvironments
    };
}

public sealed class CursorProductSettings
{
    /// <summary>Explicit path to the Cursor <c>agent</c> CLI. When null the locator discovers it.</summary>
    public string? Path { get; set; }

    /// <summary>Optional path to a file containing a personal user API key.</summary>
    public string? ApiKeyPath { get; set; }

    /// <summary>Optional path to a file containing a user-scoped auth token.</summary>
    public string? AuthTokenFile { get; set; }

    /// <summary>
    /// Prefer the existing <c>agent login</c> session when no API key / token file is configured.
    /// </summary>
    public bool PreferExistingLogin { get; set; } = true;

    /// <summary>
    /// On Windows, automatically replace a mismatched <c>better_sqlite3.node</c> before starting a
    /// worker. No-op on Linux/macOS.
    /// </summary>
    public bool AutoPatchWindowsSqlite { get; set; } = true;

    public CursorProductSettings Clone() => new()
    {
        Path = Path,
        ApiKeyPath = ApiKeyPath,
        AuthTokenFile = AuthTokenFile,
        PreferExistingLogin = PreferExistingLogin,
        AutoPatchWindowsSqlite = AutoPatchWindowsSqlite
    };
}

/// <summary>Cursor-specific options on a supervised session.</summary>
public sealed class CursorSessionOptions
{
    /// <summary>
    /// Additional <c>--worker-dir</c> roots beyond <see cref="SessionConfig.Directory"/> (the primary
    /// assignment identity). Each path must exist.
    /// </summary>
    public List<string> ExtraWorkerDirs { get; set; } = new();

    public bool Verbose { get; set; }

    public bool Debug { get; set; }

    /// <summary>Per-session API key file; falls back to <see cref="CursorProductSettings.ApiKeyPath"/>.</summary>
    public string? ApiKeyPath { get; set; }

    /// <summary>Per-session auth token file; falls back to product settings.</summary>
    public string? AuthTokenFile { get; set; }

    /// <summary>
    /// When true and no usable credentials are found, the first launch runs <c>agent login</c> in the
    /// terminal so the user can sign in interactively (same idea as Claude trust prompts).
    /// </summary>
    public bool RunLoginInTerminal { get; set; }

    public CursorSessionOptions Clone() => new()
    {
        ExtraWorkerDirs = ExtraWorkerDirs.ToList(),
        Verbose = Verbose,
        Debug = Debug,
        ApiKeyPath = ApiKeyPath,
        AuthTokenFile = AuthTokenFile,
        RunLoginInTerminal = RunLoginInTerminal
    };
}
