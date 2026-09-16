namespace LiveClaude.Abstractions;

/// <summary>
/// Where LiveClaude keeps its configuration, logs, state and runtime socket.
///
/// Windows uses one machine-wide root under %ProgramData% so a service running as another account
/// still reads the same configuration. POSIX has no real equivalent that both a daemon and the
/// user's app can reach, so Linux and macOS are per-user by default; a system-scope daemon opts into
/// a shared root through <see cref="RootOverrideVariable"/>.
/// </summary>
public interface IPathLayout
{
    string RootDirectory { get; }

    string ConfigFilePath { get; }

    string LogDirectory { get; }

    string StateDirectory { get; }

    /// <summary>
    /// Where the IPC endpoint lives. On Windows this is unused (a named pipe has no path); on POSIX
    /// it is a directory created with mode 0700, because a socket anyone can open is a socket anyone
    /// can use to drive the supervisor.
    /// </summary>
    string RuntimeDirectory { get; }

    void EnsureDirectories();
}

/// <summary>Shared helpers for the platform implementations of <see cref="IPathLayout"/>.</summary>
public static class PathLayout
{
    /// <summary>
    /// Overrides <see cref="IPathLayout.RootDirectory"/> on every platform. A systemd system unit or
    /// a LaunchDaemon sets it to a shared location such as /var/lib/liveclaude; without it a daemon
    /// would read a different configuration from the one the user edits.
    /// </summary>
    public const string RootOverrideVariable = "LIVECLAUDE_ROOT";

    public static string? Override
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>Creates the directories and returns the root, so implementations stay one-liners.</summary>
    public static void EnsureDirectories(IPathLayout layout)
    {
        Directory.CreateDirectory(layout.RootDirectory);
        Directory.CreateDirectory(layout.LogDirectory);
        Directory.CreateDirectory(layout.StateDirectory);
        Directory.CreateDirectory(layout.RuntimeDirectory);
    }
}
