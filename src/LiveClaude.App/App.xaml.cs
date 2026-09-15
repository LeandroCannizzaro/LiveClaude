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

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ShowError(ex);
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
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
