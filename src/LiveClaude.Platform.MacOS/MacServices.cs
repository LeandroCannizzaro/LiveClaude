using System.Diagnostics;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix;

namespace LiveClaude.Platform.MacOS;

/// <summary>Apple's layout: application support for data, a separate well-known place for logs.</summary>
public sealed class MacPathLayout : IPathLayout
{
    public string RootDirectory => PathLayout.Override ?? Path.Combine(
        Home, "Library", "Application Support", "LiveClaude");

    public string ConfigFilePath => Path.Combine(RootDirectory, "config.json");

    /// <summary>
    /// ~/Library/Logs is where Console.app looks, so the instance logs show up there without anyone
    /// being told where to find them. A daemon with an overridden root keeps everything together.
    /// </summary>
    public string LogDirectory => PathLayout.Override is { } root
        ? Path.Combine(root, "logs")
        : Path.Combine(Home, "Library", "Logs", "LiveClaude");

    public string StateDirectory => Path.Combine(RootDirectory, "state");

    /// <summary>
    /// Deliberately not under Application Support, unlike everything else here.
    ///
    /// A Unix domain socket path may be at most 104 characters on macOS, and
    /// <c>/Users/&lt;name&gt;/Library/Application Support/LiveClaude/run/</c> spends more than half of
    /// that before the file name. It fit for a short user name and broke for a long one — the kind of
    /// bug that reaches a user rather than a test. A short per-user directory under /tmp, created
    /// 0700, is what tmux does and what this does.
    /// </summary>
    public string RuntimeDirectory => PathLayout.Override is { } root
        ? Path.Combine(root, "run")
        : PosixRuntimeDirectory.Shared;

    public void EnsureDirectories() => PathLayout.EnsureDirectories(this);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

/// <summary>
/// Elevation on macOS, which has no pkexec.
///
/// <c>osascript ... with administrator privileges</c> shows the standard authentication sheet and
/// runs the command through the privileged helper — the same prompt any Mac application uses.
/// </summary>
public sealed class MacElevator : IPrivilegeElevator
{
    public bool IsAvailable => true;

    public string Mechanism => "osascript";

    public async Task<int> RunElevatedAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        // Two levels of quoting: the shell command osascript runs, and the AppleScript string that
        // carries it. Getting this wrong is how a path with a space silently runs the wrong thing.
        var command = PosixProcessLauncher.Quote(fileName) + " " +
                      string.Join(' ', arguments.Select(PosixProcessLauncher.Quote));

        var script = $"do shell script \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" with administrator privileges";

        using var process = Process.Start(new ProcessStartInfo("osascript")
        {
            ArgumentList = { "-e", script },
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start osascript.");

        var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        // -128 is "the user cancelled", which is a decision rather than a failure; the callers treat
        // any non-zero code as "declined" and fall back, so it needs no special case beyond saying so.
        if (process.ExitCode != 0 && error.Contains("-128", StringComparison.Ordinal))
            return -128;

        return process.ExitCode;
    }
}

/// <summary>
/// Finding Claude Code on a Mac.
///
/// Two things differ from Linux: Homebrew's prefix depends on the architecture (/opt/homebrew on
/// Apple silicon, /usr/local on Intel), and the CLI stores its OAuth token in the login Keychain
/// rather than in ~/.claude/.credentials.json. Reading only the file would leave the Environments tab
/// reporting "not signed in" for a user who is perfectly signed in.
/// </summary>
public sealed class MacClaudeDiscovery : PosixClaudeDiscovery
{
    private const string KeychainService = "Claude Code-credentials";

    public override string CredentialSourceDescription =>
        $"the login Keychain ('{KeychainService}'), or ~/.claude/.credentials.json";

    public override IEnumerable<ClaudeCandidate> EnumerateCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return new ClaudeCandidate(Path.Combine(home, ".local", "bin", "claude"), "native install");
        yield return new ClaudeCandidate(Path.Combine(home, ".claude", "local", "claude"), "local install");

        foreach (var onPath in FromPathVariable())
            yield return new ClaudeCandidate(onPath, "PATH");

        yield return new ClaudeCandidate("/opt/homebrew/bin/claude", "Homebrew (Apple silicon)");
        yield return new ClaudeCandidate("/usr/local/bin/claude", "Homebrew (Intel)");
    }

    public override ClaudeCredentials? LoadCredentials() => ReadFromKeychain() ?? ClaudeCredentialsFile.Read();

    /// <summary>
    /// Asks the Keychain for the item the CLI stores. The first call may show an "allow access"
    /// prompt, which is the system behaving correctly; a refusal just means no credentials here.
    /// </summary>
    private static ClaudeCredentials? ReadFromKeychain()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("security")
            {
                ArgumentList = { "find-generic-password", "-s", KeychainService, "-w" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return null;

            var json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
                return null;

            // The stored secret is the same JSON document the file holds on other platforms.
            var path = Path.Combine(Path.GetTempPath(), $"liveclaude-keychain-{Guid.NewGuid():n}.json");

            try
            {
                File.WriteAllText(path, json);
                return ClaudeCredentialsFile.Read(path);
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

public sealed class MacShellIntegration : IShellIntegration
{
    public void Reveal(string path)
    {
        var startInfo = new ProcessStartInfo("open")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -R selects the item in Finder rather than opening it, which for a log file is the
        // difference between showing it and launching whichever editor claims .log.
        if (File.Exists(path))
            startInfo.ArgumentList.Add("-R");

        startInfo.ArgumentList.Add(path);

        Process.Start(startInfo);
    }

    public void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo("open")
        {
            ArgumentList = { url },
            UseShellExecute = false,
            CreateNoWindow = true
        });
}
