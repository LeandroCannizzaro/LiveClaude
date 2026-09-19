using System.ComponentModel;
using System.IO;
using LiveClaude.Abstractions;
using LiveClaude.Core.Logging;

namespace LiveClaude.App.ViewModels;

/// <summary>
/// One autostart host — a scheduled task, a Windows service, a systemd unit, a LaunchAgent — as the
/// Hosting page sees it. The page shows one card per scope the platform offers.
/// </summary>
public sealed class AutostartHostViewModel : ObservableObject
{
    private readonly IAutostartProvider _provider;
    private readonly HostingViewModel _owner;

    private string _status = "unknown";
    private string _result = "";
    private bool _busy;

    public AutostartHostViewModel(IAutostartProvider provider, HostingViewModel owner)
    {
        _provider = provider;
        _owner = owner;
    }

    public string DisplayName => _provider.DisplayName;

    public string Kind => _provider.Kind;

    public string Summary => _provider.Summary;

    public AutostartScope Scope => _provider.Scope;

    public bool SupportsAccount => _provider.SupportsAccount;

    public bool RequiresPassword => _provider.RequiresPassword;

    public string? StartBeforeSignInRequirement => _provider.StartBeforeSignInRequirement;

    public bool IsRecommended => Scope == AutostartScope.User;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Result
    {
        get => _result;
        private set
        {
            if (SetProperty(ref _result, value))
                OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => !string.IsNullOrWhiteSpace(Result);

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    public async Task RefreshAsync()
    {
        var state = await _provider.QueryAsync();

        Status = state.Installed switch
        {
            true => state.Status ?? "installed",
            // The supervisor answering on the endpoint is proof it runs, even when the registration
            // itself is unreadable from this account.
            false when string.Equals(_owner.ConnectedSupervisorHost, Kind, StringComparison.OrdinalIgnoreCase) =>
                "running (not listed for this account)",
            false => "not installed",
            _ => "running, not readable here"
        };

        // Explain an unreadable registration once, rather than leaving the card looking wrong.
        if (state.Installed is null && state.Detail is not null && string.IsNullOrWhiteSpace(Result))
            Result = state.Detail;
    }

    /// <summary>
    /// Installs and starts the host.
    ///
    /// The shape is the same on every platform: refresh the copy that will be registered, install
    /// directly when this process already has the rights, otherwise re-launch elevated and run the
    /// same CLI verb. Never trust the exit code — query the registration back before claiming
    /// success, because an install that reported success while doing nothing is exactly the failure
    /// this flow was built around.
    /// </summary>
    public Task InstallAsync(string? password = null, bool useSystemAccount = false) => RunAsync(async () =>
    {
        var options = new AutostartOptions
        {
            StartBeforeSignIn = true,
            UserName = SupportsAccount && !useSystemAccount ? _owner.Account : null,
            Password = password
        };

        if (RequiresPassword && !string.IsNullOrWhiteSpace(options.UserName) && string.IsNullOrEmpty(password))
        {
            return $"Enter the password for {options.UserName}. {DisplayName} cannot log on with a blank one " +
                   "(error 1069). Clear the account box to install it as the system account instead — which " +
                   "usually cannot reach the Claude Code sign-in.";
        }

        var (command, note) = await _owner.PrepareAsync(Scope);

        if (!_provider.RequiresElevation(options) || _owner.IsElevated)
        {
            var direct = await _provider.InstallAsync(command, options, CancellationToken.None);
            if (!direct.Success)
                return $"Could not install {DisplayName.ToLowerInvariant()}: {direct.Message}";

            await _provider.StartAsync(CancellationToken.None);
            return HostingViewModel.Combine($"{DisplayName} installed and started. {direct.Message}".Trim(), note);
        }

        var elevated = await _owner.RunElevatedVerbAsync(BuildInstallArguments(command, options));

        if (elevated == 0)
        {
            var verification = await _provider.QueryAsync();

            if (verification.Installed == true)
                return HostingViewModel.Combine($"{DisplayName} installed and started ({verification.Status ?? "ready"}).", note);

            if (verification.Installed is null)
            {
                return HostingViewModel.Combine(
                    $"{DisplayName} installed and started. This account cannot read the registration back — it was " +
                    "made through the elevation prompt, possibly by another account — so the card above cannot show " +
                    "its state.",
                    note);
            }

            // Reported success, nothing registered: say so and fall through rather than lie.
            Result = HostingViewModel.Combine(
                         $"The elevated install reported success but no {DisplayName.ToLowerInvariant()} exists; retrying without elevation.",
                         note) +
                     $"{Environment.NewLine}What ran:{Environment.NewLine}{HostingViewModel.ReadInstallLog()}";
        }

        return await InstallWithoutElevationAsync(command, options, note);
    });

    /// <summary>
    /// What is still possible when the elevation prompt is declined.
    ///
    /// For a user-scope host that is a registration without the start-before-sign-in part: on Windows
    /// a logon-only task, on Linux a user unit without lingering. Everything works except starting
    /// before anyone signs in. A system-scope host has no such fallback — it is elevation or nothing.
    /// </summary>
    private async Task<string> InstallWithoutElevationAsync(SupervisorCommand command, AutostartOptions options, string note)
    {
        if (Scope == AutostartScope.System)
            return HostingViewModel.Combine($"The elevation prompt was declined, so the {DisplayName.ToLowerInvariant()} was not installed.", note);

        options.StartBeforeSignIn = false;
        var fallback = await _provider.InstallAsync(command, options, CancellationToken.None);

        if (!fallback.Success)
            return $"Could not install {DisplayName.ToLowerInvariant()}: {fallback.Message}";

        await _provider.StartAsync(CancellationToken.None);
        var verified = await _provider.QueryAsync();

        var outcome = verified.Installed switch
        {
            true => $"Installed for sign-in only and started ({verified.Status ?? "ready"}).",
            false => $"Reported success but the {DisplayName.ToLowerInvariant()} is not listed afterwards.",
            _ => "Installed for sign-in only and started; this account cannot read it back to confirm."
        };

        var why = StartBeforeSignInRequirement is { Length: > 0 } requirement
            ? $" {requirement}"
            : "";

        return HostingViewModel.Combine(
            outcome +
            " The elevation prompt was declined or cancelled, so the servers start when you sign in, not before." +
            why,
            note);
    }

    private string[] BuildInstallArguments(SupervisorCommand command, AutostartOptions options)
    {
        var args = new List<string>
        {
            "install-autostart",
            "--scope", Scope == AutostartScope.System ? "system" : "user",
            "--exe", command.ExecutablePath,
            "--args", command.Arguments
        };

        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            args.Add("--user");
            args.Add(options.UserName);
        }

        if (RequiresPassword && !string.IsNullOrWhiteSpace(options.UserName))
        {
            args.Add("--password");
            args.Add(options.Password ?? "");
        }

        return args.ToArray();
    }

    public Task UninstallAsync() => RunAsync(async () =>
    {
        await _provider.StopAsync(CancellationToken.None);
        var result = await _provider.UninstallAsync(CancellationToken.None);

        if (result.Success)
            return $"{DisplayName} removed.";

        if (!_owner.IsElevated)
        {
            var exitCode = await _owner.RunElevatedVerbAsync(
                ["uninstall-autostart", "--scope", Scope == AutostartScope.System ? "system" : "user"]);

            if (exitCode == 0)
                return $"{DisplayName} removed.";
        }

        return result.Message;
    });

    public Task StartAsync() => RunAsync(async () =>
    {
        var start = await _provider.StartAsync(CancellationToken.None);
        if (start.Success)
            return $"{DisplayName} started.";

        if (!_owner.IsElevated &&
            await _owner.RunElevatedVerbAsync(["start-autostart", "--scope", Scope == AutostartScope.System ? "system" : "user"]) == 0)
        {
            return $"{DisplayName} started.";
        }

        return start.Message;
    });

    public Task StopAsync() => RunAsync(async () =>
    {
        var stop = await _provider.StopAsync(CancellationToken.None);
        if (stop.Success)
            return $"{DisplayName} stopped.";

        if (!_owner.IsElevated &&
            await _owner.RunElevatedVerbAsync(["stop-autostart", "--scope", Scope == AutostartScope.System ? "system" : "user"]) == 0)
        {
            return $"{DisplayName} stopped.";
        }

        return stop.Message;
    });

    /// <summary>
    /// Stops the host without reporting anything. Used before refreshing the deployed copy, where a
    /// "not installed" answer is the normal case and would only be noise on the card.
    /// </summary>
    internal async Task StopQuietlyAsync()
    {
        try
        {
            await _provider.StopAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Nothing to stop, or not ours to stop.
        }
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        Busy = true;
        _owner.NotifyBusy(true);

        try
        {
            Result = await action();
        }
        catch (Exception ex)
        {
            Result = ex.Message;
        }
        finally
        {
            Busy = false;
            _owner.NotifyBusy(false);
            await RefreshAsync();
        }
    }
}

/// <summary>
/// Drives the autostart hosts this platform offers: one for the signed-in session, and one that
/// starts before sign-in where the platform has such a thing.
/// </summary>
public sealed class HostingViewModel : ObservableObject
{
    private static IPlatform Platform => PlatformLoader.Current;

