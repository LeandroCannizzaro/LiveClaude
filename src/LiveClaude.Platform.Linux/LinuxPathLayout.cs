using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix;

namespace LiveClaude.Platform.Linux;

/// <summary>
/// XDG locations, per user.
///
/// Windows keeps one machine-wide root under %ProgramData% so a service running as another account
/// still reads the configuration the app writes. Linux has no equivalent a daemon and a desktop user
/// can both reach, so this is per-user by default. A systemd system unit opts into a shared root by
/// setting LIVECLAUDE_ROOT — the unit file written by the system provider does exactly that.
/// </summary>
public sealed class LinuxPathLayout : IPathLayout
{
    public string RootDirectory => PathLayout.Override ?? Path.Combine(ConfigHome, "liveclaude");

    public string ConfigFilePath => Path.Combine(RootDirectory, "config.json");

    /// <summary>
    /// Logs and state follow the root when it is overridden — a daemon writing its logs into the
    /// invoking user's home directory would be both surprising and, under a system unit, impossible.
    /// </summary>
    public string LogDirectory => PathLayout.Override is { } root
        ? Path.Combine(root, "logs")
        : Path.Combine(StateHome, "liveclaude", "logs");

    public string StateDirectory => PathLayout.Override is { } root
        ? Path.Combine(root, "state")
        : Path.Combine(StateHome, "liveclaude", "state");

    /// <summary>
    /// $XDG_RUNTIME_DIR is a tmpfs the login session owns, already mode 0700 and cleared at logout —
    /// the right home for a socket that must not be reachable by other users, and short
    /// (/run/user/1000/liveclaude), which matters because a socket path may be at most ~104
    /// characters. It is unset under a system unit and in some ssh sessions; the fallback is the same
    /// short per-user directory macOS uses, not the state directory, which can be arbitrarily long.
    /// </summary>
    public string RuntimeDirectory
    {
        get
        {
            if (PathLayout.Override is { } root)
                return Path.Combine(root, "run");

            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            return string.IsNullOrWhiteSpace(runtime)
                ? PosixRuntimeDirectory.Shared
                : Path.Combine(runtime, "liveclaude");
        }
    }

    public void EnsureDirectories() => PathLayout.EnsureDirectories(this);

    private static string ConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Home, ".config");

    private static string StateHome =>
        Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Home, ".local", "state");

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
