using System.Diagnostics;
using LiveClaude.Abstractions;
using LiveClaude.Core.Config;
using LiveClaude.Core.Logging;

namespace LiveClaude.Service;

/// <summary>
/// The install/uninstall/status commands. Shared, because on a ClickOnce install the desktop
/// application is the only executable that can run: it re-launches itself elevated with these same
/// verbs instead of the supervisor executable.
///
/// The verbs are platform-neutral — <c>install-autostart --scope user|system</c> — because what they
/// register differs per OS. <c>install-task</c> and <c>install-service</c> survive as aliases so the
/// Windows documentation, the winget package and anyone's scripts keep working.
/// </summary>
public static class SupervisorCli
{
    public static readonly string[] Verbs =
    [
        "install-autostart", "uninstall-autostart", "start-autostart", "stop-autostart", "status",
        // Windows-era aliases, kept deliberately.
        "install-service", "uninstall-service", "start-service", "stop-service",
        "install-task", "uninstall-task"
    ];

    public static bool IsVerb(string? candidate) =>
        candidate is not null && Verbs.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the output of an install run is kept, since it usually happens out of sight.</summary>
    public static string LogPath => Path.Combine(ConfigStore.LogDirectory, "install.log");

    private static IPlatform Platform => PlatformLoader.Current;

    /// <summary>
    /// Runs a command and records everything it printed. These commands normally run elevated, in a
    /// window nobody sees, so without this a failure leaves no trace at all.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ConfigStore.EnsureDirectories();

        using var log = new RollingLogWriter(LogPath, maxSizeMb: 2);
        var original = Console.Out;
        var tee = new TeeTextWriter(original, log);
        Console.SetOut(tee);
        Console.SetError(tee);

        log.Write($"--- {string.Join(' ', Redact(args))} (platform: {Platform.Id}, elevated: {Platform.Processes.IsElevated}, user: {Platform.Processes.CurrentUserName})");

