using LiveClaude.Abstractions;
using LiveClaude.Platform.Posix;

namespace LiveClaude.Platform.MacOS.Autostart;

/// <summary>
/// A LaunchAgent: the recommended host on macOS, and the counterpart of the Windows scheduled task.
/// It runs in the user's GUI session, so Claude Code finds its credentials and the Keychain is
/// unlocked, and launchd brings it back whenever it dies.
/// </summary>
public sealed class LaunchAgentProvider : LaunchdProvider
{
    public LaunchAgentProvider(IProcessLauncher processes) : base(processes)
    {
    }

    public override string Kind => "launch-agent";

    public override string DisplayName => "LaunchAgent";

    public override AutostartScope Scope => AutostartScope.User;

    public override string Summary =>
        "Runs in your GUI session, starts when you log in and restarts five seconds after it dies.";

    /// <summary>An agent is the session's own; there is no account to choose.</summary>
    public override bool SupportsAccount => false;

    public override string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label}.plist");

    protected override string Domain => $"gui/{Uid}";

    /// <summary>Writing into your own LaunchAgents folder needs nothing.</summary>
    public override bool RequiresElevation(AutostartOptions options) => false;

    /// <summary>
    /// The honest answer, and the reason the macOS story differs from the other two: a LaunchAgent
    /// cannot start before sign-in at all. There is no lingering to enable and no boot trigger to
    /// register — the GUI session is the thing it belongs to.
    /// </summary>
    public override string? StartBeforeSignInRequirement =>
        "A LaunchAgent belongs to your GUI session, so it starts when you log in — never before. " +
        "Install the LaunchDaemon instead if the servers have to be up before anyone signs in; " +
        "it needs an administrator, and the Keychain will not be unlocked for it.";

    private static uint Uid => PosixUser.EffectiveUserId;
}

/// <summary>
/// A LaunchDaemon: starts at boot, before anyone signs in, and the counterpart of the Windows
/// service. Like that one it should name the user's own account, and it comes with a caveat that has
/// no Windows equivalent.
/// </summary>
public sealed class LaunchDaemonProvider : LaunchdProvider
{
    public LaunchDaemonProvider(IProcessLauncher processes) : base(processes)
    {
    }

    public override string Kind => "launch-daemon";

    public override string DisplayName => "LaunchDaemon";

    public override AutostartScope Scope => AutostartScope.System;

    /// <summary>
    /// The caveat is worth stating in the UI, not just in the documentation: the login Keychain is
    /// locked until someone signs in, so a daemon started at boot cannot read the Claude Code token
    /// that macOS keeps there. It will sit waiting for a sign-in that the LaunchAgent would not need.
    /// </summary>
    public override string Summary =>
        "Starts at boot, before anyone signs in. Name your own account — and note that the login " +
        "Keychain stays locked until you sign in, so the Claude Code token is unreadable until then.";

    public override bool SupportsAccount => true;

    public override string PlistPath => Path.Combine("/Library/LaunchDaemons", $"{Label}.plist");

    protected override string Domain => "system";

    public override bool RequiresElevation(AutostartOptions options) => true;

    public override string? StartBeforeSignInRequirement => null;
}
