using System.Text;
using System.Text.RegularExpressions;
using LiveClaude.Abstractions;

namespace LiveClaude.Product.Antigravity;

/// <summary>Interprets Antigravity <c>agy --remote-control</c> output for ready / auth / fatal signals.</summary>
public sealed class AntigravityOutputInterpreter : IOutputInterpreter
{
    private static readonly Regex AnsiPattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[PX^_][^\x1B]*\x1B\\)",
        RegexOptions.Compiled);

    private static readonly Regex UrlPattern = new(
        @"https://antigravity\.google(?:\.com)?/\S+",
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

        if (Contains(clean, "not authenticated") ||
            Contains(clean, "sign in") ||
            Contains(clean, "login required") ||
            Contains(clean, "Unauthorized") ||
            Contains(clean, "authentication failed") ||
            Contains(clean, "accounts.google.com"))
            return OutputSignal.LoginRequired;

        if (Contains(clean, "Do you trust") ||
            Contains(clean, "Trust this workspace"))
            return OutputSignal.TrustPrompt;

        if (Contains(clean, "unknown flag") ||
            Contains(clean, "unknown option") ||
            Contains(clean, "No such file") ||
            Contains(clean, "cannot combine"))
            return OutputSignal.FatalError;

        if (Contains(clean, "remote control") &&
            (Contains(clean, "connected") ||
             Contains(clean, "ready") ||
             Contains(clean, "active") ||
             Contains(clean, "listening") ||
             Contains(clean, "antigravity.google")))
            return OutputSignal.Ready;

        return OutputSignal.None;
    }

    public string Describe(OutputSignal signal) => signal switch
    {
        OutputSignal.LoginRequired =>
            "Antigravity CLI is not signed in. Open the Terminal tab or use Settings → Products → Sign in with Antigravity.",
        OutputSignal.TrustPrompt =>
            "Antigravity CLI is asking to trust this workspace. Open the Terminal tab to confirm.",
        OutputSignal.FatalError =>
            "Antigravity CLI refused to start due to an invalid flag or configuration.",
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
