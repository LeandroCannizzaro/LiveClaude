namespace LiveClaude.Abstractions;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string Combined => string.Join(Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public interface IProcessLauncher
{
    /// <summary>Administrator on Windows, <c>euid == 0</c> on POSIX.</summary>
    bool IsElevated { get; }

    /// <summary>DOMAIN\user on Windows, the login name elsewhere.</summary>
    string CurrentUserName { get; }

    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default);

    /// <summary>
    /// Renders a command for a human to read — the session editor's command preview, and the
    /// "starting: …" line in the instance log. Windows quotes the way CommandLineToArgvW parses;
    /// POSIX quotes the way a shell would. Never used to actually launch anything: that takes an
    /// argument vector.
    /// </summary>
    string FormatCommandLine(string executable, IEnumerable<string> arguments);
}

/// <summary>Runs a command with administrator rights: UAC, pkexec, or osascript.</summary>
public interface IPrivilegeElevator
{
    /// <summary>False when nothing on this machine can prompt for rights (no polkit agent, say).</summary>
    bool IsAvailable { get; }

    /// <summary>"UAC", "pkexec", "sudo" or "osascript" — named in the UI so the prompt is expected.</summary>
    string Mechanism { get; }

    /// <summary>Runs the command elevated and waits for it. Returns its exit code.</summary>
    Task<int> RunElevatedAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default);
}
