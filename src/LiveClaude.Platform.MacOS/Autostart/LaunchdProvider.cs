using System.Security;
using LiveClaude.Abstractions;

namespace LiveClaude.Platform.MacOS.Autostart;

/// <summary>
/// The supervisor as a launchd job. A LaunchAgent runs in the user's GUI session; a LaunchDaemon runs
/// from boot, before anyone signs in. Both are a property list plus a <c>launchctl</c> call.
/// </summary>
public abstract class LaunchdProvider : IAutostartProvider
{
    protected const string Label = "com.leandrocannizzaro.liveclaude";

    private readonly IProcessLauncher _processes;

    protected LaunchdProvider(IProcessLauncher processes)
    {
        _processes = processes;
    }

    public abstract string Kind { get; }

    public abstract string DisplayName { get; }

    public abstract AutostartScope Scope { get; }

    public abstract string Summary { get; }

    public abstract bool SupportsAccount { get; }

    public abstract string PlistPath { get; }

    public abstract bool RequiresElevation(AutostartOptions options);

    public abstract string? StartBeforeSignInRequirement { get; }

    /// <summary>launchd names an account; it never asks for its password.</summary>
    public bool RequiresPassword => false;

    /// <summary>The domain a job belongs to: <c>gui/&lt;uid&gt;</c> for an agent, <c>system</c> for a daemon.</summary>
    protected abstract string Domain { get; }

    protected string ServiceTarget => $"{Domain}/{Label}";

