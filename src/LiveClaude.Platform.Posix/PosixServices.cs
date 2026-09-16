using System.Diagnostics;
using System.Text;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix.Native;

namespace LiveClaude.Platform.Posix;

public sealed class PosixProcessLauncher : IProcessLauncher
{
    /// <summary>Root, which is what sudo, pkexec and osascript all produce.</summary>
    public bool IsElevated => Libc.geteuid() == 0;

    public string CurrentUserName => Environment.UserName;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default) =>
        ProcessRunner.TryRunAsync(fileName, arguments, workingDirectory, ct);

    /// <summary>
    /// Renders a command the way a POSIX shell would accept it back. Nothing is ever launched through
    /// a shell — the argument vector goes straight to the child — but the session editor shows this
    /// so somebody can paste it into their own terminal, and it has to survive a path with a space.
    /// </summary>
    public string FormatCommandLine(string executable, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder(Quote(executable));

        foreach (var argument in arguments)
            builder.Append(' ').Append(Quote(argument));

        return builder.ToString();
    }

    /// <summary>
    /// Single quotes, because inside them a shell interprets nothing at all. A single quote in the
    /// value has to end the quoting, contribute an escaped quote, and start it again — the
    /// <c>'\''</c> dance every shell script ends up writing.
    /// </summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '=' or '@' or '+' or ','))
            return value;

        return "'" + value.Replace("'", "'\\''") + "'";
    }
}

/// <summary>
/// Running something with administrator rights on a desktop Unix.
///
/// <c>pkexec</c> is the one that shows a graphical prompt, which is what the app needs; <c>sudo</c> is
/// the fallback for a machine with no polkit agent, and only works when the caller already has a
/// terminal and a cached credential. macOS overrides this with osascript.
/// </summary>
public class PosixElevator : IPrivilegeElevator
{
    public virtual bool IsAvailable => ProcessRunner.Exists("pkexec") || ProcessRunner.Exists("sudo");

    public virtual string Mechanism => ProcessRunner.Exists("pkexec") ? "pkexec" : "sudo";

    public virtual async Task<int> RunElevatedAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var elevator = ProcessRunner.Exists("pkexec") ? "pkexec" : "sudo";

        var psi = new ProcessStartInfo(elevator)
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // pkexec starts the program with a minimal environment on purpose; passing the variables the
        // supervisor actually needs keeps a root-scoped install pointed at the same configuration.
        if (elevator == "pkexec")
        {
            psi.ArgumentList.Add("--disable-internal-agent");

            if (Environment.GetEnvironmentVariable(PathLayout.RootOverrideVariable) is { Length: > 0 } root)
            {
                psi.ArgumentList.Add("env");
                psi.ArgumentList.Add($"{PathLayout.RootOverrideVariable}={root}");
            }
        }
        else
        {
            // Without this sudo tries to read a password from a terminal the app does not have, and
            // hangs until it times out.
            psi.ArgumentList.Add("-n");
        }

        psi.ArgumentList.Add(fileName);

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{elevator}'.");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }
}

/// <summary>
/// Finds the Claude Code CLI on a Unix system.
///
/// The executable has no extension and is usually a symlink to a script, so "does the file exist?" is
/// not the right question — <c>~/.local/bin/claude</c> is very often a dangling symlink left behind by
/// an uninstall. Only an executability check catches that.
/// </summary>
public class PosixClaudeDiscovery : IClaudeDiscovery
{
    public string ExecutableName => "claude";

    public virtual string CredentialSourceDescription => "~/.claude/.credentials.json";

    public bool IsExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // access() follows symlinks and answers the real question: could this process exec it?
        return Libc.access(path, Libc.X_OK) == 0 && File.Exists(path);
    }

    public virtual IEnumerable<ClaudeCandidate> EnumerateCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return new ClaudeCandidate(Path.Combine(home, ".local", "bin", "claude"), "native install");
        yield return new ClaudeCandidate(Path.Combine(home, ".claude", "local", "claude"), "local install");

        foreach (var onPath in FromPathVariable())
            yield return new ClaudeCandidate(onPath, "PATH");

        yield return new ClaudeCandidate("/usr/local/bin/claude", "system install");
        yield return new ClaudeCandidate("/usr/bin/claude", "system install");
    }

    public virtual ClaudeCredentials? LoadCredentials() => ClaudeCredentialsFile.Read();

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

    protected static IEnumerable<string> FromPathVariable()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir, "claude");
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidate;
        }
    }
}

/// <summary>
/// Nothing to deploy.
///
/// The Windows implementation exists because ClickOnce and winget install into folders that are
/// replaced on every update, which would leave a registration pointing at an executable that no
/// longer exists. A tar.gz under /opt, a .deb, a Homebrew cask and an .app bundle all keep the same
/// path across updates, so the supervisor is registered where it already is.
/// </summary>
public class PosixSupervisorDeployment : ISupervisorDeployment
{
    public string SupervisorExecutable => "LiveClaude.Service";

    public string AppExecutable => "LiveClaude";

    /// <summary>Empty: there is no second copy to keep.</summary>
    public string StableDirectory => "";

    public bool IsVolatileLocation(string directory) => false;

    public virtual DeploymentResult Deploy(string? sourceDirectory = null)
    {
        var directory = (sourceDirectory ?? AppContext.BaseDirectory).TrimEnd('/');
        var executable = Path.Combine(directory, SupervisorExecutable);

        return new DeploymentResult(executable, [], ReadVersion(executable));
    }

    public IReadOnlyList<string> StopProcessesIn(string directory) => [];

    public string? ReadVersion(string executablePath)
    {
        try
        {
            return File.Exists(executablePath)
                ? FileVersionInfo.GetVersionInfo(executablePath).ProductVersion
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public virtual string? DescribeDeployment(string supervisorPath) => null;

    public virtual SupervisorCommand ResolveCommand(string directory, AutostartScope scope)
    {
        var argument = scope == AutostartScope.System ? "--service" : "--supervise";
        var supervisor = Path.Combine(directory, SupervisorExecutable);

        if (File.Exists(supervisor))
            return new SupervisorCommand(supervisor, argument);

        // A single-file publish has no separate supervisor executable; the app hosts it instead.
        var app = Path.Combine(directory, AppExecutable);
        if (File.Exists(app))
        {
            return new SupervisorCommand(app, argument,
                "This build does not ship a separate supervisor executable, so LiveClaude hosts it itself.");
        }

        return new SupervisorCommand(supervisor, argument);
    }
}

/// <summary>
/// The few pieces of process identity the per-OS assemblies need. The libc bindings themselves stay
/// internal — they are an implementation detail with platform-dependent constants — but the uid is
/// part of how launchd names a domain (<c>gui/&lt;uid&gt;</c>), so it has to be reachable.
/// </summary>
public static class PosixUser
{
    public static uint EffectiveUserId => Native.Libc.geteuid();

    public static bool IsRoot => EffectiveUserId == 0;
}
