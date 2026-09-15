using System.Text.Json.Serialization;

namespace LiveClaude.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstanceState
{
    /// <summary>Entry exists but is switched off in the configuration.</summary>
    Disabled,

    /// <summary>Not running, no restart pending.</summary>
    Stopped,

    /// <summary>Process spawned, waiting for the server to report itself ready.</summary>
    Starting,

    /// <summary>Remote Control server is up.</summary>
    Running,

    /// <summary>Waiting for a human: workspace trust or the Remote Control confirmation prompt.</summary>
    NeedsAttention,

    /// <summary>Crashed, waiting out the restart backoff.</summary>
    Backoff,

    /// <summary>Gave up after MaxRestarts, or the configuration is invalid.</summary>
    Failed
}

/// <summary>Point-in-time view of one supervised instance, shipped to the desktop app over IPC.</summary>
public sealed class InstanceSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    public InstanceState State { get; set; } = InstanceState.Stopped;
    public int? ProcessId { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? LastExitUtc { get; set; }
    public int? LastExitCode { get; set; }
    public int RestartCount { get; set; }
    public DateTimeOffset? NextRetryUtc { get; set; }

    /// <summary>Session URL reported by the CLI, when it could be parsed from the output.</summary>
    public string? SessionUrl { get; set; }

    /// <summary>
    /// The bridge environment this server registered on Anthropic's side, resolved after startup.
    /// Knowing it is what lets the Environments tab tell a live entry from a leftover.
    /// </summary>
    public string? EnvironmentId { get; set; }

    /// <summary>Last non-empty output line, with ANSI escapes stripped.</summary>
    public string? LastOutput { get; set; }

    /// <summary>Why the instance needs a human, when <see cref="State"/> is NeedsAttention.</summary>
    public string? AttentionReason { get; set; }

    public string? LastError { get; set; }

    public TimeSpan? Uptime => StartedUtc is { } s && State is InstanceState.Running or InstanceState.Starting or InstanceState.NeedsAttention
        ? DateTimeOffset.UtcNow - s
        : null;
}
