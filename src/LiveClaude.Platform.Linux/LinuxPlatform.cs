using System.Diagnostics;
using System.Runtime.InteropServices;
using LiveClaude.Abstractions;
using LiveClaude.Platform.Linux.Autostart;
using LiveClaude.Platform.Posix;
using LiveClaude.Platform.Posix.Ipc;
using LiveClaude.Platform.Posix.Pty;

[assembly: LiveClaudePlatform(typeof(LiveClaude.Platform.Linux.LinuxPlatform))]

namespace LiveClaude.Platform.Linux;

/// <summary>Linux: ptys, systemd, polkit, XDG.</summary>
public sealed class LinuxPlatform : IPlatform
{
    private readonly LinuxPathLayout _paths = new();

    public LinuxPlatform()
    {
        Processes = new PosixProcessLauncher();
        Elevation = new PosixElevator();
        Ipc = new UnixSocketEndpointFactory(_paths);
        UserAutostart = new SystemdUserProvider(Processes, Elevation);
        SystemAutostart = new SystemdSystemProvider(Processes);
    }

    public string Id => "linux";

    public string DisplayName => $"Linux ({RuntimeInformation.OSDescription.Trim()})";

    public IPtyFactory Pty { get; } = new PosixPtyFactory();

    public IPathLayout Paths => _paths;

    public IIpcEndpointFactory Ipc { get; }

    public IIpcEndpointFactory CreateIpcEndpoint(string name) => new UnixSocketEndpointFactory(_paths, name);

    public IProcessLauncher Processes { get; }

    public IPrivilegeElevator Elevation { get; }

    public IAutostartProvider UserAutostart { get; }

    public IAutostartProvider? SystemAutostart { get; }

    public IClaudeDiscovery Claude { get; } = new PosixClaudeDiscovery();

    public IShellIntegration Shell { get; } = new LinuxShellIntegration();

    public ISupervisorDeployment Deployment { get; } = new PosixSupervisorDeployment();

    /// <summary>
    /// Nothing to do. A POSIX process inherits the terminal it was started from, so a CLI command
    /// already prints where it was typed — the Windows dance exists only because the supervisor has
    /// to be a windowed executable there.
    /// </summary>
    public void AttachToParentConsole()
    {
    }
}

/// <summary>
/// Opening things the way the desktop environment would.
///
/// <c>xdg-open</c> has no notion of "reveal this file", so the file manager is asked directly over
/// D-Bus first — every major one implements org.freedesktop.FileManager1 — and the folder is opened
/// as a fallback. Being one folder away is better than not opening at all.
/// </summary>
public sealed class LinuxShellIntegration : IShellIntegration
{
    public void Reveal(string path)
    {
        if (File.Exists(path) && TryShowInFileManager(path))
            return;

        var target = File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
        Open(target);
    }

    public void OpenUrl(string url) => Open(url);

    private static bool TryShowInFileManager(string path)
    {
        try
        {
            var uri = new Uri(path).AbsoluteUri;

            using var process = Process.Start(new ProcessStartInfo("dbus-send")
            {
                ArgumentList =
                {
                    "--session",
                    "--dest=org.freedesktop.FileManager1",
                    "--type=method_call",
                    "/org/freedesktop/FileManager1",
                    "org.freedesktop.FileManager1.ShowItems",
                    $"array:string:{uri}",
                    "string:"
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            });

            if (process is null)
                return false;

            return process.WaitForExit(3000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UriFormatException)
        {
            return false;
        }
    }

    private static void Open(string target)
    {
        using var process = Process.Start(new ProcessStartInfo("xdg-open")
        {
            ArgumentList = { target },
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }
}
