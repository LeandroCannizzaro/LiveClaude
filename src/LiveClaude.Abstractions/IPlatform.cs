namespace LiveClaude.Abstractions;

/// <summary>
/// Everything LiveClaude needs from the operating system, in one place.
///
/// The implementations live in <c>LiveClaude.Platform.Windows</c>, <c>.Linux</c> and <c>.MacOS</c>,
/// which nothing references at compile time: <see cref="PlatformLoader"/> finds the one matching the
/// running OS and activates it. That is what keeps <c>LiveClaude.Core</c> on plain <c>net10.0</c>
/// with no OS conditionals, and lets a Linux package ship without a single Windows-only binary.
/// </summary>
public interface IPlatform
{
    /// <summary>"windows", "linux" or "macos" — also the suffix of the assembly name.</summary>
    string Id { get; }

    /// <summary>Shown in the dashboard, e.g. "Windows 11 (26100)".</summary>
    string DisplayName { get; }

    IPtyFactory Pty { get; }

    IPathLayout Paths { get; }

    /// <summary>The endpoint the supervisor serves and the app connects to.</summary>
    IIpcEndpointFactory Ipc { get; }

    /// <summary>
    /// An endpoint under a different name. Tests need one so they do not collide with the supervisor
    /// actually running on the developer's machine — which is not a hypothetical, since LiveClaude is
    /// the kind of program you run while working on it.
    /// </summary>
    IIpcEndpointFactory CreateIpcEndpoint(string name);

    IProcessLauncher Processes { get; }

    IPrivilegeElevator Elevation { get; }

    /// <summary>
    /// Autostart in the signed-in user's session: a scheduled task, a systemd user unit, or a
    /// LaunchAgent. This is the recommended host everywhere, because Claude Code's credentials live
    /// in the user's profile.
    /// </summary>
    IAutostartProvider UserAutostart { get; }

    /// <summary>
    /// Autostart before anyone signs in: a Windows service, a systemd system unit, or a LaunchDaemon.
    /// Null when the platform has no such concept.
    /// </summary>
    IAutostartProvider? SystemAutostart { get; }

    IClaudeDiscovery Claude { get; }

    IShellIntegration Shell { get; }

    ISupervisorDeployment Deployment { get; }

    /// <summary>
    /// Borrows the calling terminal so a one-shot CLI command can print into it.
    ///
    /// Only Windows needs this, and only because the supervisor is a windowed executable there: a
    /// process that owns a console hands it to every child and the pseudo console is then ignored,
    /// so the supervisor cannot simply be a console application. It attaches to the caller's console
    /// for install/status and nothing else. On POSIX a process inherits its terminal normally and
    /// this does nothing.
    /// </summary>
    void AttachToParentConsole();
}
