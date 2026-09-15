using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LiveClaude.App;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveClaude",
        "app-errors.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Attached before anything else: a failure in the headless modes below has nowhere else to
        // be reported, and losing it means a scheduled task that dies with no explanation.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) => Log(args.Exception);

        // A ClickOnce install does not ship the runtime configuration of LiveClaude.Service.exe, so
        // that executable cannot start there. This one always can, and hosts the supervisor itself
        // when the scheduled task or the service launches it with --supervise / --service.
        var asService = e.Args.Any(a => a.Equals("--service", StringComparison.OrdinalIgnoreCase));
        var supervise = asService || e.Args.Any(a => a.Equals("--supervise", StringComparison.OrdinalIgnoreCase));

        if (supervise)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                Log(args.Exception);
                args.Handled = true;
            };

            RunSupervisor(e.Args, asService);
            return;
        }

        // The same install/uninstall verbs as the supervisor executable: the app re-launches itself
        // elevated with one of these when it is the only executable that can run here.
        if (LiveClaude.Service.SupervisorCli.IsVerb(e.Args.FirstOrDefault()))
        {
            RunCommandLine(e.Args);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    /// <summary>Records an exception the app recovered from, without interrupting the user.</summary>
    public static void LogError(Exception exception) => Log(exception);

    /// <summary>Runs one install/uninstall command and exits with its exit code.</summary>
    private void RunCommandLine(string[] args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _ = Task.Run(async () =>
        {
            var exitCode = 1;
            try
            {
                LiveClaude.Core.Hosting.ConsoleBridge.AttachToParent();
                exitCode = await LiveClaude.Service.SupervisorCli.RunAsync(args);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
            finally
            {
                Dispatcher.Invoke(() => Shutdown(exitCode));
            }
        });
    }

    /// <summary>
    /// Runs the supervisor in this process, with no window. The host keeps running until it is
    /// stopped, so the application must not shut down when it has no windows open.
    /// </summary>
    private void RunSupervisor(string[] args, bool asService)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _ = Task.Run(async () =>
        {
            try
            {
                await LiveClaude.Service.SupervisorHost.RunAsync(args, asService);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
            finally
            {
                Dispatcher.Invoke(Shutdown);
            }
        });
    }

    private static void ShowError(Exception exception)
    {
        var details = Log(exception);

        MessageBox.Show(
            $"{exception.Message}\n\nDetails were written to:\n{CrashLogPath}",
            "LiveClaude",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        _ = details;
    }

    private static string Log(Exception exception)
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
}
