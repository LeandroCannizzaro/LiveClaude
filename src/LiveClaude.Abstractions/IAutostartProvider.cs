namespace LiveClaude.Abstractions;

/// <summary>Which executable and arguments to register with the operating system.</summary>
public sealed record SupervisorCommand(string ExecutablePath, string Arguments, string? Note = null);

/// <summary>Where the supervisor is registered.</summary>
public enum AutostartScope
{
    /// <summary>The signed-in user's session: scheduled task, systemd user unit, LaunchAgent.</summary>
    User,

    /// <summary>Machine-wide, before anyone signs in: Windows service, systemd system unit, LaunchDaemon.</summary>
    System
}

/// <summary>Knobs that mean something on more than one platform.</summary>
public sealed class AutostartOptions
{
    /// <summary>
    /// Start before anyone signs in. On Windows that is the boot trigger, which only an administrator
    /// may register; on Linux it needs <c>loginctl enable-linger</c>; on macOS a LaunchAgent cannot do
    /// it at all. Declining leaves a working logon-only registration everywhere.
    /// </summary>
    public bool StartBeforeSignIn { get; set; } = true;

    /// <summary>
    /// The account the supervisor runs as. Matters on Windows, where UAC may have been answered with
    /// a different administrator and the task must still belong to the person whose session the
    /// servers run in, and for a system unit or LaunchDaemon that must not run as root.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>Windows service accounts need a real password; never logged.</summary>
    public string? Password { get; set; }
}

/// <summary>
/// What could be learned about the registration.
///
/// <see cref="Installed"/> is deliberately three-valued. "I cannot tell" is a real answer on every
/// one of these systems — a scheduled task registered by another administrator is unreadable by a
/// standard account, <c>systemctl --user</c> fails without a session bus, <c>launchctl print</c>
/// refuses for another user's domain — and reporting that as "not installed" is what sent people
/// installing the same thing over and over.
/// </summary>
public sealed record AutostartState(bool? Installed, string? Status, string? RunAs, string? Detail = null)
{
    public string Describe() => Installed switch
    {
        true => Status ?? "installed",
        false => "not installed",
        _ => "installed, but this account cannot read it"
    };
}

public sealed record AutostartResult(bool Success, string Message)
{
    public static AutostartResult Ok(string message) => new(true, message);

    public static AutostartResult Failed(string message) => new(false, message);
}

/// <summary>
/// Registers the supervisor with the operating system's own service manager, so it comes back after
/// a crash, a logout and a reboot. One implementation per scope per platform.
/// </summary>
public interface IAutostartProvider
{
    /// <summary>"task", "service", "systemd-user", "systemd-system", "launch-agent", "launch-daemon".</summary>
    string Kind { get; }

    /// <summary>What to call it in the UI: "Scheduled task", "systemd user unit", "LaunchAgent".</summary>
    string DisplayName { get; }

    AutostartScope Scope { get; }

    /// <summary>One sentence on what this host does and when to prefer it.</summary>
    string Summary { get; }

    /// <summary>True when installing with these options needs administrator rights.</summary>
    bool RequiresElevation(AutostartOptions options);

    /// <summary>
    /// True when the registration can name the account to run as. A scheduled task, a Windows
    /// service, a systemd system unit and a LaunchDaemon all can; a systemd user unit and a
    /// LaunchAgent cannot — they are the session's own, and there is nothing to choose.
    /// </summary>
    bool SupportsAccount { get; }

    /// <summary>
    /// True when that account also needs a password. Only the Windows service does: Windows never
    /// lets a user account log on as a service with a blank one, and asking for it up front is what
    /// keeps the failure from arriving as error 1069 after an elevation prompt.
    /// </summary>
    bool RequiresPassword { get; }

    /// <summary>
    /// A condition the user must satisfy for <see cref="AutostartOptions.StartBeforeSignIn"/> to
    /// work, or null when there is none. On Linux this is <c>loginctl enable-linger</c>; a LaunchAgent
    /// simply cannot start before sign-in and says so.
    /// </summary>
    string? StartBeforeSignInRequirement { get; }

    Task<AutostartState> QueryAsync(CancellationToken ct = default);

    Task<AutostartResult> InstallAsync(SupervisorCommand command, AutostartOptions options, CancellationToken ct = default);

    Task<AutostartResult> UninstallAsync(CancellationToken ct = default);

    Task<AutostartResult> StartAsync(CancellationToken ct = default);

    Task<AutostartResult> StopAsync(CancellationToken ct = default);
}