    private string _account = Platform.Processes.CurrentUserName;
    private string _supervisorBuild = "";
    private bool _busy;
    private string? _supervisorPath;

    public HostingViewModel()
    {
        UserHost = new AutostartHostViewModel(Platform.UserAutostart, this);
        SystemHost = Platform.SystemAutostart is { } system
            ? new AutostartHostViewModel(system, this)
            : null;
    }

    /// <summary>The recommended host: runs in the user's session, where the Claude credentials are.</summary>
    public AutostartHostViewModel UserHost { get; }

    /// <summary>Starts before anyone signs in. Null on a platform with no such host.</summary>
    public AutostartHostViewModel? SystemHost { get; }

    public bool HasSystemHost => SystemHost is not null;

    /// <summary>What to call the elevation prompt in the UI: "UAC", "pkexec", "osascript".</summary>
    public string ElevationMechanism => Platform.Elevation.Mechanism;

    public bool IsElevated => Platform.Processes.IsElevated;

    public string Account
    {
        get => _account;
        set => SetProperty(ref _account, value);
    }

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    internal void NotifyBusy(bool busy) => Busy = busy;

    /// <summary>Set by the shell so a card can say a supervisor is running even when unreadable.</summary>
    public string? ConnectedSupervisorHost { get; set; }

