using System.Text;
using System.Text.RegularExpressions;
using LiveClaude.Abstractions;

namespace LiveClaude.Product.Cursor;

/// <summary>Interprets Cursor <c>agent worker</c> output for ready / auth / fatal signals.</summary>
public sealed class CursorOutputInterpreter : IOutputInterpreter
{
    private static readonly Regex AnsiPattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[PX^_][^\x1B]*\x1B\\)",
        RegexOptions.Compiled);

    private static readonly Regex UrlPattern = new(
        @"https://cursor\.com/\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string StripAnsi(string text) =>
        AnsiPattern.Replace(text, string.Empty).Replace("\r", string.Empty);

    public string? FindSessionUrl(string text)
    {
        var match = UrlPattern.Match(StripAnsi(text));
        return match.Success ? match.Value.TrimEnd('.', ',', ')', ']') : null;
    }

    public OutputSignal Classify(string text)
    {
        var clean = StripAnsi(text);

        if (Contains(clean, "NODE_MODULE_VERSION") ||
            Contains(clean, "was compiled against a different Node.js") ||
            Contains(clean, "better_sqlite3"))
            return OutputSignal.FatalError;

        if (Contains(clean, "not authenticated") ||
            Contains(clean, "Please run 'agent login'") ||
            Contains(clean, "Please run \"agent login\"") ||
            Contains(clean, "login required") ||
            Contains(clean, "Unauthorized") ||
            Contains(clean, "Invalid API key") ||
            Contains(clean, "authentication failed"))
            return OutputSignal.LoginRequired;

        if (Contains(clean, "unknown option") ||
            (Contains(clean, "worker-dir") && Contains(clean, "must exist")) ||
            Contains(clean, "No such file") ||
            (Contains(clean, "service account") && Contains(clean, "My Machines")))
            return OutputSignal.FatalError;

        if (Contains(clean, "connected") ||
            Contains(clean, "Worker started") ||
            Contains(clean, "Listening for") ||
            Contains(clean, "ready to accept") ||
            Contains(clean, "Registered worker") ||
            Contains(clean, "Waiting for agents") ||
            Contains(clean, "bridge mode"))
            return OutputSignal.Ready;

        return OutputSignal.None;
    }

    public string Describe(OutputSignal signal) => signal switch
    {
        OutputSignal.LoginRequired =>
            "Cursor Agent is not signed in. Open the Terminal tab, or enable 'Run agent login in terminal' on this session, or set an API key path in Settings → Products.",
        OutputSignal.FatalError =>
            "The Cursor worker refused to start. On Windows this is often a better-sqlite3 ABI mismatch — check that auto-patch is enabled in Settings → Products.",
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
