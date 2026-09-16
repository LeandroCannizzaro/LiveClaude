using System.Diagnostics;
using System.Text.Json;
using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Windows;

/// <summary>
/// One machine-wide root under %ProgramData%, so a service running under a different account reads
/// the same configuration the desktop app writes.
/// </summary>
public sealed class WindowsPathLayout : IPathLayout
{
    public string RootDirectory => PathLayout.Override ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LiveClaude");

    public string ConfigFilePath => Path.Combine(RootDirectory, "config.json");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string StateDirectory => Path.Combine(RootDirectory, "state");

    /// <summary>A named pipe has no path, so nothing is ever created here.</summary>
    public string RuntimeDirectory => Path.Combine(RootDirectory, "run");

    public void EnsureDirectories() => PathLayout.EnsureDirectories(this);
}

/// <summary>
/// Finds the Claude Code CLI. Prefers a stable native install over the copy bundled with the
/// desktop app, whose path carries the version number and therefore moves on every update.
/// </summary>
public sealed class WindowsClaudeDiscovery : IClaudeDiscovery
{
    public string ExecutableName => "claude.exe";

    public string CredentialSourceDescription => @"%USERPROFILE%\.claude\.credentials.json";

    public bool IsExecutable(string path) => File.Exists(path);

    public IEnumerable<ClaudeCandidate> EnumerateCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return new ClaudeCandidate(Path.Combine(local, "Programs", "claude", "claude.exe"), "native install");
        yield return new ClaudeCandidate(Path.Combine(profile, ".local", "bin", "claude.exe"), "native install");

        foreach (var onPath in FromPathVariable())
            yield return new ClaudeCandidate(onPath, "PATH");

        // Copy managed by the Claude desktop app: %APPDATA%\Claude\claude-code\<version>\claude.exe
        var bundledRoot = Path.Combine(roaming, "Claude", "claude-code");
        if (!Directory.Exists(bundledRoot))
            yield break;

        var newest = Directory.EnumerateDirectories(bundledRoot)
            .Select(dir => (dir, version: ParseVersion(Path.GetFileName(dir))))
            .Where(x => x.version is not null)
            .OrderByDescending(x => x.version)
            .Select(x => Path.Combine(x.dir, "claude.exe"))
            .FirstOrDefault();

        if (newest is not null)
            yield return new ClaudeCandidate(newest, "desktop app (path changes on update)");
    }

    public ClaudeCredentials? LoadCredentials() => ClaudeCredentialsFile.Read();

    public string? ReadVersion(string exePath)
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
}

