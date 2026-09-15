using System.ComponentModel;
using System.IO;
using LiveClaude.Core.Hosting;
using LiveClaude.Core.Logging;

namespace LiveClaude.App.ViewModels;

/// <summary>Drives the two supported hosts: the Windows service and the scheduled task.</summary>
public sealed class HostingViewModel : ObservableObject
{
    private string _serviceStatus = "unknown";
    private string _taskStatus = "unknown";
    private string _account = ProcessHelper.CurrentUserName;
    private string _taskResult = "";
    private string _serviceResult = "";
    private bool _busy;
    private string? _supervisorPath;

    public string ServiceStatus
    {
        get => _serviceStatus;
        private set => SetProperty(ref _serviceStatus, value);
    }

    public string TaskStatus
    {
        get => _taskStatus;
        private set => SetProperty(ref _taskStatus, value);
    }

    public string Account
    {
        get => _account;
        set => SetProperty(ref _account, value);
    }

    /// <summary>Outcome of the last scheduled-task operation, shown under that card.</summary>
    public string TaskResult
    {
        get => _taskResult;
        private set
        {
            if (SetProperty(ref _taskResult, value))
                OnPropertyChanged(nameof(HasTaskResult));
        }
    }

    public bool HasTaskResult => !string.IsNullOrWhiteSpace(TaskResult);

    /// <summary>Outcome of the last Windows-service operation, shown under that card.</summary>
    public string ServiceResult
    {
        get => _serviceResult;
        private set
        {
            if (SetProperty(ref _serviceResult, value))
                OnPropertyChanged(nameof(HasServiceResult));
        }
    }

    public bool HasServiceResult => !string.IsNullOrWhiteSpace(ServiceResult);

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    /// <summary>
    /// Path of the supervisor to register. ClickOnce and winget put the app in a folder that is
    /// replaced on every update, so in that case a stable copy is deployed first.
    /// </summary>
    public string SupervisorPath => _supervisorPath ??= SupervisorDeployment.EnsureDeployed();

    /// <summary>
    /// Refreshes the stable copy before registering anything.
    ///
    /// The scheduled task runs that copy, so its files are locked while it runs and the refresh used
    /// to be skipped without a word: every install then ran whatever build was there before, which
    /// is how an install could quietly do nothing. The supervisor is stopped, the copy refreshed and
    /// the supervisor left for the caller to start again.
    /// </summary>
    private async Task<(SupervisorCommand Command, string Note)> PrepareAsync(bool asService)
    {
        var deployment = SupervisorDeployment.Deploy();

        if (!deployment.UpToDate)
        {
            // Stop whatever is holding the files, then try once more.
            await ScheduledTaskInstaller.EndAsync();
            await WindowsServiceInstaller.StopAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));

