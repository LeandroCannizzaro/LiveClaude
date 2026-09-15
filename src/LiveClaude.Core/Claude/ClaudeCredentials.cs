using System.Text.Json;

namespace LiveClaude.Core.Claude;

/// <summary>
/// Reads the OAuth token Claude Code stores for the signed-in user. It is only ever sent to
/// api.anthropic.com, which is the account that issued it.
/// </summary>
public sealed record ClaudeCredentials(string AccessToken, DateTimeOffset? ExpiresAt, string? SubscriptionType)
{
    public bool IsExpired => ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        ".credentials.json");

    /// <summary>Returns the stored credentials, or null when the user has not signed in on this machine.</summary>
    public static ClaudeCredentials? Load(string? path = null)
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
