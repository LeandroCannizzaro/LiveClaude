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

/// <summary>The whole LiveClaude configuration, persisted as JSON under %ProgramData%\LiveClaude.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Explicit path to claude.exe. When null the locator discovers it.</summary>
    public string? ClaudePath { get; set; }

    /// <summary>Health probe interval, in seconds.</summary>
    public int HealthCheckSeconds { get; set; } = 30;

    /// <summary>Lines kept in memory per instance for the Logs tab.</summary>
    public int LogTailLines { get; set; } = 1000;

    /// <summary>Rolling log file size, in megabytes.</summary>
    public int LogMaxSizeMb { get; set; } = 8;

    /// <summary>Use a pseudo console (ConPTY) to host the CLI. Required for the embedded terminal.</summary>
    public bool UsePseudoConsole { get; set; } = true;

    public BackoffSettings Backoff { get; set; } = new();

    public List<SessionConfig> Sessions { get; set; } = new();

    public SessionConfig? Find(string id) =>
        Sessions.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public AppConfig Clone() => new()
    {
        Version = Version,
        ClaudePath = ClaudePath,
        HealthCheckSeconds = HealthCheckSeconds,
        LogTailLines = LogTailLines,
        LogMaxSizeMb = LogMaxSizeMb,
        UsePseudoConsole = UsePseudoConsole,
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
