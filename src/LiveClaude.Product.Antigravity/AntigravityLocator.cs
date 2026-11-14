using LiveClaude.Abstractions;

namespace LiveClaude.Product.Antigravity;

public sealed record AntigravityInstall(string Path, string? Version, string Source);

/// <summary>Finds the Antigravity <c>agy</c> CLI on PATH and in the usual install folders.</summary>
public static class AntigravityLocator
{
    public static string FallbackExecutableName => OperatingSystem.IsWindows() ? "agy.exe" : "agy";

    public static AntigravityInstall? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && IsExecutable(configuredPath))
            return new AntigravityInstall(configuredPath, ReadVersion(configuredPath), "configured");

        foreach (var candidate in EnumerateCandidates())
        {
            if (IsExecutable(candidate.Path))
                return new AntigravityInstall(candidate.Path, ReadVersion(candidate.Path), candidate.Source);
        }

        return null;
    }

    public static IReadOnlyList<AntigravityInstall> FindAll(string? configuredPath = null)
    {
        var result = new List<AntigravityInstall>();
        var seen = new HashSet<string>(PathComparer);

        if (!string.IsNullOrWhiteSpace(configuredPath) && IsExecutable(configuredPath) && seen.Add(configuredPath))
            result.Add(new AntigravityInstall(configuredPath, ReadVersion(configuredPath), "configured"));

        foreach (var candidate in EnumerateCandidates())
        {
            if (IsExecutable(candidate.Path) && seen.Add(candidate.Path))
                result.Add(new AntigravityInstall(candidate.Path, ReadVersion(candidate.Path), candidate.Source));
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
            var candidate = Path.Combine(local, "agy", "bin", "agy.exe");
            if (File.Exists(candidate))
                yield return (candidate, "LocalAppData\\agy");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var relative in new[]
                     {
                         Path.Combine(".local", "bin", "agy"),
                         Path.Combine(".agy", "bin", "agy"),
                         Path.Combine(".antigravity", "bin", "agy")
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

    private static IEnumerable<string> EnumeratePathEntries()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var parts = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var names = OperatingSystem.IsWindows()
            ? new[] { "agy.exe", "agy.cmd", "agy" }
            : new[] { "agy" };

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
