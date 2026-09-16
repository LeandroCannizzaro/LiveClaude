using LiveClaude.Abstractions;

namespace LiveClaude.Core.Claude;

public sealed record ClaudeInstall(string Path, string? Version, string Source);

/// <summary>
/// Finds the Claude Code CLI. The candidate list and the "can this actually be executed?" test come
/// from the platform: Windows looks for <c>claude.exe</c> in the places its installers use, POSIX for
/// an executable <c>claude</c> that is usually a symlink to a script.
/// </summary>
public static class ClaudeLocator
{
    private static IClaudeDiscovery Discovery => PlatformLoader.Current.Claude;

    public static ClaudeInstall? Locate(string? configuredPath = null)
    {
        var discovery = Discovery;

        if (!string.IsNullOrWhiteSpace(configuredPath) && discovery.IsExecutable(configuredPath))
            return new ClaudeInstall(configuredPath, discovery.ReadVersion(configuredPath), "configured");

        foreach (var candidate in discovery.EnumerateCandidates())
        {
            if (discovery.IsExecutable(candidate.Path))
                return new ClaudeInstall(candidate.Path, discovery.ReadVersion(candidate.Path), candidate.Source);
        }

        return null;
    }

    public static IReadOnlyList<ClaudeInstall> FindAll(string? configuredPath = null)
    {
        var discovery = Discovery;
        var result = new List<ClaudeInstall>();
        var seen = new HashSet<string>(PathComparer);

        if (!string.IsNullOrWhiteSpace(configuredPath) && discovery.IsExecutable(configuredPath) && seen.Add(configuredPath))
            result.Add(new ClaudeInstall(configuredPath, discovery.ReadVersion(configuredPath), "configured"));

        foreach (var candidate in discovery.EnumerateCandidates())
        {
            if (discovery.IsExecutable(candidate.Path) && seen.Add(candidate.Path))
                result.Add(new ClaudeInstall(candidate.Path, discovery.ReadVersion(candidate.Path), candidate.Source));
        }

        return result;
    }

    /// <summary>What to run when nothing was found and nothing is configured.</summary>
    public static string FallbackExecutableName => Discovery.ExecutableName;

    public static string? ReadVersion(string exePath) => Discovery.ReadVersion(exePath);

    /// <summary>
    /// Paths are case-insensitive on Windows and macOS, case-sensitive on Linux. Deduplicating with
    /// the wrong one either merges two real installs or lists the same one twice.
    /// </summary>
    private static StringComparer PathComparer =>
        PlatformLoader.Current.Id == "linux" ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
