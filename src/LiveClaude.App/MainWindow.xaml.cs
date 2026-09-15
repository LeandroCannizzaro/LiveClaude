using System.ComponentModel;
using System.Windows;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        Shell = new ShellViewModel();
        DataContext = Shell;

        Loaded += async (_, _) =>
        {
            await Shell.ConnectAsync();
            HandleStartupArguments();
        };
    }

    /// <summary>
    /// <c>LiveClaude.exe --terminal &lt;directory&gt;</c> opens straight into the embedded terminal for
    /// that folder — the quickest route to the one-time workspace-trust prompt.
    /// </summary>
    private void HandleStartupArguments()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, "--terminal", StringComparison.OrdinalIgnoreCase));

        if (index >= 0 && index + 1 < args.Length && System.IO.Directory.Exists(args[index + 1]))
            OpenInteractiveTerminal(args[index + 1]);
    }

    public static MainWindow? Current { get; private set; }

    public ShellViewModel Shell { get; }

    /// <summary>
    /// Opens the Terminal tab and runs the CLI interactively in <paramref name="directory"/>, which is
    /// how the one-time workspace trust dialog and the Remote Control confirmation get answered.
    /// </summary>
    public void OpenInteractiveTerminal(string directory, string[]? arguments = null)
    {
        Shell.SelectedTabIndex = 2;
        TerminalTab.StartLocal(directory, arguments ?? []);
    }

    /// <summary>Opens the Terminal tab attached to a supervised instance's live pseudo console.</summary>
    public async void AttachTerminal(string instanceId)
    {
        Shell.SelectedTabIndex = 2;
        await TerminalTab.AttachAsync(instanceId);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        TerminalTab.Shutdown();
        await Shell.DisposeAsync();
    }
}
