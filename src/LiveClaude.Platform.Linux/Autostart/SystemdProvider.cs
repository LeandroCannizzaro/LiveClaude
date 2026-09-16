using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Linux.Autostart;

/// <summary>
/// The supervisor as a systemd unit. Two scopes share almost everything: a user unit under
/// <c>~/.config/systemd/user</c> managed with <c>systemctl --user</c>, and a system unit under
/// <c>/etc/systemd/system</c> that names the account to run as.
/// </summary>
public abstract class SystemdProvider : IAutostartProvider
{
    protected const string UnitName = "liveclaude.service";

    private readonly IProcessLauncher _processes;

    protected SystemdProvider(IProcessLauncher processes)
    {
        _processes = processes;
    }

    public abstract string Kind { get; }

    public abstract string DisplayName { get; }

    public abstract AutostartScope Scope { get; }

    public abstract string Summary { get; }

    public abstract bool SupportsAccount { get; }

    public abstract string UnitPath { get; }

    public abstract bool RequiresElevation(AutostartOptions options);

    public abstract string? StartBeforeSignInRequirement { get; }

    /// <summary>systemd never asks for a password; the unit names an account and that is that.</summary>
    public bool RequiresPassword => false;

    /// <summary>The <c>--user</c> flag, or nothing for the system manager.</summary>
    protected abstract string[] ScopeArguments { get; }

    public async Task<AutostartState> QueryAsync(CancellationToken ct = default)
    {
        var result = await SystemctlAsync(
            ["show", UnitName, "--property=LoadState", "--property=ActiveState", "--property=SubState", "--property=UnitFileState"],
            ct).ConfigureAwait(false);

        if (!result.Success)
            return Classify(result);

        var properties = Parse(result.StandardOutput);

        // LoadState is the honest answer to "does this unit exist?". ActiveState alone says "inactive"
        // for a unit that was never installed, which reads as "installed but stopped".
        if (!properties.TryGetValue("LoadState", out var loadState) || loadState is "not-found")
            return new AutostartState(false, null, null);

        properties.TryGetValue("ActiveState", out var active);
        properties.TryGetValue("SubState", out var sub);
        properties.TryGetValue("UnitFileState", out var fileState);

        var status = sub is { Length: > 0 } && sub != active ? $"{active} ({sub})" : active;

        if (fileState is "disabled")
            status += ", not enabled at startup";

        return new AutostartState(true, status, await ReadRunAsAsync(ct).ConfigureAwait(false));
    }

