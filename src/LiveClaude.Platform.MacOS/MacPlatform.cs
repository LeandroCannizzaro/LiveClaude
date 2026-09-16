using System.Runtime.InteropServices;
using LiveClaude.Abstractions;
using LiveClaude.Platform.MacOS.Autostart;
using LiveClaude.Platform.Posix;
using LiveClaude.Platform.Posix.Ipc;
using LiveClaude.Platform.Posix.Pty;

[assembly: LiveClaudePlatform(typeof(LiveClaude.Platform.MacOS.MacPlatform))]

namespace LiveClaude.Platform.MacOS;

/// <summary>macOS: ptys, launchd, the authentication sheet, the login Keychain.</summary>
public sealed class MacPlatform : IPlatform
{
    private readonly MacPathLayout _paths = new();

    public MacPlatform()
    {
        Processes = new PosixProcessLauncher();
        Elevation = new MacElevator();
        Ipc = new UnixSocketEndpointFactory(_paths);
        UserAutostart = new LaunchAgentProvider(Processes);
        SystemAutostart = new LaunchDaemonProvider(Processes);
    }

    public string Id => "macos";

    public string DisplayName => $"macOS ({RuntimeInformation.OSDescription.Trim()})";

    public IPtyFactory Pty { get; } = new PosixPtyFactory();

    public IPathLayout Paths => _paths;

    public IIpcEndpointFactory Ipc { get; }

    public IIpcEndpointFactory CreateIpcEndpoint(string name) => new UnixSocketEndpointFactory(_paths, name);

    public IProcessLauncher Processes { get; }

    public IPrivilegeElevator Elevation { get; }

    public IAutostartProvider UserAutostart { get; }

    public IAutostartProvider? SystemAutostart { get; }

    public IClaudeDiscovery Claude { get; } = new MacClaudeDiscovery();

    public IShellIntegration Shell { get; } = new MacShellIntegration();

    public ISupervisorDeployment Deployment { get; } = new MacSupervisorDeployment();

    /// <summary>Nothing to do: a POSIX process already inherits the terminal it was started from.</summary>
    public void AttachToParentConsole()
    {
    }
}

/// <summary>
/// Almost the POSIX no-op, with one macOS detail: inside an application bundle the executables live
/// in <c>LiveClaude.app/Contents/MacOS</c>, so that is where a registration has to point.
/// </summary>
public sealed class MacSupervisorDeployment : PosixSupervisorDeployment
{
    /// <summary>
    /// A bundle path is stable across updates — the .app is replaced in place — so there is still
    /// nothing to copy. Worth naming so that reading the plist later is not a puzzle.
    /// </summary>
    public bool IsInsideBundle(string directory) =>
        directory.Contains(".app/Contents/MacOS", StringComparison.Ordinal);

    public override string? DescribeDeployment(string supervisorPath) =>
        IsInsideBundle(supervisorPath)
            ? "The registration points inside the application bundle, which keeps its path across updates."
            : null;
}
