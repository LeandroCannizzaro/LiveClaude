using LiveClaude.Abstractions;
using LiveClaude.Core.Model;

namespace LiveClaude.Product.Antigravity;

/// <summary>Builds <c>agy --remote-control</c> argv from a session entry.</summary>
public static class AntigravityArgs
{
    public static IReadOnlyList<string> BuildRemoteControl(SessionConfig session)
    {
        var options = session.Antigravity;
        var args = new List<string> { "--remote-control" };

        if (!string.IsNullOrWhiteSpace(options.Mode))
        {
            args.Add("--mode");
            args.Add(options.Mode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            args.Add("--model");
            args.Add(options.Model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options.Effort))
        {
            args.Add("--effort");
            args.Add(options.Effort.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options.Agent))
        {
            args.Add("--agent");
            args.Add(options.Agent.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options.Project))
        {
            args.Add("--project");
            args.Add(options.Project.Trim());
        }

        foreach (var dir in options.ExtraDirs
                     .Where(d => !string.IsNullOrWhiteSpace(d))
                     .Select(d => d.Trim()))
        {
            args.Add("--add-dir");
            args.Add(dir);
        }

        if (options.Sandbox)
            args.Add("--sandbox");

        if (options.DangerouslySkipPermissions)
            args.Add("--dangerously-skip-permissions");

        if (!string.IsNullOrWhiteSpace(options.ConversationId))
        {
            args.Add("--conversation");
            args.Add(options.ConversationId.Trim());
        }
        else if (options.ContinuePrevious)
        {
            args.Add("--continue");
        }

        if (!string.IsNullOrWhiteSpace(session.ExtraArgs))
            args.AddRange(CliArguments.Split(session.ExtraArgs));

        return args;
    }

    /// <summary>Launches interactive <c>agy</c> in the embedded terminal for OAuth sign-in.</summary>
    public static IReadOnlyList<string> BuildLogin() => [];
}