    public async Task<AutostartResult> InstallAsync(SupervisorCommand command, AutostartOptions options, CancellationToken ct = default)
    {
        var notes = new List<string>();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UnitPath)!);
            await File.WriteAllTextAsync(UnitPath, BuildUnit(command, options), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AutostartResult.Failed($"Could not write {UnitPath}: {ex.Message}");
        }

        var reload = await SystemctlAsync(["daemon-reload"], ct).ConfigureAwait(false);
        if (!reload.Success)
            return AutostartResult.Failed(Explain(reload));

        var enable = await SystemctlAsync(["enable", UnitName], ct).ConfigureAwait(false);
        if (!enable.Success)
            return AutostartResult.Failed(Explain(enable));

        notes.Add($"Unit written to {UnitPath} and enabled.");

        if (options.StartBeforeSignIn && await ApplyStartBeforeSignInAsync(ct).ConfigureAwait(false) is { Length: > 0 } note)
            notes.Add(note);

        return AutostartResult.Ok(string.Join(" ", notes));
    }

    /// <summary>Whatever the scope needs beyond enabling, if anything. Returns a note for the user.</summary>
    protected virtual Task<string> ApplyStartBeforeSignInAsync(CancellationToken ct) => Task.FromResult("");

    public async Task<AutostartResult> UninstallAsync(CancellationToken ct = default)
    {
        await SystemctlAsync(["disable", "--now", UnitName], ct).ConfigureAwait(false);

        try
        {
            if (File.Exists(UnitPath))
                File.Delete(UnitPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AutostartResult.Failed($"Could not remove {UnitPath}: {ex.Message}");
        }

        await SystemctlAsync(["daemon-reload"], ct).ConfigureAwait(false);
        return AutostartResult.Ok($"Removed {UnitPath}.");
    }

    public async Task<AutostartResult> StartAsync(CancellationToken ct = default) =>
        ToResult(await SystemctlAsync(["start", UnitName], ct).ConfigureAwait(false));

    public async Task<AutostartResult> StopAsync(CancellationToken ct = default) =>
        ToResult(await SystemctlAsync(["stop", UnitName], ct).ConfigureAwait(false));

    protected Task<ProcessResult> SystemctlAsync(IEnumerable<string> arguments, CancellationToken ct) =>
        _processes.RunAsync("systemctl", [.. ScopeArguments, .. arguments], ct: ct);

    protected static AutostartResult ToResult(ProcessResult result) =>
        result.Success ? AutostartResult.Ok(result.Combined) : AutostartResult.Failed(Explain(result));

    /// <summary>
    /// Turns a systemctl failure into something actionable, the way the Windows provider decodes
    /// error 1069 and friends.
    /// </summary>
    protected static string Explain(ProcessResult result)
    {
        var text = result.Combined;

        if (text.Contains("Failed to connect to bus", StringComparison.OrdinalIgnoreCase))
        {
            return "systemd's user manager is not reachable from here (no session bus). That happens over " +
                   "ssh and in containers. Run 'loginctl enable-linger " + Environment.UserName + "' so the user " +
                   "manager runs without an interactive session, or use the system-wide unit instead.";
        }

        if (text.Contains("Interactive authentication required", StringComparison.OrdinalIgnoreCase))
            return "polkit refused without a prompt: this has to run elevated (pkexec or sudo).";

        if (text.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Permission denied: writing a system unit needs root.";
        }

        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase) && text.Contains("systemctl", StringComparison.OrdinalIgnoreCase))
            return "systemctl was not found. This machine does not use systemd; start the supervisor from your session manager instead.";

        return text.Trim();
    }

    /// <summary>A failed query is "cannot tell", never "not installed".</summary>
    protected static AutostartState Classify(ProcessResult result) =>
        new(null, null, null, Explain(result));

    protected virtual Task<string?> ReadRunAsAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    private static Dictionary<string, string> Parse(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
                properties[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return properties;
    }

    /// <summary>
    /// The unit file. <c>Restart=always</c> with <c>RestartSec=5</c> is the counterpart of the
    /// scheduled task's "restart every minute on failure", and of the service's sc.exe failure
    /// actions — deliberately quicker, because systemd has no minimum.
    /// </summary>
    protected virtual string BuildUnit(SupervisorCommand command, AutostartOptions options)
    {
        var account = SupportsAccount && !string.IsNullOrWhiteSpace(options.UserName)
            ? $"User={options.UserName.Trim()}\n"
            : "";

        var root = PathLayout.Override is { Length: > 0 } configured
            ? $"Environment={PathLayout.RootOverrideVariable}={configured}\n"
            : "";

        return $"""
            [Unit]
            Description=LiveClaude supervisor — keeps Claude Code Remote Control servers running
            Documentation=https://github.com/LeandroCannizzaro/LiveClaude
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=simple
            ExecStart={Escape(command.ExecutablePath)} {command.Arguments}
            WorkingDirectory={Escape(Path.GetDirectoryName(command.ExecutablePath) ?? "/")}
            {account}{root}Restart=always
            RestartSec=5
            # The supervisor restarts its own servers with backoff, so systemd must not give up on it.
            StartLimitIntervalSec=0
            KillSignal=SIGINT
            # Long enough for every supervised server to be asked to stop and deregister its bridge
            # environment before anything is killed — a hard kill is what leaves dead entries behind.
            TimeoutStopSec=90

            [Install]
            WantedBy={WantedBy}

            """;
    }

    protected abstract string WantedBy { get; }

    private static string Escape(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}
