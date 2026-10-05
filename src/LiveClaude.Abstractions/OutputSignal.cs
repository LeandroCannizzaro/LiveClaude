namespace LiveClaude.Abstractions;

/// <summary>Signals the supervisor and UI care about when interpreting agent CLI output.</summary>
public enum OutputSignal
{
    None,

    /// <summary>The process reported itself ready / connected.</summary>
    Ready,

    /// <summary>The CLI is asking the workspace-trust question.</summary>
    TrustPrompt,

    /// <summary>The CLI is asking a one-time confirmation (e.g. Enable Remote Control).</summary>
    RemoteControlConfirmation,

    /// <summary>Authentication is missing or expired.</summary>
    LoginRequired,

    /// <summary>A fatal configuration error that a restart will not fix.</summary>
    FatalError
}

/// <summary>
/// Turns raw CLI output into the few signals the supervisor and the UI care about: is the agent up,
/// is it blocked on a human, did it print a session URL.
/// </summary>
public interface IOutputInterpreter
{
    string StripAnsi(string text);

    /// <summary>Extracts a session / dashboard URL from a chunk of output, if present.</summary>
    string? FindSessionUrl(string text);

    OutputSignal Classify(string text);

    string Describe(OutputSignal signal);

    /// <summary>Splits a chunk into printable lines for the log file, dropping empty noise.</summary>
    IEnumerable<string> ToLogLines(string chunk);
}
