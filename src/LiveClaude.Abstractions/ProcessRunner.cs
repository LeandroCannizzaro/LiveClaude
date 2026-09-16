using System.Diagnostics;
using System.Text;

namespace LiveClaude.Abstractions;

/// <summary>
/// Running a child process and collecting its output is the same everywhere, so the platform
/// implementations of <see cref="IProcessLauncher"/> share this rather than each carrying a copy.
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    /// <summary>
    /// Runs a command that is allowed to be missing — <c>pkexec</c>, <c>xdg-open</c>, <c>loginctl</c>
    /// are not on every machine — and reports that as a failed result instead of throwing.
    /// </summary>
    public static async Task<ProcessResult> TryRunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(fileName, arguments, workingDirectory, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new ProcessResult(127, "", $"Could not run '{fileName}': {ex.Message}");
        }
    }

    /// <summary>True when the program can be found and started at all.</summary>
    public static bool Exists(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, fileName)))
                    return true;
            }
            catch (ArgumentException)
            {
                // a malformed PATH entry is not worth failing over
            }
        }

        return false;
    }
}
