using LiveClaude.Abstractions;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Supervision;

/// <summary>
/// Reads <c>git worktree list --porcelain</c> for an instance's directory, best effort: no git on
/// PATH, the directory not being a repository, or any other failure all just mean "no worktrees".
/// </summary>
public static class GitWorktrees
{
    public static async Task<List<WorktreeInfo>> ListAsync(string directory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new();

        var result = await ProcessRunner
            .TryRunAsync("git", ["worktree", "list", "--porcelain"], directory, ct)
            .ConfigureAwait(false);

        return result.ExitCode == 0 ? Parse(result.StandardOutput) : new();
    }

    /// <summary>
    /// Parses the porcelain format: one blank-line-separated block per worktree, each a run of
    /// "key value" lines such as "worktree &lt;path&gt;", "branch refs/heads/&lt;name&gt;" or the
    /// valueless "detached".
    /// </summary>
    public static List<WorktreeInfo> Parse(string porcelain)
    {
        var worktrees = new List<WorktreeInfo>();
        string? path = null;
        string? branch = null;

        void Flush()
        {
            if (path is not null)
                worktrees.Add(new WorktreeInfo { Path = path, Branch = branch });

            path = null;
            branch = null;
        }

        foreach (var rawLine in porcelain.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..];
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                branch = line["branch refs/heads/".Length..];
        }

        Flush();
        return worktrees;
    }
}
