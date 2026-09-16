using System.Runtime.InteropServices;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Windows.Autostart;
using LiveClaude.Platform.Windows.Deployment;
using LiveClaude.Platform.Windows.Ipc;
using LiveClaude.Platform.Windows.Pty;

[assembly: LiveClaudePlatform(typeof(LiveClaude.Platform.Windows.WindowsPlatform))]

namespace LiveClaude.Platform.Windows;

/// <summary>Windows 10 1809+ and Windows 11: ConPTY, Task Scheduler, the service control manager.</summary>
public sealed class WindowsPlatform : IPlatform
{
    public string Id => "windows";

    public string DisplayName => RuntimeInformation.OSDescription;

    public IPtyFactory Pty { get; } = new WindowsPtyFactory();

    public IPathLayout Paths { get; } = new WindowsPathLayout();

    public IIpcEndpointFactory Ipc { get; } = new WindowsIpcEndpointFactory();

    public IIpcEndpointFactory CreateIpcEndpoint(string name) => new WindowsIpcEndpointFactory(name);

    public IProcessLauncher Processes { get; } = new WindowsProcessLauncher();

    public IPrivilegeElevator Elevation { get; } = new WindowsElevator();

    public IAutostartProvider UserAutostart { get; } = new ScheduledTaskProvider();

    public IAutostartProvider? SystemAutostart { get; } = new WindowsServiceProvider();

    public IClaudeDiscovery Claude { get; } = new WindowsClaudeDiscovery();

    public IShellIntegration Shell { get; } = new WindowsShellIntegration();

    public ISupervisorDeployment Deployment { get; } = new WindowsSupervisorDeployment();

    public void AttachToParentConsole() => ConsoleBridge.AttachToParent(allocateIfMissing: true);
}
