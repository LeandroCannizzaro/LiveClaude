using System.ComponentModel;
using System.IO;
using LiveClaude.Core.Hosting;

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
    /// The executable and arguments to register. A ClickOnce install cannot start
    /// LiveClaude.Service.exe (its runtime configuration is not deployed), so there the desktop
    /// application hosts the supervisor itself.
    /// </summary>
    private SupervisorCommand ResolveCommand(bool asService) =>
        SupervisorLauncher.Resolve(Path.GetDirectoryName(SupervisorPath)!, asService);

    public async Task RefreshAsync()
    {
        var service = WindowsServiceInstaller.Query();
        ServiceStatus = service.Installed ? service.Status ?? "installed" : "not installed";

        var task = await ScheduledTaskInstaller.QueryAsync();
        TaskStatus = task.Installed ? task.Status ?? "installed" : "not installed";
    }

    /// <summary>
    /// Installs the scheduled task. The boot trigger can only be registered by an administrator, so
    /// this asks for elevation; declining falls back to a logon-only task, which works as a normal
    /// user and covers everything except starting before sign-in.
    /// </summary>
    public Task InstallTaskAsync() => RunAsync(result => TaskResult = result, async () =>
    {
        var command = ResolveCommand(asService: false);
        var note = Combine(SupervisorDeployment.DescribeDeployment(SupervisorPath) ?? "", command.Note ?? "").Trim();

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
            return Combine("Installed with the logon and boot triggers, and started.", note);

        var fallback = await ScheduledTaskInstaller.InstallAsync(
            command.ExecutablePath, runAtBoot: false, userName: ProcessHelper.CurrentUserName, arguments: command.Arguments);
        if (!fallback.Success)
        {
            return ScheduledTaskInstaller.IsAccessDenied(fallback)
                ? $"Windows refused the task: {fallback.Combined.Trim()} Your account may be blocked from creating scheduled tasks by policy; the Windows service below is the alternative."
                : $"Could not install the task: {fallback.Combined}";
        }

        await ScheduledTaskInstaller.RunAsync();
        return Combine(
            "Installed without the boot trigger and started. It runs at every sign-in; " +
            "registering the boot trigger needs administrator rights, so run this again and accept the prompt " +
            "if you want the servers up before anyone signs in.",
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
        var command = ResolveCommand(asService: true);
        var note = Combine(SupervisorDeployment.DescribeDeployment(SupervisorPath) ?? "", command.Note ?? "").Trim();

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

        return exitCode == 0
            ? Combine("Service installed and started.", note)
            : $"Service installation returned exit code {exitCode}.";
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
