using System.Diagnostics;

namespace LiveClaude.Core.Claude;

public sealed record ClaudeInstall(string Path, string? Version, string Source);

/// <summary>
/// Finds the Claude Code CLI. Prefers a stable native install over the copy bundled with the
/// desktop app, whose path carries the version number and therefore moves on every update.
/// </summary>
public static class ClaudeLocator
{
    public static ClaudeInstall? Locate(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return new ClaudeInstall(configuredPath, ReadVersion(configuredPath), "configured");

        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate.Path))
                return candidate with { Version = ReadVersion(candidate.Path) };
        }

        return null;
    }

    public static IReadOnlyList<ClaudeInstall> FindAll(string? configuredPath = null)
    {
        var result = new List<ClaudeInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) && seen.Add(configuredPath))
            result.Add(new ClaudeInstall(configuredPath, ReadVersion(configuredPath), "configured"));

        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate.Path) && seen.Add(candidate.Path))
                result.Add(candidate with { Version = ReadVersion(candidate.Path) });
        }

        return result;
    }

    private static IEnumerable<ClaudeInstall> EnumerateCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return new ClaudeInstall(Path.Combine(local, "Programs", "claude", "claude.exe"), null, "native install");
        yield return new ClaudeInstall(Path.Combine(profile, ".local", "bin", "claude.exe"), null, "native install");

        foreach (var onPath in FromPathVariable())
            yield return new ClaudeInstall(onPath, null, "PATH");

        // Copy managed by the Claude desktop app: %APPDATA%\Claude\claude-code\<version>\claude.exe
        var bundledRoot = Path.Combine(roaming, "Claude", "claude-code");
        if (Directory.Exists(bundledRoot))
        {
            var newest = Directory.EnumerateDirectories(bundledRoot)
                .Select(dir => (dir, version: ParseVersion(Path.GetFileName(dir))))
                .Where(x => x.version is not null)
                .OrderByDescending(x => x.version)
                .Select(x => Path.Combine(x.dir, "claude.exe"))
                .FirstOrDefault();

            if (newest is not null)
                yield return new ClaudeInstall(newest, null, "desktop app (path changes on update)");
        }
    }

    private static IEnumerable<string> FromPathVariable()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir, "claude.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static Version? ParseVersion(string? text) => Version.TryParse(text, out var v) ? v : null;

    public static string? ReadVersion(string exePath)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exePath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc is null)
                return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output.Trim().Split('\n').FirstOrDefault()?.Trim();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
