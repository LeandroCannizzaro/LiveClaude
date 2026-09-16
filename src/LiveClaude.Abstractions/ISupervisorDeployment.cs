namespace LiveClaude.Abstractions;

/// <summary>Outcome of refreshing the copy of the supervisor that autostart points at.</summary>
public sealed record DeploymentResult(string ExecutablePath, IReadOnlyList<string> Locked, string? Version)
{
    public bool UpToDate => Locked.Count == 0;
}

/// <summary>
/// Gives the supervisor a path that does not move between updates.
///
/// This only means something on Windows, where a ClickOnce install lives under a random folder and a
/// winget portable one under a versioned package folder — both replaced on every update, which would
/// leave a task or a service pointing at an executable that no longer exists. Linux and macOS install
/// to stable locations, so their implementation reports the running path and does nothing.
/// </summary>
public interface ISupervisorDeployment
{
    /// <summary>The supervisor executable's file name on this platform.</summary>
    string SupervisorExecutable { get; }

    /// <summary>The application executable's file name on this platform.</summary>
    string AppExecutable { get; }

    /// <summary>Where a copy is kept when the install location is volatile. Empty when never needed.</summary>
    string StableDirectory { get; }

    /// <summary>True when the folder the app runs from is replaced on update.</summary>
    bool IsVolatileLocation(string directory);

    /// <summary>Refreshes the stable copy if needed and reports what could not be replaced.</summary>
    DeploymentResult Deploy(string? sourceDirectory = null);

    /// <summary>
    /// Stops processes running from a folder so its files can be replaced. A supervisor started from
    /// the deployed folder holds its own files open, and without this an update silently keeps
    /// registering the previous build.
    /// </summary>
    IReadOnlyList<string> StopProcessesIn(string directory);

    string? ReadVersion(string executablePath);

    /// <summary>A sentence for the message shown after installing, or null when there is nothing to say.</summary>
    string? DescribeDeployment(string supervisorPath);

    /// <summary>
    /// Picks the executable to register for a scope, which is not always the supervisor: a ClickOnce
    /// install ships it without its runtime configuration, and .NET then refuses to start it.
    /// </summary>
    SupervisorCommand ResolveCommand(string directory, AutostartScope scope);
}
