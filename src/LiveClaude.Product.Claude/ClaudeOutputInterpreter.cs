using System.Text;
using System.Text.RegularExpressions;
using LiveClaude.Abstractions;

namespace LiveClaude.Product.Claude;

/// <summary>
/// Turns raw Claude CLI output into the few signals the supervisor and the UI care about.
/// </summary>
public sealed class ClaudeOutputInterpreter : IOutputInterpreter
{
    private static readonly Regex AnsiPattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[PX^_][^\x1B]*\x1B\\)",
        RegexOptions.Compiled);

    private static readonly Regex SessionUrlPattern = new(
        @"https://claude\.ai/code/[A-Za-z0-9\-_]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string StripAnsi(string text) =>
        AnsiPattern.Replace(text, string.Empty).Replace("\r", string.Empty);

    public string? FindSessionUrl(string text)
    {
        var match = SessionUrlPattern.Match(StripAnsi(text));
        return match.Success ? match.Value : null;
    }

    public OutputSignal Classify(string text)
    {
        var clean = StripAnsi(text);

        if (Contains(clean, "Enable Remote Control?") || Contains(clean, "Enable Remote Control ("))
            return OutputSignal.RemoteControlConfirmation;

        if (Contains(clean, "Do you trust the files in this folder") ||
            Contains(clean, "trust the files in this directory") ||
            Contains(clean, "Yes, I trust this") ||
            Contains(clean, "workspace trust"))
            return OutputSignal.TrustPrompt;

        if (Contains(clean, "/login") && (Contains(clean, "sign in") || Contains(clean, "not authenticated") || Contains(clean, "log in")))
            return OutputSignal.LoginRequired;

        if (Contains(clean, "Invalid API key") || Contains(clean, "OAuth token has expired") || Contains(clean, "Please run /login"))
            return OutputSignal.LoginRequired;

        if (Contains(clean, "is already served by"))
            return OutputSignal.FolderInUse;

        if (Contains(clean, "not carried over to the sessions") ||
            Contains(clean, "unknown option") ||
            Contains(clean, "Remote Control is not available") ||
            Contains(clean, "is disabled by your organization"))
            return OutputSignal.FatalError;

        if (SessionUrlPattern.IsMatch(clean) ||
            Contains(clean, "Waiting for connections") ||
            Contains(clean, "Remote Control active") ||
            Contains(clean, "Serving session") ||
            Contains(clean, "Press space"))
            return OutputSignal.Ready;

        return OutputSignal.None;
    }

    public string Describe(OutputSignal signal) => signal switch
    {
        OutputSignal.TrustPrompt => "Workspace trust must be accepted once for this directory. Open the Terminal tab and answer the prompt.",
        OutputSignal.RemoteControlConfirmation => "Remote Control asks for a one-time confirmation (y/n). Open the Terminal tab and answer it.",
        OutputSignal.LoginRequired => "Claude Code is not signed in. Run 'claude /login' (or 'claude setup-token') as this Windows user.",
        OutputSignal.FatalError => "The CLI refused to start with the current configuration.",
        OutputSignal.FolderInUse => "Another claude remote-control on this machine already serves this folder (another LiveClaude supervisor, or a terminal). It is retried automatically; stop the other one to start sooner.",
        _ => ""
    };

    public IEnumerable<string> ToLogLines(string chunk)
    {
        foreach (var raw in StripAnsi(chunk).Split('\n'))
        {
            var line = TrimControl(raw);
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    private static string TrimControl(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c) || c == '\t')
                sb.Append(c);
        }

        return sb.ToString().TrimEnd();
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