        try
        {
            var exitCode = await ExecuteAsync(args).ConfigureAwait(false);
            tee.Flush();
            log.Write($"--- exit code {exitCode}");
            return exitCode;
        }
        catch (Exception ex)
        {
            tee.Flush();
            log.Write($"--- failed: {ex}");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>The password must never reach a log file.</summary>
    private static IEnumerable<string> Redact(string[] args)
    {
        var redactNext = false;

        foreach (var arg in args)
        {
            if (redactNext)
            {
                yield return "***";
                redactNext = false;
                continue;
            }

            redactNext = arg.Equals("--password", StringComparison.OrdinalIgnoreCase);
            yield return arg;
        }
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        var verb = args.FirstOrDefault()?.ToLowerInvariant();

        return verb switch
        {
            "install-autostart" => await InstallAsync(args, ScopeFrom(args, AutostartScope.User)),
            "uninstall-autostart" => await UninstallAsync(ScopeFrom(args, AutostartScope.User)),
            "start-autostart" => await ControlAsync(ScopeFrom(args, AutostartScope.User), start: true),
            "stop-autostart" => await ControlAsync(ScopeFrom(args, AutostartScope.User), start: false),

            "install-service" => await InstallAsync(args, AutostartScope.System),
            "uninstall-service" => await UninstallAsync(AutostartScope.System),
            "start-service" => await ControlAsync(AutostartScope.System, start: true),
            "stop-service" => await ControlAsync(AutostartScope.System, start: false),
            "install-task" => await InstallAsync(args, AutostartScope.User),
            "uninstall-task" => await UninstallAsync(AutostartScope.User),

            "status" => await ShowStatusAsync(),
            _ => 64
        };
    }

    private static AutostartScope ScopeFrom(string[] args, AutostartScope fallback) =>
        GetOption(args, "--scope")?.ToLowerInvariant() switch
        {
            "system" => AutostartScope.System,
            "user" => AutostartScope.User,
            _ => fallback
        };

    /// <summary>Returns the provider for a scope, or null with a message when the platform has none.</summary>
    private static IAutostartProvider? Provider(AutostartScope scope)
    {
        if (scope == AutostartScope.User)
            return Platform.UserAutostart;

        var system = Platform.SystemAutostart;
        if (system is null)
            Console.Error.WriteLine($"{Platform.DisplayName} has no system-wide autostart host. Use --scope user.");

        return system;
    }

    private static async Task<int> InstallAsync(string[] args, AutostartScope scope)
    {
        var provider = Provider(scope);
        if (provider is null)
            return 64;

        var options = new AutostartOptions
        {
            // --no-boot is the Windows spelling and stays; --no-start-before-sign-in reads better
            // everywhere else and means the same thing.
            StartBeforeSignIn = !args.Contains("--no-boot", StringComparer.OrdinalIgnoreCase) &&
                                !args.Contains("--no-start-before-sign-in", StringComparer.OrdinalIgnoreCase),

            // When this runs elevated, the elevation prompt may have been answered with a different
            // administrator account; --user keeps the registration owned by the person whose session
            // the servers actually run in. --account is the Windows-service spelling of the same thing.
            UserName = GetOption(args, "--user") ?? GetOption(args, "--account"),
            Password = GetOption(args, "--password")
        };

        if (provider.RequiresElevation(options) && !Platform.Processes.IsElevated)
        {
            Console.Error.WriteLine(
                $"Installing {provider.DisplayName} with these options needs administrator rights " +
                $"({Platform.Elevation.Mechanism}).");
            return 5;
        }

        WarnAboutAccount(scope, options);

        var command = ResolveTarget(args, scope);
        var result = await provider.InstallAsync(command, options, CancellationToken.None);
        Console.WriteLine(result.Message);

        if (!result.Success)
            return 1;

        if (command.Note is { Length: > 0 } note)
            Console.WriteLine(note);

        var start = await provider.StartAsync(CancellationToken.None);
        Console.WriteLine(start.Message);

        Console.WriteLine(start.Success
            ? $"{provider.DisplayName} installed and started."
            : $"{provider.DisplayName} installed, but it did not start.");

        return start.Success ? 0 : 1;
    }

    /// <summary>
    /// The one thing that goes wrong on every platform: running the supervisor as an account that is
    /// not the user Claude Code signed in as. Its credentials live in that user's profile.
    /// </summary>
    private static void WarnAboutAccount(AutostartScope scope, AutostartOptions options)
    {
        if (scope != AutostartScope.System || !string.IsNullOrWhiteSpace(options.UserName))
            return;

        Console.WriteLine("No account given, so the supervisor will run as the system account.");
        Console.WriteLine($"Claude Code credentials live in the user profile ({Platform.Claude.CredentialSourceDescription}),");
        Console.WriteLine($"so that account usually cannot sign in. Recommended: --user \"{Platform.Processes.CurrentUserName}\".");
    }

    private static async Task<int> UninstallAsync(AutostartScope scope)
    {
        var provider = Provider(scope);
        if (provider is null)
            return 64;

        if (provider.RequiresElevation(new AutostartOptions()) && !Platform.Processes.IsElevated)
        {
            Console.Error.WriteLine($"Removing {provider.DisplayName} needs administrator rights.");
            return 5;
        }

        await provider.StopAsync(CancellationToken.None);
        var result = await provider.UninstallAsync(CancellationToken.None);
        Console.WriteLine(result.Success ? $"{provider.DisplayName} removed. {result.Message}".Trim() : result.Message);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> ControlAsync(AutostartScope scope, bool start)
    {
        var provider = Provider(scope);
        if (provider is null)
            return 64;

        var result = start
            ? await provider.StartAsync(CancellationToken.None)
            : await provider.StopAsync(CancellationToken.None);

        if (result.Success)
        {
            Console.WriteLine(start ? $"{provider.DisplayName} started." : $"{provider.DisplayName} stopped.");
            return 0;
        }

        Console.Error.WriteLine(result.Message);
        return 1;
    }

    private static async Task<int> ShowStatusAsync()
    {
        var store = new ConfigStore();
        var config = store.Load();
        var user = await Platform.UserAutostart.QueryAsync();

        var rows = new List<(string Label, string Value)>
        {
            ("Platform", Platform.DisplayName),
            ("Config", store.Path),
            ("Sessions", config.Sessions.Count.ToString()),
            ("Endpoint", Platform.Ipc.EndpointDescription),
            (Platform.UserAutostart.DisplayName, user.Describe())
        };

        if (Platform.SystemAutostart is { } system)
            rows.Add((system.DisplayName, (await system.QueryAsync()).Describe()));

        rows.Add(("Logs", ConfigStore.LogDirectory));

        // The labels are platform names ("Scheduled task", "systemd user unit", "LaunchAgent"), so
        // the column width cannot be a constant.
        var width = rows.Max(r => r.Label.Length);
        foreach (var (label, value) in rows)
            Console.WriteLine($"{label.PadRight(width)} : {value}");

        return 0;
    }

    /// <summary>
    /// What to register. Defaults to this executable, and <c>--exe</c> / <c>--args</c> let the caller
    /// register the desktop application instead, which is what a ClickOnce install needs.
    /// </summary>
    private static SupervisorCommand ResolveTarget(string[] args, AutostartScope scope)
    {
        var exe = GetOption(args, "--exe");
        var arguments = GetOption(args, "--args");

        if (!string.IsNullOrWhiteSpace(exe))
        {
            return new SupervisorCommand(exe, string.IsNullOrWhiteSpace(arguments)
                ? scope == AutostartScope.System ? ServiceIdentity.ServiceArgument : ServiceIdentity.SuperviseArgument
                : arguments);
        }

        return Platform.Deployment.ResolveCommand(AppContext.BaseDirectory, scope);
    }

    public static string? GetOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static string CurrentExecutablePath()
    {
        var path = Environment.ProcessPath;

        // Environment.ProcessPath is the host (dotnet) when the app is run through it; on Windows the
        // ".exe" test caught that, and a generic "is it our own executable?" test does elsewhere.
        if (!string.IsNullOrWhiteSpace(path) &&
            !Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Process.GetCurrentProcess().MainModule?.FileName
               ?? Path.Combine(AppContext.BaseDirectory, PlatformLoader.Current.Deployment.SupervisorExecutable);
    }
}
