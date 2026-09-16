using LiveClaude.Abstractions;
using LiveClaude.Platform.Linux.Autostart;
using LiveClaude.Platform.MacOS.Autostart;
using LiveClaude.Platform.Posix;
using Xunit;

namespace LiveClaude.Tests.Platform;

/// <summary>
/// The unit files, property lists and shell quoting the POSIX providers produce.
///
/// These are pure functions of their input, so they are asserted on every operating system — the
/// point of a unit file being wrong is that you find out at the next reboot, and a test that only
/// runs on the machine that would have caught it is no help.
/// </summary>
public class PosixAutostartTests
{
    private static readonly SupervisorCommand Command =
        new("/opt/liveclaude/LiveClaude.Service", "--supervise");

    private static SystemdUserProvider UserUnit() =>
        new(new PosixProcessLauncher(), new PosixElevator());

    private static SystemdSystemProvider SystemUnit() => new(new PosixProcessLauncher());

    /// <summary>
    /// Restart=always is the counterpart of the scheduled task's "restart every minute on failure".
    /// StartLimitIntervalSec=0 matters just as much and is easier to miss: without it systemd gives
    /// up after five restarts in ten seconds, which is exactly what a machine waking with no network
    /// looks like, and the supervisor would then stay down until someone noticed.
    /// </summary>
    [Fact]
    public void TheUserUnitRestartsForeverAndStartsAtLogin()
    {
        var unit = Build(UserUnit(), new AutostartOptions());

        Assert.Contains("Restart=always", unit);
        Assert.Contains("RestartSec=5", unit);
        Assert.Contains("StartLimitIntervalSec=0", unit);
        Assert.Contains("WantedBy=default.target", unit);
        Assert.Contains("ExecStart=/opt/liveclaude/LiveClaude.Service --supervise", unit);
    }

    /// <summary>
    /// SIGINT, not SIGTERM: it is what the supervisor turns into a clean stop of every server, which
    /// is how a Remote Control server deregisters its bridge environment instead of leaving a dead
    /// entry in the session picker. The stop timeout has to outlast all of them doing so.
    /// </summary>
    [Fact]
    public void TheUnitStopsServersTheWayTheyExpectToBeStopped()
    {
        var unit = Build(UserUnit(), new AutostartOptions());

        Assert.Contains("KillSignal=SIGINT", unit);
        Assert.Contains("TimeoutStopSec=90", unit);
    }

    /// <summary>A user unit is the session's own; naming an account in it would be rejected.</summary>
    [Fact]
    public void TheUserUnitNeverNamesAnAccount()
    {
        var unit = Build(UserUnit(), new AutostartOptions { UserName = "someone" });

        Assert.DoesNotContain("User=", unit);
    }

    [Fact]
    public void TheSystemUnitRunsAsTheNamedAccountAndStartsAtBoot()
    {
        var unit = Build(SystemUnit(), new AutostartOptions { UserName = "leandro" });

        Assert.Contains("User=leandro", unit);
        Assert.Contains("WantedBy=multi-user.target", unit);
    }

    /// <summary>
    /// A system unit runs outside any login session, so the per-user configuration root would resolve
    /// against root's home directory rather than the one the app writes to. Pinning it is what keeps
    /// the daemon and the desktop app looking at the same sessions.
    /// </summary>
    [Fact]
    public void TheSystemUnitPinsTheConfigurationRoot()
    {
        var unit = Build(SystemUnit(), new AutostartOptions { UserName = "leandro" });

        Assert.Contains($"Environment={PathLayout.RootOverrideVariable}=/var/lib/liveclaude", unit);
    }

    [Fact]
    public void TheLaunchAgentRunsAtLoadAndIsKeptAlive()
    {
        var plist = Build(new LaunchAgentProvider(new PosixProcessLauncher()), new AutostartOptions());

        Assert.Contains("<key>RunAtLoad</key>", plist);
        Assert.Contains("<key>KeepAlive</key>", plist);
        Assert.Contains("<string>com.leandrocannizzaro.liveclaude</string>", plist);
        Assert.Contains("<string>/opt/liveclaude/LiveClaude.Service</string>", plist);
        Assert.Contains("<string>--supervise</string>", plist);
    }

    /// <summary>
    /// ExitTimeOut is launchd's equivalent of TimeoutStopSec. The default is twenty seconds, which is
    /// not enough for several servers to each be given the configured grace period.
    /// </summary>
    [Fact]
    public void TheLaunchAgentAllowsTimeForServersToStopCleanly()
    {
        var plist = Build(new LaunchAgentProvider(new PosixProcessLauncher()), new AutostartOptions());

        Assert.Contains("<key>ExitTimeOut</key>", plist);
        Assert.Contains("<integer>90</integer>", plist);
    }

    [Fact]
    public void TheLaunchDaemonRunsAsTheNamedAccount()
    {
        var plist = Build(new LaunchDaemonProvider(new PosixProcessLauncher()), new AutostartOptions { UserName = "leandro" });

        Assert.Contains("<key>UserName</key>", plist);
        Assert.Contains("<string>leandro</string>", plist);
    }

    /// <summary>
    /// A LaunchAgent cannot start before sign-in and a systemd user unit needs lingering. Both say so
    /// rather than quietly installing something that will not do what was asked.
    /// </summary>
    [Fact]
    public void EachUserHostExplainsWhatStartingBeforeSignInTakes()
    {
        Assert.Contains("linger", UserUnit().StartBeforeSignInRequirement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("log in", new LaunchAgentProvider(new PosixProcessLauncher()).StartBeforeSignInRequirement!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("/usr/local/bin/claude", "/usr/local/bin/claude")]
    [InlineData("with space", "'with space'")]
    [InlineData("it's", @"'it'\''s'")]
    [InlineData("", "''")]
    public void PosixArgumentsAreQuotedTheWayAShellReadsThemBack(string input, string expected) =>
        Assert.Equal(expected, PosixProcessLauncher.Quote(input));

    /// <summary>
    /// Reaches BuildUnit / BuildPlist, which are protected because nothing outside the provider
    /// should be writing these files — but they are the whole of what an install produces, so they
    /// are worth pinning.
    /// </summary>
    private static string Build(IAutostartProvider provider, AutostartOptions options)
    {
        var method = provider.GetType()
            .GetMethod("BuildUnit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? provider.GetType().GetMethod("BuildPlist", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (string)method!.Invoke(provider, [Command, options])!;
    }
}