    /// <summary>
    /// Path of the supervisor to register. On Windows, ClickOnce and winget put the app in a folder
    /// that is replaced on every update, so a stable copy is deployed first; elsewhere the install
    /// location does not move and this is simply where the app runs from.
    /// </summary>
    public string SupervisorPath => _supervisorPath ??= Platform.Deployment.Deploy().ExecutablePath;

    /// <summary>
    /// The executable to actually run elevated for an install/uninstall/start/stop verb.
    ///
    /// Not the same as <see cref="SupervisorPath"/>: that one is always the supervisor executable, for
    /// display and for registering the long-running host once elevation has already happened. This one
    /// has to start on its own right now, in the elevated process, so it goes through the same
    /// can-it-actually-run check as the registered command — a ClickOnce install ships the supervisor
    /// without its runtime configuration, and elevating it directly is exactly the ".NET Desktop
    /// Runtime is required" dialog instead of the install this was supposed to perform.
    /// </summary>
    private string ElevatorPath
    {
        get
        {
            var directory = Path.GetDirectoryName(SupervisorPath)!;
            return Platform.Deployment.ResolveCommand(directory, AutostartScope.User).ExecutablePath;
        }
    }

    /// <summary>
    /// The build sitting in the deployed copy — the one a registration actually runs. Shown always,
    /// because a copy left behind by a locked file is invisible otherwise.
    /// </summary>
    public string SupervisorBuild
    {
        get => _supervisorBuild;
        private set => SetProperty(ref _supervisorBuild, value);
    }

    public async Task RefreshAsync()
    {
        UpdateSupervisorBuild();

        await UserHost.RefreshAsync();

        if (SystemHost is not null)
            await SystemHost.RefreshAsync();
    }

