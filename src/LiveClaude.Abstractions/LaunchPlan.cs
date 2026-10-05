namespace LiveClaude.Abstractions;

/// <summary>Everything needed to start one supervised agent process in a PTY.</summary>
public sealed record LaunchPlan(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>Context the product needs beyond the session and app config when building a launch.</summary>
public sealed record LaunchContext(
    DateTimeOffset? LastStopUtc = null,
    DateTimeOffset? NowUtc = null);
