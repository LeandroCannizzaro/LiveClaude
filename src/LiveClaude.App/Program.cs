using System.IO;
using Avalonia;
using LiveClaude.Abstractions;

namespace LiveClaude.App;

/// <summary>
/// The entry point.
///
/// Three modes live behind the same executable, and only the FIRST argument selects one. Scanning the
/// whole list once made <c>install-autostart … --args --supervise</c> — the command that registers
/// the host — start a supervisor instead of installing anything, and exit 0 as if it had worked.
/// </summary>
public static class Program
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveClaude",
        "app-errors.log");

    [STAThread]
    public static int Main(string[] args)
    {
        // Attached before anything else: a failure in the headless modes below has nowhere else to be
        // reported, and losing it means an autostart host that dies with no explanation.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) => Log(e.Exception);

        try
        {
            _ = PlatformLoader.Current;
        }
        catch (PlatformNotAvailableException ex)
        {
            // A package built for another operating system. One line, not a stack trace.
            Console.Error.WriteLine(ex.Message);
            Log(ex);
            return 69;
        }

        var mode = args.FirstOrDefault();
        var asService = string.Equals(mode, "--service", StringComparison.OrdinalIgnoreCase);

        // A ClickOnce install does not ship the runtime configuration of the supervisor executable, so
        // that executable cannot start there. This one always can, and hosts the supervisor itself
        // when the registered host launches it with --supervise or --service.
        if (asService || string.Equals(mode, "--supervise", StringComparison.OrdinalIgnoreCase))
            return RunSupervisor(args, asService);

        // The same install/uninstall verbs as the supervisor executable: the app re-launches itself
        // elevated with one of these when it is the only executable that can run here.
        if (Service.SupervisorCli.IsVerb(mode))
            return RunCommandLine(args);

        return RunDesktop(args);
    }

    private static int RunDesktop(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log(ex);
            throw;
        }
    }

    /// <summary>Runs the supervisor in this process, with no window.</summary>
    private static int RunSupervisor(string[] args, bool asService)
    {
        try
        {
            Service.SupervisorHost.RunAsync(args, asService).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Log(ex);
            return 1;
        }
    }

    /// <summary>Runs one install/uninstall command and exits with its exit code.</summary>
    private static int RunCommandLine(string[] args)
    {
        try
        {
            PlatformLoader.Current.AttachToParentConsole();
            return Service.SupervisorCli.RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log(ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // Referenced by name by the Avalonia design-time tooling; do not rename.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>Records an exception the app recovered from, without interrupting the user.</summary>
    public static string Log(Exception exception)
    {
        var details = $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath, details);
        }
        catch (IOException)
        {
            // nothing else we can do
        }

        return details;
    }

    public static string LogPath => CrashLogPath;
}
