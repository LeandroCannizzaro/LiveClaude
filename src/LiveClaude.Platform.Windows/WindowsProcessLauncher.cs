using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using LiveClaude.Abstractions;

namespace LiveClaude.Platform.Windows;

public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public string CurrentUserName => WindowsIdentity.GetCurrent().Name;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default) =>
        ProcessRunner.RunAsync(fileName, arguments, workingDirectory, ct);

    public string FormatCommandLine(string executable, IEnumerable<string> arguments) =>
        WindowsCommandLine.Build(executable, arguments);

    /// <summary>
    /// Runs a program with a command line built by the caller.
    ///
    /// sc.exe parses its own command line and rejects the quoting .NET applies to
    /// <see cref="ProcessStartInfo.ArgumentList"/>: <c>sc create X "binPath= …"</c> comes back as a
    /// usage error (1639). Its options have to be passed exactly as they would be typed, which is
    /// why this stays Windows-only rather than joining <see cref="IProcessLauncher"/>.
    /// </summary>
    public static async Task<ProcessResult> RunRawAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

/// <summary>Re-launches an executable through UAC and waits for it.</summary>
public sealed class WindowsElevator : IPrivilegeElevator
{
    public bool IsAvailable => true;

    public string Mechanism => "UAC";

    public async Task<int> RunElevatedAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = false
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{fileName}' elevated.");

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return process.ExitCode;
    }
}

public sealed class WindowsShellIntegration : IShellIntegration
{
    public void Reveal(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
