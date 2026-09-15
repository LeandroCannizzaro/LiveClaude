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

    private void OnShowAbout(object sender, RoutedEventArgs e)
    {
        // A decorative dialog must never be able to take the supervisor's window down with it.
        try
        {
            new Views.AboutWindow { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            MessageBox.Show(
                $"The About window could not be opened.\n\n{ex.Message}",
                "LiveClaude",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // async void: anything thrown here reaches the dispatcher, and shutting down is not worth
        // an error dialog in the user's face.
        try
        {
            TerminalTab.Shutdown();
            await Shell.DisposeAsync();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }
}
