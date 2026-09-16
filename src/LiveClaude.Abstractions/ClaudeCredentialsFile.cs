using System.Text.Json;

namespace LiveClaude.Abstractions;

/// <summary>
/// Reads the OAuth token Claude Code writes to <c>~/.claude/.credentials.json</c>.
///
/// Windows and Linux both use that file, so the parser is shared. macOS is the exception: the CLI
/// keeps the token in the Keychain there, and its platform assembly reads it from the Keychain and
/// only falls back to this file.
///
/// The token is only ever sent to api.anthropic.com, which is the account that issued it.
/// </summary>
public static class ClaudeCredentialsFile
{
    /// <summary>The usual location on any platform that stores credentials in a file.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    /// <summary>Returns the stored credentials, or null when there are none to read.</summary>
    public static ClaudeCredentials? Read(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
                return null;

            if (!oauth.TryGetProperty("accessToken", out var token) || token.GetString() is not { Length: > 0 } accessToken)
                return null;

            DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out var expiry) && expiry.TryGetInt64(out var epochMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
                : null;

            var subscription = oauth.TryGetProperty("subscriptionType", out var sub) ? sub.GetString() : null;

            return new ClaudeCredentials(accessToken, expiresAt, subscription);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
