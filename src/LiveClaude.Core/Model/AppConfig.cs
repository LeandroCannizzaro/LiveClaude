using System.Text.Json.Serialization;

namespace LiveClaude.Core.Model;

/// <summary>Restart policy applied to every supervised instance.</summary>
public sealed class BackoffSettings
{
    public int InitialSeconds { get; set; } = 10;
    public int MaxSeconds { get; set; } = 300;
    public double Multiplier { get; set; } = 2.0;

    /// <summary>Uptime after which an instance is considered healthy and the backoff resets.</summary>
    public int ResetAfterHealthySeconds { get; set; } = 120;

    /// <summary>0 = restart forever.</summary>
    public int MaxRestarts { get; set; }
}

/// <summary>The whole LiveClaude configuration, persisted as JSON under the platform config root.</summary>
public sealed class AppConfig
{
    /// <summary>
    /// Schema version. 1 = Claude-only flat fields; 2 = Products section + session ProductId.
    /// </summary>
    public int Version { get; set; } = 2;

    /// <summary>Per-product CLI paths and product-wide toggles.</summary>
    public ProductsConfig Products { get; set; } = new();

    /// <summary>
    /// Explicit path to claude.exe. Alias of <see cref="ClaudeProductSettings.Path"/> for older
    /// callers; prefer <c>Products.Claude.Path</c>. Not written to JSON (Products owns the value).
    /// </summary>
    [JsonIgnore]
    public string? ClaudePath
    {
        get => Products.Claude.Path;
        set => Products.Claude.Path = value;
    }

    /// <summary>Health probe interval, in seconds.</summary>
    public int HealthCheckSeconds { get; set; } = 30;

    /// <summary>Lines kept in memory per instance for the Logs tab.</summary>
    public int LogTailLines { get; set; } = 1000;

    /// <summary>Rolling log file size, in megabytes.</summary>
    public int LogMaxSizeMb { get; set; } = 8;

    /// <summary>Use a pseudo console (ConPTY) to host the CLI. Required for the embedded terminal.</summary>
    public bool UsePseudoConsole { get; set; } = true;

    /// <summary>
    /// How long to wait after Ctrl+C before killing a server. A clean stop lets the CLI deregister
    /// itself; killing it outright is what leaves dead entries in the session picker.
    /// </summary>
    public int GracefulStopSeconds { get; set; } = 12;

    /// <summary>
    /// Ask the API which bridge environment a server registered. Alias of
    /// <see cref="ClaudeProductSettings.TrackEnvironments"/>.
    /// </summary>
    [JsonIgnore]
    public bool TrackEnvironments
    {
        get => Products.Claude.TrackEnvironments;
        set => Products.Claude.TrackEnvironments = value;
    }

    public BackoffSettings Backoff { get; set; } = new();

    public List<SessionConfig> Sessions { get; set; } = new();

    public SessionConfig? Find(string id) =>
        Sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public AppConfig Clone() => new()
    {
        Version = Version,
        Products = Products.Clone(),
        HealthCheckSeconds = HealthCheckSeconds,
        LogTailLines = LogTailLines,
        LogMaxSizeMb = LogMaxSizeMb,
        UsePseudoConsole = UsePseudoConsole,
        GracefulStopSeconds = GracefulStopSeconds,
        Backoff = new BackoffSettings
        {
            InitialSeconds = Backoff.InitialSeconds,
            MaxSeconds = Backoff.MaxSeconds,
            Multiplier = Backoff.Multiplier,
            ResetAfterHealthySeconds = Backoff.ResetAfterHealthySeconds,
            MaxRestarts = Backoff.MaxRestarts
        },
        Sessions = Sessions.Select(s => s.Clone()).ToList()
    };
}
