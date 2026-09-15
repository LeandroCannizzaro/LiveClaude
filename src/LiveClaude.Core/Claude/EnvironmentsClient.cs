using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Claude;

/// <summary>
/// A failed call to the environments API, carrying everything needed to explain it to the user and
/// to find it again in Anthropic's logs.
/// </summary>
public sealed class EnvironmentsApiException : Exception
{
    public EnvironmentsApiException(string message, int statusCode = 0, string? requestId = null, bool requiresForce = false)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
        RequiresForce = requiresForce;
    }

    public int StatusCode { get; }

    /// <summary>Anthropic's request id, worth quoting in a bug report.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// True for the 409 that says the environment still has session records attached. Deleting it
    /// needs <c>force=true</c>, which also removes those sessions.
    /// </summary>
    public bool RequiresForce { get; }
}

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
    private readonly Action<string>? _log;

    public EnvironmentsClient(
        HttpClient? http = null,
        Func<ClaudeCredentials?>? credentialsFactory = null,
        Action<string>? log = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _credentialsFactory = credentialsFactory ?? (() => ClaudeCredentials.Load());
        _log = log;
    }

    public async Task<IReadOnlyList<RemoteEnvironment>> ListAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, BaseAddress);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var requestId = ReadRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
            var failure = Describe(response.StatusCode, body, requestId);
            _log?.Invoke($"GET {BaseAddress} -> {(int)response.StatusCode} (request-id {requestId ?? "n/a"}): {failure.Message}");
            throw failure;
        }

        var environments = Parse(body);
        _log?.Invoke($"GET {BaseAddress} -> 200, {environments.Count} environment(s) (request-id {requestId ?? "n/a"}).");
        return environments;
    }

    /// <summary>
    /// Removes an environment permanently. Without <paramref name="force"/> the API refuses with 409
    /// when session records are still attached; forcing deletes those too.
    /// </summary>
    public async Task DeleteAsync(string environmentId, bool force = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            throw new ArgumentException("An environment id is required.", nameof(environmentId));

        var url = BuildDeleteUrl(environmentId, force);

        using var request = CreateRequest(HttpMethod.Delete, url);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var requestId = ReadRequestId(response);

        if (response.IsSuccessStatusCode)
        {
            _log?.Invoke($"DELETE {environmentId} (force={force}) -> {(int)response.StatusCode} (request-id {requestId ?? "n/a"}).");
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log?.Invoke($"DELETE {environmentId} (force={force}) -> 404, already gone (request-id {requestId ?? "n/a"}).");
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var failure = Describe(response.StatusCode, body, requestId);
        _log?.Invoke($"DELETE {environmentId} (force={force}) -> {(int)response.StatusCode} (request-id {requestId ?? "n/a"}): {failure.Message}");
        throw failure;
    }

    public static string BuildDeleteUrl(string environmentId, bool force) =>
        force
            ? $"{BaseAddress}/{Uri.EscapeDataString(environmentId)}?force=true"
            : $"{BaseAddress}/{Uri.EscapeDataString(environmentId)}";

    /// <summary>Turns an API failure into something worth showing a person. Public so it can be tested.</summary>
    public static EnvironmentsApiException Describe(HttpStatusCode status, string body, string? requestId)
    {
        var apiMessage = TryReadApiMessage(body);
        var code = (int)status;

        // 409 + "force" is the API telling us the environment still has session records attached.
        var requiresForce = status == HttpStatusCode.Conflict &&
                            apiMessage.Contains("force", StringComparison.OrdinalIgnoreCase);

        var message = status switch
        {
            HttpStatusCode.Conflict when requiresForce =>
                $"{apiMessage} These are session records left behind by servers that were killed; forcing removes them together with the environment.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Anthropic rejected the request ({code}). Sign in again with 'claude /login'. {apiMessage}".Trim(),
            HttpStatusCode.NotFound =>
                $"Not found ({code}). {apiMessage}".Trim(),
            HttpStatusCode.TooManyRequests =>
                "Rate limited by the API. Wait a moment and try again.",
            _ => $"The environments API returned {code}. {apiMessage}".Trim()
        };

        return new EnvironmentsApiException(message, code, requestId, requiresForce);
    }

    /// <summary>Parses the list payload. Public so it can be tested without the network.</summary>
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
                          ?? throw new EnvironmentsApiException(
                              "No Claude Code sign-in found on this machine. Run 'claude /login' first.");

        if (credentials.IsExpired)
            throw new EnvironmentsApiException(
                "The Claude Code token has expired. Start a Claude Code session to refresh it, then try again.");

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.UserAgent.ParseAdd("LiveClaude");
        return request;
    }

    private static string? ReadRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("request-id", out var values) ? values.FirstOrDefault() : null;

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
