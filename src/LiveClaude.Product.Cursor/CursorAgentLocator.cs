using LiveClaude.Abstractions;

namespace LiveClaude.Product.Cursor;

public sealed record CursorAgentInstall(string Path, string? Version, string Source);

/// <summary>Finds the Cursor <c>agent</c> CLI on PATH and in the usual install folders.</summary>
public static class CursorAgentLocator
{
    public static string FallbackExecutableName => "agent";

    public static CursorAgentInstall? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && IsExecutable(configuredPath))
            return new CursorAgentInstall(configuredPath, ReadVersion(configuredPath), "configured");

        foreach (var candidate in EnumerateCandidates())
        {
            if (IsExecutable(candidate.Path))
                return new CursorAgentInstall(candidate.Path, ReadVersion(candidate.Path), candidate.Source);
        }

        return null;
    }

    public static IReadOnlyList<CursorAgentInstall> FindAll(string? configuredPath = null)
    {
        var result = new List<CursorAgentInstall>();
        var seen = new HashSet<string>(PathComparer);

        if (!string.IsNullOrWhiteSpace(configuredPath) && IsExecutable(configuredPath) && seen.Add(configuredPath))
            result.Add(new CursorAgentInstall(configuredPath, ReadVersion(configuredPath), "configured"));

        foreach (var candidate in EnumerateCandidates())
        {
            if (IsExecutable(candidate.Path) && seen.Add(candidate.Path))
                result.Add(new CursorAgentInstall(candidate.Path, ReadVersion(candidate.Path), candidate.Source));
        }

        return result;
    }

    public static IEnumerable<(string Path, string Source)> EnumerateCandidates()
    {
        foreach (var path in EnumeratePathEntries())
            yield return (path, "PATH");

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var versionsRoot = Path.Combine(local, "cursor-agent", "versions");
            if (Directory.Exists(versionsRoot))
            {
                foreach (var versionDir in Directory.EnumerateDirectories(versionsRoot)
                             .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var name in new[] { "agent.cmd", "agent.exe", "agent" })
                    {
                        var candidate = Path.Combine(versionDir, name);
                        if (File.Exists(candidate))
                            yield return (candidate, "LocalAppData\\cursor-agent");
                    }
                }
            }

            var shim = Path.Combine(local, "cursor-agent", "agent.cmd");
            if (File.Exists(shim))
                yield return (shim, "LocalAppData\\cursor-agent");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var relative in new[]
                     {
                         Path.Combine(".local", "bin", "agent"),
                         Path.Combine(".cursor", "bin", "agent"),
                         Path.Combine(".cursor-agent", "agent")
                     })
            {
                var path = Path.Combine(home, relative);
                if (File.Exists(path))
                    yield return (path, "home");
            }
        }
    }

    public static bool IsExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            var mode = File.GetUnixFileMode(path);
            return mode.HasFlag(UnixFileMode.UserExecute)
                   || mode.HasFlag(UnixFileMode.GroupExecute)
                   || mode.HasFlag(UnixFileMode.OtherExecute);
        }
        catch
        {
            return true;
        }
    }

    public static string? ReadVersion(string executablePath)
    {
        try
        {
            var result = PlatformLoader.Current.Processes
                .RunAsync(executablePath, ["--version"])
                .GetAwaiter().GetResult();
            if (result.ExitCode != 0)
                return null;

            return (result.StandardOutput + "\n" + result.StandardError)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Newest <c>%LOCALAPPDATA%\cursor-agent\versions\*</c> directory, or null when missing.
    /// Used by the Windows better-sqlite3 patcher.
    /// </summary>
    public static string? LatestWindowsVersionsDirectory()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var versionsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "cursor-agent",
            "versions");

        if (!Directory.Exists(versionsRoot))
            return null;

        return Directory.EnumerateDirectories(versionsRoot)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumeratePathEntries()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = OperatingSystem.IsWindows()
            ? new[] { "agent.cmd", "agent.exe", "agent.ps1", "agent" }
            : new[] { "agent" };

        foreach (var dir in parts)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
}
