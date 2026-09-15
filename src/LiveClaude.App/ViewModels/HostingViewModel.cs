using System.IO;
using LiveClaude.Core.Hosting;

namespace LiveClaude.App.ViewModels;

/// <summary>Drives the two supported hosts: the Windows service and the scheduled task.</summary>
public sealed class HostingViewModel : ObservableObject
{
    private string _serviceStatus = "unknown";
    private string _taskStatus = "unknown";
    private string _account = ProcessHelper.CurrentUserName;
    private string _lastResult = "";
    private bool _busy;

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

    public string LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public bool Busy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    /// <summary>Path of the supervisor executable shipped next to the app.</summary>
    public static string SupervisorPath
    {
        get
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "LiveClaude.Service.exe");
            return File.Exists(candidate) ? candidate : candidate;
        }
    }

    public async Task RefreshAsync()
    {
        var service = WindowsServiceInstaller.Query();
        ServiceStatus = service.Installed ? service.Status ?? "installed" : "not installed";

        var task = await ScheduledTaskInstaller.QueryAsync();
        TaskStatus = task.Installed ? task.Status ?? "installed" : "not installed";
    }

    public async Task InstallTaskAsync()
    {
        await RunAsync(async () =>
        {
            var result = await ScheduledTaskInstaller.InstallAsync(SupervisorPath);
            if (result.Success)
                await ScheduledTaskInstaller.RunAsync();
            return result.Combined;
        });
    }

    public Task UninstallTaskAsync() => RunAsync(async () =>
    {
        await ScheduledTaskInstaller.EndAsync();
        var result = await ScheduledTaskInstaller.UninstallAsync();
        return result.Combined;
    });

    public Task StartTaskAsync() => RunAsync(async () => (await ScheduledTaskInstaller.RunAsync()).Combined);

    public Task StopTaskAsync() => RunAsync(async () => (await ScheduledTaskInstaller.EndAsync()).Combined);

    /// <summary>Service management needs elevation, so it goes through the supervisor exe with UAC.</summary>
    public Task InstallServiceAsync(string? password) => RunAsync(async () =>
    {
        var args = new List<string> { "install-service" };
        if (!string.IsNullOrWhiteSpace(Account))
        {
            args.Add("--account");
            args.Add(Account);
            args.Add("--password");
            args.Add(password ?? "");
        }

        var exitCode = await ProcessHelper.RunElevatedAsync(SupervisorPath, args);
        return exitCode == 0
            ? "Service installed and started."
            : $"Service installation returned exit code {exitCode}.";
    });

    public Task UninstallServiceAsync() => RunAsync(async () =>
    {
        var exitCode = await ProcessHelper.RunElevatedAsync(SupervisorPath, ["uninstall-service"]);
        return exitCode == 0 ? "Service removed." : $"Uninstall returned exit code {exitCode}.";
    });

    public Task StartServiceAsync() => RunAsync(async () => (await WindowsServiceInstaller.StartAsync()).Combined);

    public Task StopServiceAsync() => RunAsync(async () => (await WindowsServiceInstaller.StopAsync()).Combined);

    private async Task RunAsync(Func<Task<string>> action)
    {
        Busy = true;
        try
        {
            LastResult = await action();
        }
        catch (Exception ex)
        {
            LastResult = ex.Message;
        }
        finally
        {
            Busy = false;
            await RefreshAsync();
        }
    }
}
