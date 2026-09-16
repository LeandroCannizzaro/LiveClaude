namespace LiveClaude.Abstractions;

public sealed record ClaudeCandidate(string Path, string Source);

/// <summary>The OAuth token Claude Code stores for the signed-in user.</summary>
public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset? ExpiresAt, string? SubscriptionType)
{
    public bool IsExpired => ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;
}

/// <summary>
/// Finds the Claude Code CLI and the credentials it signed in with.
///
/// Both differ per platform in ways that are easy to miss: the executable is <c>claude.exe</c> on
/// Windows and an extensionless file — often a symlink to a script — elsewhere, and macOS keeps the
/// OAuth token in the Keychain rather than in <c>~/.claude/.credentials.json</c>.
/// </summary>
public interface IClaudeDiscovery
{
    /// <summary>"claude.exe" or "claude". Also the fallback when nothing is found and nothing is configured.</summary>
    string ExecutableName { get; }

    /// <summary>Candidate locations in preference order. Existence is checked by the caller.</summary>
    IEnumerable<ClaudeCandidate> EnumerateCandidates();

    /// <summary>
    /// True when the path is a file this process could actually execute. On POSIX that is an
    /// <c>access(X_OK)</c> check, not just "the file exists": <c>~/.local/bin/claude</c> is
    /// frequently a dangling symlink left by an uninstall.
    /// </summary>
    bool IsExecutable(string path);

    /// <summary>Asks the CLI for its version. Null when it cannot be run.</summary>
    string? ReadVersion(string executablePath);

    /// <summary>Returns the stored credentials, or null when the user has not signed in here.</summary>
    ClaudeCredentials? LoadCredentials();

    /// <summary>Where the credentials came from, for the message shown when there are none.</summary>
    string CredentialSourceDescription { get; }
}

/// <summary>Opening things the way the desktop environment would.</summary>
public interface IShellIntegration
{
    /// <summary>Shows a file or folder in the file manager: Explorer, the desktop's handler, Finder.</summary>
    void Reveal(string path);

    void OpenUrl(string url);
}
