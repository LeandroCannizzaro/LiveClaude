namespace LiveClaude.Core.Hosting;

/// <summary>What to register with Task Scheduler or the service control manager.</summary>
public sealed record SupervisorCommand(string ExecutablePath, string Arguments, string? Note = null);

/// <summary>
/// Decides which executable actually hosts the supervisor.
///
/// A ClickOnce install ships LiveClaude.Service.exe but not its runtime configuration — .NET refuses
/// to start such an executable ("You must install .NET Desktop Runtime"), so registering it would
/// leave a task that fails every time. When that file is missing the desktop application hosts the
/// supervisor itself: its own runtime configuration is always deployed.
/// </summary>
public static class SupervisorLauncher
{
    public const string SuperviseArgument = "--supervise";
    public const string ServiceArgument = "--service";
    public const string AppExecutable = "LiveClaude.exe";

    /// <summary>
    /// True when the supervisor executable next to <paramref name="executablePath"/> can actually
    /// start: .NET needs the matching <c>.runtimeconfig.json</c> beside it.
    /// </summary>
    public static bool CanRunStandalone(string executablePath)
    {
        if (!File.Exists(executablePath))
            return false;

        var runtimeConfig = Path.ChangeExtension(executablePath, null) + ".runtimeconfig.json";
        return File.Exists(runtimeConfig);
    }

    /// <summary>Picks the executable and arguments to register for the given mode.</summary>
    public static SupervisorCommand Resolve(string directory, bool asService)
    {
        var argument = asService ? ServiceArgument : SuperviseArgument;
        var supervisor = Path.Combine(directory, SupervisorDeployment.SupervisorExecutable);

        if (CanRunStandalone(supervisor))
            return new SupervisorCommand(supervisor, argument);

        var app = Path.Combine(directory, AppExecutable);
        if (CanRunStandalone(app))
        {
            return new SupervisorCommand(
                app,
                argument,
                "This install does not ship the supervisor's runtime configuration (ClickOnce strips it), " +
                "so LiveClaude itself hosts the supervisor.");
        }

        // Nothing verifiable: fall back to the supervisor and let the caller report what happens.
        return new SupervisorCommand(supervisor, argument);
    }
}