    public async Task<AutostartState> QueryAsync(CancellationToken ct = default)
    {
        var result = await _processes.RunAsync("launchctl", ["print", ServiceTarget], ct: ct).ConfigureAwait(false);

        if (!result.Success)
        {
            var text = result.Combined;

            // 113 / "Could not find service" is launchd's way of saying it is not loaded. The plist
            // may still be on disk, which is a real state: installed but never bootstrapped.
            if (text.Contains("Could not find service", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("No such process", StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(PlistPath)
                    ? new AutostartState(true, "installed, not loaded", null,
                        $"{PlistPath} exists but launchd has not bootstrapped it. Install again, or reboot.")
                    : new AutostartState(false, null, null);
            }

            if (text.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Could not find domain", StringComparison.OrdinalIgnoreCase))
            {
                return new AutostartState(null, null, null,
                    "launchd will not describe this job from here. Reading another user's domain, or the " +
                    "system domain without root, is not permitted; the job itself is unaffected.");
            }

            return new AutostartState(null, null, null, text.Trim());
        }

        return new AutostartState(true, DescribeState(result.StandardOutput), ReadRunAs(result.StandardOutput));
    }

    public async Task<AutostartResult> InstallAsync(SupervisorCommand command, AutostartOptions options, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
            await File.WriteAllTextAsync(PlistPath, BuildPlist(command, options), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AutostartResult.Failed($"Could not write {PlistPath}: {ex.Message}");
        }

        // Replacing rather than adding: bootstrapping a label that is already loaded fails with
        // "service already loaded", and an upgrade must not have to be uninstalled first.
        await _processes.RunAsync("launchctl", ["bootout", ServiceTarget], ct: ct).ConfigureAwait(false);

        var bootstrap = await _processes.RunAsync("launchctl", ["bootstrap", Domain, PlistPath], ct: ct).ConfigureAwait(false);

        if (!bootstrap.Success)
            return AutostartResult.Failed(Explain(bootstrap));

        // KeepAlive restarts the job, but it does not start it the first time in every macOS version.
        await _processes.RunAsync("launchctl", ["enable", ServiceTarget], ct: ct).ConfigureAwait(false);

        return AutostartResult.Ok($"Written to {PlistPath} and loaded.");
    }

    public async Task<AutostartResult> UninstallAsync(CancellationToken ct = default)
    {
        await _processes.RunAsync("launchctl", ["bootout", ServiceTarget], ct: ct).ConfigureAwait(false);

        try
        {
            if (File.Exists(PlistPath))
                File.Delete(PlistPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AutostartResult.Failed($"Could not remove {PlistPath}: {ex.Message}");
        }

        return AutostartResult.Ok($"Removed {PlistPath}.");
    }

    public async Task<AutostartResult> StartAsync(CancellationToken ct = default)
    {
        var result = await _processes.RunAsync("launchctl", ["kickstart", ServiceTarget], ct: ct).ConfigureAwait(false);
        return result.Success ? AutostartResult.Ok("Started.") : AutostartResult.Failed(Explain(result));
    }

    public async Task<AutostartResult> StopAsync(CancellationToken ct = default)
    {
        // "kill SIGINT" rather than bootout: the supervisor gets to stop its servers cleanly, and the
        // job stays loaded so it comes back at the next login. bootout would unload it entirely.
        var result = await _processes.RunAsync("launchctl", ["kill", "SIGINT", ServiceTarget], ct: ct).ConfigureAwait(false);
        return result.Success ? AutostartResult.Ok("Stopped.") : AutostartResult.Failed(Explain(result));
    }

    protected static string Explain(ProcessResult result)
    {
        var text = result.Combined;

        if (text.Contains("Bootstrap failed: 5", StringComparison.Ordinal) ||
            text.Contains("Input/output error", StringComparison.OrdinalIgnoreCase))
        {
            return "launchd refused to load the job (bootstrap failed: 5). That usually means the plist is " +
                   "malformed, or a job with this label is still loaded — try removing it and installing again.";
        }

        if (text.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase))
        {
            return "Operation not permitted. Installing a LaunchDaemon needs root, and macOS also requires " +
                   "Full Disk Access for a background job that reads files in your home directory — grant it " +
                   "in System Settings › Privacy & Security.";
        }

        if (text.Contains("Load failed: 5", StringComparison.Ordinal))
            return "launchd could not load the job (5): check the executable path in the plist.";

        return text.Trim();
    }

    /// <summary>Pulls the interesting line out of launchctl print, which is otherwise pages long.</summary>
    private static string DescribeState(string output)
    {
        var state = Find(output, "state = ");
        var pid = Find(output, "pid = ");
        var lastExit = Find(output, "last exit code = ");

        if (state is null)
            return "loaded";

        if (pid is not null)
            return $"{state} (pid {pid})";

        return lastExit is not null and not "0" ? $"{state}, last exit code {lastExit}" : state;
    }

    private static string? ReadRunAs(string output) => Find(output, "username = ");

    private static string? Find(string output, string key)
    {
        var index = output.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var start = index + key.Length;
        var end = output.IndexOf('\n', start);
        return (end < 0 ? output[start..] : output[start..end]).Trim();
    }

    /// <summary>
    /// The job description. <c>KeepAlive</c> is launchd's "restart it when it dies" and the
    /// counterpart of Restart=always; the throttle interval matches systemd's RestartSec.
    /// </summary>
    protected virtual string BuildPlist(SupervisorCommand command, AutostartOptions options)
    {
        var account = SupportsAccount && !string.IsNullOrWhiteSpace(options.UserName)
            ? $"    <key>UserName</key>\n    <string>{Escape(options.UserName.Trim())}</string>\n"
            : "";

        var environment = PathLayout.Override is { Length: > 0 } root
            ? $"""
                   <key>EnvironmentVariables</key>
                   <dict>
                     <key>{PathLayout.RootOverrideVariable}</key>
                     <string>{Escape(root)}</string>
                   </dict>

               """
            : "";

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "LiveClaude");

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
              <dict>
                <key>Label</key>
                <string>{Label}</string>
                <key>ProgramArguments</key>
                <array>
                  <string>{Escape(command.ExecutablePath)}</string>
                  <string>{Escape(command.Arguments)}</string>
                </array>
                <key>WorkingDirectory</key>
                <string>{Escape(Path.GetDirectoryName(command.ExecutablePath) ?? "/")}</string>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <true/>
                <key>ThrottleInterval</key>
                <integer>5</integer>
                <key>ProcessType</key>
                <string>Background</string>
                <key>ExitTimeOut</key>
                <integer>90</integer>
            {account}{environment}    <key>StandardOutPath</key>
                <string>{Escape(Path.Combine(logDirectory, "launchd.out.log"))}</string>
                <key>StandardErrorPath</key>
                <string>{Escape(Path.Combine(logDirectory, "launchd.err.log"))}</string>
              </dict>
            </plist>

            """;
    }

    protected static string Escape(string value) => SecurityElement.Escape(value) ?? value;
}