    /// <summary>
    /// Refreshes the copy that will be registered, before registering anything.
    ///
    /// On Windows the registered host runs that copy, so its files are locked while it runs and the
    /// refresh used to be skipped without a word: every install then ran whatever build was there
    /// before, which is how an install could quietly do nothing. The hosts are stopped, the copy
    /// refreshed, and starting again is left to the caller. On Linux and macOS the deployment is a
    /// no-op and this costs two cheap queries.
    /// </summary>
    internal async Task<(SupervisorCommand Command, string Note)> PrepareAsync(AutostartScope scope)
    {
        var deployment = Platform.Deployment;

        // Nothing may be running from the deployment folder while it is refreshed: a process started
        // from there holds its own executable, and the copy then keeps an old build that gets
        // registered and run. Stop the hosts, then kill anything left over from earlier attempts.
        await UserHost.StopQuietlyAsync();
        if (SystemHost is not null)
            await SystemHost.StopQuietlyAsync();

        var stopped = deployment.StopProcessesIn(deployment.StableDirectory).ToList();
        if (stopped.Count > 0)
            await Task.Delay(TimeSpan.FromSeconds(1));

        var result = deployment.Deploy();

        // One more round if something grabbed a file in the meantime.
        if (!result.UpToDate)
        {
            stopped.AddRange(deployment.StopProcessesIn(deployment.StableDirectory));
            await Task.Delay(TimeSpan.FromSeconds(1));
            result = deployment.Deploy();
        }

        _supervisorPath = result.ExecutablePath;
        var command = deployment.ResolveCommand(Path.GetDirectoryName(result.ExecutablePath)!, scope);

        var notes = new List<string>();

        if (deployment.DescribeDeployment(result.ExecutablePath) is { } deployed)
            notes.Add(deployed);

        if (command.Note is not null)
            notes.Add(command.Note);

        if (result.Version is not null)
            notes.Add($"Registered build: {result.Version}.");

        if (stopped.Count > 0)
            notes.Add($"Stopped {string.Join(", ", stopped)} to refresh the copy.");

        if (!result.UpToDate)
        {
            notes.Add(
                $"Warning: {result.Locked.Count} file(s) could not be refreshed because something still has them " +
                $"open ({string.Join(", ", result.Locked.Take(3))}). Close every LiveClaude window, stop every host, " +
                "then install again — otherwise this registers an older build.");
        }

        return (command, string.Join(" ", notes));
    }

    /// <summary>
    /// Re-launches the supervisor elevated with a CLI verb and waits for it.
    ///
    /// The elevation goes through the supervisor executable when it can run on its own; otherwise the
    /// application elevates itself, which is what a ClickOnce install needs.
    /// </summary>
    internal async Task<int> RunElevatedVerbAsync(string[] arguments)
    {
        var elevator = ElevatorPath;

        try
        {
            LogInstallStep($"elevating {elevator} (build {Platform.Deployment.ReadVersion(elevator) ?? "unknown"}) for {arguments.FirstOrDefault()}");
            var exitCode = await Platform.Elevation.RunElevatedAsync(elevator, arguments);
            LogInstallStep($"{arguments.FirstOrDefault()} returned {exitCode}");
            return exitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // The prompt was declined, or there is no way to elevate on this machine.
            LogInstallStep($"the elevation prompt was declined or unavailable: {ex.Message}");
            return -1;
        }
    }

    private void UpdateSupervisorBuild()
    {
        var deployment = Platform.Deployment;
        var running = deployment.ReadVersion(Environment.ProcessPath ?? "") ?? "unknown";
        var directory = Path.GetDirectoryName(SupervisorPath);
        var deployedApp = directory is null ? null : Path.Combine(directory, deployment.AppExecutable);
        var deployed = deployment.ReadVersion(deployedApp ?? SupervisorPath);

        SupervisorBuild = deployed is null
            ? $"app {running}"
            : deployed == running
                ? $"app and registered copy: {running}"
                : $"app {running}, but the registered copy is {deployed} — install again to refresh it";
    }

    /// <summary>
    /// Records what the app is about to launch elevated. The elevated process may be an old build
    /// that writes nothing at all, and then this is the only evidence of what actually ran.
    /// </summary>
    private static void LogInstallStep(string message)
    {
        try
        {
            using var log = new RollingLogWriter(Service.SupervisorCli.LogPath, maxSizeMb: 2);
            log.Write($"[app] {message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort
        }
    }

    /// <summary>
    /// The last lines an install command printed. Those commands run elevated, in a process the user
    /// never sees, so this is the only way the reason for a failure reaches the screen.
    /// </summary>
    internal static string ReadInstallLog(int lines = 8)
    {
        try
        {
            var log = new RollingLogWriter(Service.SupervisorCli.LogPath, maxSizeMb: 2);
            var tail = log.Tail(lines);
            log.Dispose();

            return tail.Count == 0
                ? $"(nothing was written to {Service.SupervisorCli.LogPath})"
                : string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(the install log could not be read)";
        }
    }

    internal static string Combine(string message, string? note) =>
        string.IsNullOrWhiteSpace(note) ? message : $"{message} {note}";
}
