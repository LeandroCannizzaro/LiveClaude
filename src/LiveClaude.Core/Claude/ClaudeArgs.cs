using System.Text;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Claude;

/// <summary>Builds the command line for <c>claude remote-control</c> from a session entry.</summary>
public static class ClaudeArgs
{
    /// <summary>
    /// Window in which <c>claude remote-control --continue</c> can still reattach to the sessions the
    /// previous server was serving. Past that the CLI refuses and a fresh session is created instead.
    /// </summary>
    public static readonly TimeSpan ReattachWindow = TimeSpan.FromHours(4);

    /// <summary>
    /// Builds the argument list. <paramref name="lastStopUtc"/> is when this instance's previous
    /// server process exited; pass null on a cold start so no reattach flag is used.
    /// </summary>
    public static IReadOnlyList<string> BuildRemoteControl(SessionConfig s, DateTimeOffset? lastStopUtc = null, DateTimeOffset? nowUtc = null)
    {
        var args = new List<string> { "remote-control" };

        if (!string.IsNullOrWhiteSpace(s.SessionId))
        {
            // --session-id cannot be combined with --continue or any spawn flag.
            args.Add("--session-id");
            args.Add(s.SessionId.Trim());
        }
        else if (CanReattach(s, lastStopUtc, nowUtc))
        {
            args.Add("--continue");
        }
        else
        {
            args.Add("--name");
            args.Add(s.Name);
            args.Add("--spawn");
            args.Add(s.Spawn switch
            {
                SpawnMode.Worktree => "worktree",
                SpawnMode.Session => "session",
                _ => "same-dir"
            });

            if (s.Spawn != SpawnMode.Session)
            {
                args.Add("--capacity");
                args.Add(s.Capacity.ToString());
            }

            args.Add(s.CreateSessionInDir ? "--create-session-in-dir" : "--no-create-session-in-dir");

            if (!string.IsNullOrWhiteSpace(s.SessionNamePrefix))
            {
                args.Add("--remote-control-session-name-prefix");
                args.Add(s.SessionNamePrefix.Trim());
            }
        }

        if (s.PermissionMode != PermissionMode.Default)
        {
            args.Add("--permission-mode");
            args.Add(s.PermissionMode switch
            {
                PermissionMode.AcceptEdits => "acceptEdits",
                PermissionMode.Auto => "auto",
                PermissionMode.BypassPermissions => "bypassPermissions",
                PermissionMode.DontAsk => "dontAsk",
                PermissionMode.Plan => "plan",
                _ => "default"
            });
        }

        if (s.Sandbox)
            args.Add("--sandbox");

        if (s.Verbose)
            args.Add("--verbose");

        if (!string.IsNullOrWhiteSpace(s.ExtraArgs))
            args.AddRange(SplitArguments(s.ExtraArgs));

        return args;
    }

    /// <summary>True when the previous server stopped recently enough for <c>--continue</c> to work.</summary>
    public static bool CanReattach(SessionConfig s, DateTimeOffset? lastStopUtc, DateTimeOffset? nowUtc = null)
    {
        if (!s.ContinuePrevious || lastStopUtc is null)
            return false;

        // --continue is rejected together with --no-create-session-in-dir servers, which archive
        // their sessions on shutdown, so there is nothing to bring back.
        if (!s.CreateSessionInDir)
            return false;

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        return now - lastStopUtc.Value < ReattachWindow;
    }

    /// <summary>Splits a raw extra-arguments string, honouring double quotes.</summary>
    public static IReadOnlyList<string> SplitArguments(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
