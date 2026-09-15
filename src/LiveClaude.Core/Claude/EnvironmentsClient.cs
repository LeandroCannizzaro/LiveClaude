using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Claude;

/// <summary>
/// Talks to the Anthropic environments API with the signed-in user's own Claude Code token.
///
/// Every <c>claude remote-control</c> process registers a bridge environment, and nothing removes it
/// when the process goes away: that is why the session picker ends up showing the same project three
/// times, one live and two dead. The API is in beta (<c>environments-2025-11-01</c>) and may change.
/// </summary>
public sealed class EnvironmentsClient : IDisposable
{
    public const string BetaHeader = "environments-2025-11-01";
    private const string BaseAddress = "https://api.anthropic.com/v1/environments";

    private readonly HttpClient _http;
    private readonly Func<ClaudeCredentials?> _credentialsFactory;

    public EnvironmentsClient(HttpClient? http = null, Func<ClaudeCredentials?>? credentialsFactory = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _credentialsFactory = credentialsFactory ?? (() => ClaudeCredentials.Load());
    }

    /// <summary>Thrown when the API refuses the call; the message is meant to be shown to the user.</summary>
    public sealed class EnvironmentsException(string message) : Exception(message);

    public async Task<IReadOnlyList<RemoteEnvironment>> ListAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, BaseAddress);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new EnvironmentsException(DescribeFailure(response.StatusCode, body));

        return Parse(body);
    }

    /// <summary>Removes an environment. This is permanent and cannot be undone.</summary>
    public async Task DeleteAsync(string environmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            throw new ArgumentException("An environment id is required.", nameof(environmentId));

        using var request = CreateRequest(HttpMethod.Delete, $"{BaseAddress}/{Uri.EscapeDataString(environmentId)}");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new EnvironmentsException(DescribeFailure(response.StatusCode, body));
    }

    /// <summary>Parses the list payload. Kept internal-but-public so it can be tested without the network.</summary>
    public static IReadOnlyList<RemoteEnvironment> Parse(string json)
    {
        var result = new List<RemoteEnvironment>();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in data.EnumerateArray())
        {
            var environment = new RemoteEnvironment
            {
                Id = GetString(item, "id") ?? "",
                Name = GetString(item, "name") ?? "",
                Description = GetString(item, "description"),
                State = GetString(item, "state"),
                CreatedUtc = GetDate(item, "created_at"),
                UpdatedUtc = GetDate(item, "updated_at"),
                ArchivedUtc = GetDate(item, "archived_at")
            };

            if (item.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
            {
                environment.ConfigType = GetString(config, "type");
                environment.MachineName = GetString(config, "machine_name");
                environment.Directory = GetString(config, "directory");
                environment.Branch = GetString(config, "branch");
                environment.GitRepoUrl = GetString(config, "git_repo_url");
            }

            if (!string.IsNullOrEmpty(environment.Id))
                result.Add(environment);
        }

        return result;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var credentials = _credentialsFactory()
                          ?? throw new EnvironmentsException(
                              "No Claude Code sign-in found on this machine. Run 'claude /login' first.");

        if (credentials.IsExpired)
            throw new EnvironmentsException(
                "The Claude Code token has expired. Start a Claude Code session to refresh it, then try again.");

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.UserAgent.ParseAdd("LiveClaude");
        return request;
    }

    private static string DescribeFailure(HttpStatusCode status, string body)
    {
        var message = TryReadApiMessage(body);

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Anthropic rejected the request ({(int)status}). Sign in again with 'claude /login'. {message}".Trim(),
            HttpStatusCode.NotFound =>
                $"The environments API is not available for this account ({(int)status}). {message}".Trim(),
            HttpStatusCode.TooManyRequests =>
                "Rate limited by the API. Wait a moment and try again.",
            _ => $"The environments API returned {(int)status}. {message}".Trim()
        };
    }

    private static string TryReadApiMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // fall through: the body was not JSON
        }

        return body.Length > 200 ? body[..200] : body;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    public void Dispose() => _http.Dispose();
}
