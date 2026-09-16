using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Linux.Autostart;

/// <summary>
/// A systemd user unit: the recommended host on Linux, and the counterpart of the Windows scheduled
/// task. It runs as the signed-in user, so Claude Code finds its credentials, and systemd restarts it
/// when it dies and at every login.
/// </summary>
public sealed class SystemdUserProvider : SystemdProvider
{
    private readonly IProcessLauncher _processes;
    private readonly IPrivilegeElevator _elevation;

    public SystemdUserProvider(IProcessLauncher processes, IPrivilegeElevator elevation) : base(processes)
    {
        _processes = processes;
        _elevation = elevation;
    }

    public override string Kind => "systemd-user";

    public override string DisplayName => "systemd user unit";

    public override AutostartScope Scope => AutostartScope.User;

    public override string Summary =>
        "Runs as your signed-in user, starts at login and restarts five seconds after it dies.";

    /// <summary>
    /// A user unit is the session's own; there is nothing to choose, and naming another account in it
    /// would be rejected by systemd.
    /// </summary>
    public override bool SupportsAccount => false;

    public override string UnitPath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configHome
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "systemd", "user", UnitName);

    protected override string[] ScopeArguments => ["--user"];

    protected override string WantedBy => "default.target";

    /// <summary>
    /// Writing the unit and enabling it needs no rights at all. Only lingering does — it is a change
    /// to the machine, not to this session — which is the exact shape of the Windows rule that only
    /// an administrator may register a task with a boot trigger.
    /// </summary>
    public override bool RequiresElevation(AutostartOptions options) =>
        options.StartBeforeSignIn && !_processes.IsElevated && !IsLingerEnabled();

    public override string? StartBeforeSignInRequirement =>
        "A user unit only runs while you have a session, so starting before you sign in needs lingering " +
        $"('loginctl enable-linger {Environment.UserName}'), which requires elevation. Decline it and the " +
        "unit is still installed: everything works except starting before anyone signs in.";

    /// <summary>
    /// Turns on lingering so the user manager — and with it the supervisor — runs from boot without
    /// anybody logging in.
    /// </summary>
    protected override async Task<string> ApplyStartBeforeSignInAsync(CancellationToken ct)
    {
        if (IsLingerEnabled())
            return "Lingering is already on, so the supervisor starts at boot.";

        var user = Environment.UserName;

        var direct = await _processes.RunAsync("loginctl", ["enable-linger", user], ct: ct).ConfigureAwait(false);
        if (direct.Success)
            return "Lingering enabled, so the supervisor starts at boot without anyone signing in.";

        // Not elevated: ask, the way installing a boot-triggered task asks on Windows.
        try
        {
            var exitCode = await _elevation.RunElevatedAsync("loginctl", ["enable-linger", user], ct).ConfigureAwait(false);

            if (exitCode == 0)
                return "Lingering enabled, so the supervisor starts at boot without anyone signing in.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // no way to elevate here
        }

        return $"Could not enable lingering, so the supervisor starts when you log in rather than at boot. " +
               $"Run 'sudo loginctl enable-linger {user}' to change that.";
    }

    /// <summary>
    /// Lingering shows up as a file named after the user. Reading the directory is cheaper than
    /// shelling out to loginctl and works the same when there is no session bus to talk to.
    /// </summary>
    private static bool IsLingerEnabled() =>
        File.Exists(Path.Combine("/var/lib/systemd/linger", Environment.UserName));

    protected override Task<string?> ReadRunAsAsync(CancellationToken ct) =>
        Task.FromResult<string?>(Environment.UserName);
}

/// <summary>
/// A systemd system unit: starts before anyone signs in, and the counterpart of the Windows service.
/// Like that one, it has to name the user's own account — a supervisor running as root has a
/// different home directory and will not find the Claude Code credentials.
/// </summary>
public sealed class SystemdSystemProvider : SystemdProvider
{
    public SystemdSystemProvider(IProcessLauncher processes) : base(processes)
    {
    }

    public override string Kind => "systemd-system";

    public override string DisplayName => "systemd system unit";

    public override AutostartScope Scope => AutostartScope.System;

    public override string Summary =>
        "Starts at boot, before anyone signs in. Name your own account: running as root means a " +
        "different home directory, where the Claude Code sign-in is not.";

    public override bool SupportsAccount => true;

    public override string UnitPath => Path.Combine("/etc/systemd/system", UnitName);

    protected override string[] ScopeArguments => [];

    protected override string WantedBy => "multi-user.target";

    public override bool RequiresElevation(AutostartOptions options) => true;

    public override string? StartBeforeSignInRequirement => null;

    /// <summary>
    /// A system unit runs outside any login session, so it has no D-Bus session, no keyring and no
    /// XDG_RUNTIME_DIR. The configuration root is pinned to a shared path for the same reason: the
    /// per-user default would point at root's home directory, not at the one the app writes to.
    /// </summary>
    protected override string BuildUnit(SupervisorCommand command, AutostartOptions options)
    {
        var unit = base.BuildUnit(command, options);

        if (!unit.Contains(PathLayout.RootOverrideVariable, StringComparison.Ordinal))
        {
            unit = unit.Replace(
                "Restart=always",
                $"Environment={PathLayout.RootOverrideVariable}=/var/lib/liveclaude\nRestart=always",
                StringComparison.Ordinal);
        }

        return unit;
    }
}