            deployment = SupervisorDeployment.Deploy();
        }

        _supervisorPath = deployment.ExecutablePath;
        var command = ResolveCommand(asService);

        var notes = new List<string>();

        if (SupervisorDeployment.DescribeDeployment(deployment.ExecutablePath) is { } deployed)
            notes.Add(deployed);

        if (command.Note is not null)
            notes.Add(command.Note);

        if (deployment.Version is not null)
            notes.Add($"Registered build: {deployment.Version}.");

        if (!deployment.UpToDate)
        {
            notes.Add(
                $"Warning: {deployment.Locked.Count} file(s) could not be refreshed because something still has them " +
                $"open ({string.Join(", ", deployment.Locked.Take(3))}). Close every LiveClaude window, stop the task " +
                "and the service, then install again — otherwise this registers an older build.");
        }

        return (command, string.Join(" ", notes));
    }

    /// <summary>
    /// The executable and arguments to register. A ClickOnce install cannot start
    /// LiveClaude.Service.exe (its runtime configuration is not deployed), so there the desktop
    /// application hosts the supervisor itself.
    /// </summary>
    private SupervisorCommand ResolveCommand(bool asService) =>
        SupervisorLauncher.Resolve(Path.GetDirectoryName(SupervisorPath)!, asService);

    /// <summary>Set by the shell so the card can say a supervisor is running even when the task is unreadable.</summary>
    public string? ConnectedSupervisorHost { get; set; }

    public async Task RefreshAsync()
    {
        var service = WindowsServiceInstaller.Query();
        ServiceStatus = service.Installed ? service.Status ?? "installed" : "not installed";

        var task = await ScheduledTaskInstaller.QueryAsync();
        TaskStatus = task.Installed switch
        {
            true => task.Status ?? "installed",
            false when string.Equals(ConnectedSupervisorHost, "task", StringComparison.OrdinalIgnoreCase) =>
                "running (not listed for this account)",
            false => "not installed",
            _ => "running, not readable here"
        };

        // Explain an unreadable task once, rather than leaving the card looking wrong.
        if (task.Installed is null && task.Detail is not null && string.IsNullOrWhiteSpace(TaskResult))
            TaskResult = task.Detail;
    }

    /// <summary>
    /// Installs the scheduled task. The boot trigger can only be registered by an administrator, so
    /// this asks for elevation; declining falls back to a logon-only task, which works as a normal
    /// user and covers everything except starting before sign-in.
    /// </summary>
    public Task InstallTaskAsync() => RunAsync(result => TaskResult = result, async () =>
    {
        var (command, note) = await PrepareAsync(asService: false);

        if (ProcessHelper.IsElevated)
        {
            var elevatedInstall = await ScheduledTaskInstaller.InstallAsync(
                command.ExecutablePath, runAtBoot: true, userName: ProcessHelper.CurrentUserName, arguments: command.Arguments);

            if (!elevatedInstall.Success)
                return $"Could not install the task: {elevatedInstall.Combined}";

            await ScheduledTaskInstaller.RunAsync();
            return Combine("Installed with the logon and boot triggers, and started.", note);
        }

        var exitCode = -1;
        try
        {
            // The elevation goes through the supervisor executable when it can run; otherwise the
            // app elevates itself. The user is passed explicitly because UAC may be answered with a
            // different administrator account.
            var elevator = SupervisorLauncher.CanRunStandalone(SupervisorPath) ? SupervisorPath : command.ExecutablePath;
            exitCode = await ProcessHelper.RunElevatedAsync(elevator,
            [
                "install-task",
                "--user", ProcessHelper.CurrentUserName,
                "--exe", command.ExecutablePath,
                "--args", command.Arguments
            ]);
        }
        catch (Win32Exception)
        {
            // UAC declined, or no administrator available on this machine.
        }

        if (exitCode == 0)
        {
            // Never take the exit code's word for it: verify the task is really there.
            var verification = await ScheduledTaskInstaller.QueryAsync();

            if (verification.Installed == true)
                return Combine($"Installed with the logon and boot triggers, and started ({verification.Status ?? "ready"}).", note);

            if (verification.Installed is null)
            {
                return Combine(
                    "Installed with the logon and boot triggers, and started. Windows will not let this account " +
                    "read the task back — it was registered through the elevation prompt by another administrator " +
                    "account — so the card above cannot show its state.",
                    note);
            }

            // Reported success, no task: fall through to the unelevated install rather than lie.
            TaskResult = "The elevated install reported success but no task exists; retrying without elevation. " +
                         $"What it did:{Environment.NewLine}{ReadInstallLog()}";
        }

        var fallback = await ScheduledTaskInstaller.InstallAsync(
            command.ExecutablePath, runAtBoot: false, userName: ProcessHelper.CurrentUserName, arguments: command.Arguments);
        if (!fallback.Success)
        {
            return ScheduledTaskInstaller.IsAccessDenied(fallback)
                ? $"Windows refused the task: {fallback.Combined.Trim()} Your account may be blocked from creating scheduled tasks by policy; the Windows service below is the alternative."
                : $"Could not install the task: {fallback.Combined}";
        }

        await ScheduledTaskInstaller.RunAsync();

        var verified = await ScheduledTaskInstaller.QueryAsync();
        var outcome = verified.Installed switch
        {
            true => $"Installed for logon only and started (Task Scheduler reports: {verified.Status ?? "ready"}).",
            false => "Windows reported success but does not list the task afterwards — check Task Scheduler for 'LiveClaude Supervisor'.",
            _ => "Installed for logon only and started; this account cannot read the task back to confirm."
        };

        return Combine(
            outcome +
            " The elevation prompt was declined or cancelled, so there is no boot trigger: the servers start at " +
            "sign-in, not before. Run this again and accept the prompt to add it.",
            note);
    });

    public Task UninstallTaskAsync() => RunAsync(result => TaskResult = result, async () =>
    {
        await ScheduledTaskInstaller.EndAsync();
        var result = await ScheduledTaskInstaller.UninstallAsync();

        if (result.Success)
            return "Task removed.";

        if (!ProcessHelper.IsElevated)
        {
            var exitCode = await TryElevatedAsync("uninstall-task");
            if (exitCode == 0)
                return "Task removed.";
        }

        return result.Combined;
    });

    public Task StartTaskAsync() =>
        RunAsync(result => TaskResult = result, async () => (await ScheduledTaskInstaller.RunAsync()).Combined);

    public Task StopTaskAsync() =>
        RunAsync(result => TaskResult = result, async () => (await ScheduledTaskInstaller.EndAsync()).Combined);

    /// <summary>Service management needs elevation, so it goes through the supervisor exe with UAC.</summary>
    public Task InstallServiceAsync(string? password) => RunAsync(result => ServiceResult = result, async () =>
    {
        // Windows never lets a user account log on as a service with a blank password, so there is
        // no point elevating first and failing with 1069 afterwards.
        if (!string.IsNullOrWhiteSpace(Account) && string.IsNullOrEmpty(password))
        {
            return $"Enter the Windows password for {Account}. A service cannot log on with a blank password " +
                   "(error 1069). Leave the account empty to install it as LocalSystem instead — which usually " +
                   "cannot reach the Claude Code sign-in.";
        }

        var (command, note) = await PrepareAsync(asService: true);

        var args = new List<string>
        {
            "install-service",
            "--exe", command.ExecutablePath,
            "--args", command.Arguments
        };

        if (!string.IsNullOrWhiteSpace(Account))
        {
            args.Add("--account");
            args.Add(Account);
            args.Add("--password");
            args.Add(password ?? "");
        }

        int exitCode;
        try
        {
            var elevator = SupervisorLauncher.CanRunStandalone(SupervisorPath) ? SupervisorPath : command.ExecutablePath;
            exitCode = await ProcessHelper.RunElevatedAsync(elevator, args);
        }
        catch (Win32Exception)
        {
            return "The elevation prompt was declined, so the service was not installed.";
        }

        if (exitCode != 0)
            return $"Service installation returned exit code {exitCode}.";

        // Same rule as the task: confirm with the service control manager before claiming success.
        var state = WindowsServiceInstaller.Query();
        return state.Installed
            ? Combine($"Service installed ({state.Status ?? "created"}).", note)
            : "The installer reported success but the service is not registered. What it did:" +
              Environment.NewLine + ReadInstallLog();
    });

    public Task UninstallServiceAsync() => RunAsync(result => ServiceResult = result, async () =>
    {
        var exitCode = await TryElevatedAsync("uninstall-service");
        return exitCode == 0 ? "Service removed." : $"Uninstall returned exit code {exitCode}.";
    });

    public Task StartServiceAsync() => RunAsync(result => ServiceResult = result, async () =>
    {
        var start = await WindowsServiceInstaller.StartAsync();
        if (start.Success)
            return "Service started.";

        // Starting needs elevation too; retry through the supervisor before giving up.
        if (!ProcessHelper.IsElevated && await TryElevatedAsync("start-service") == 0)
            return "Service started.";

        return WindowsServiceInstaller.Explain(start);
    });

    public Task StopServiceAsync() => RunAsync(result => ServiceResult = result, async () =>
    {
        var stop = await WindowsServiceInstaller.StopAsync();
        if (stop.Success)
            return "Service stopped.";

        if (!ProcessHelper.IsElevated && await TryElevatedAsync("stop-service") == 0)
            return "Service stopped.";

        return WindowsServiceInstaller.Explain(stop);
    });

    private async Task<int> TryElevatedAsync(params string[] arguments)
    {
        try
        {
            return await ProcessHelper.RunElevatedAsync(SupervisorPath, arguments);
        }
        catch (Win32Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// The last lines an install command printed. Those commands run elevated, in a process the user
    /// never sees, so this is the only way the reason for a failure reaches the screen.
    /// </summary>
    private static string ReadInstallLog(int lines = 8)
    {
        try
        {
            var log = new RollingLogWriter(LiveClaude.Service.SupervisorCli.LogPath, maxSizeMb: 2);
            var tail = log.Tail(lines);
            log.Dispose();

            return tail.Count == 0
                ? $"(nothing was written to {LiveClaude.Service.SupervisorCli.LogPath})"
                : string.Join(Environment.NewLine, tail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(the install log could not be read)";
        }
    }

    private static string Combine(string message, string? note) =>
        string.IsNullOrWhiteSpace(note) ? message : $"{message} {note}";

    private async Task RunAsync(Action<string> report, Func<Task<string>> action)
    {
        Busy = true;
        try
        {
            report(await action());
        }
        catch (Exception ex)
        {
            report(ex.Message);
        }
        finally
        {
            Busy = false;
            await RefreshAsync();
        }
    }
}
