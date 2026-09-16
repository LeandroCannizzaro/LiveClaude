using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveClaude.App.ViewModels;
using LiveClaude.App.Views;

namespace LiveClaude.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Current = this;
        Shell = new ShellViewModel();
        DataContext = Shell;

        Opened += async (_, _) =>
        {
            await Shell.ConnectAsync();
            HandleStartupArguments();
        };
    }

    public static MainWindow? Current { get; private set; }

    public ShellViewModel Shell { get; }

    /// <summary>
    /// <c>LiveClaude --terminal &lt;directory&gt;</c> opens straight into the embedded terminal for
    /// that folder — the quickest route to the one-time workspace-trust prompt.
    /// </summary>
    private void HandleStartupArguments()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(args, a => string.Equals(a, "--terminal", StringComparison.OrdinalIgnoreCase));

        if (index >= 0 && index + 1 < args.Length && Directory.Exists(args[index + 1]))
            OpenInteractiveTerminal(args[index + 1]);
    }

    /// <summary>
    /// Opens the Terminal tab and runs the CLI interactively in <paramref name="directory"/>, which is
    /// how the one-time workspace trust prompt and the Remote Control confirmation get answered.
    /// </summary>
    public void OpenInteractiveTerminal(string directory, string[]? arguments = null)
    {
        Shell.SelectedTabIndex = 2;
        TerminalTab.StartLocal(directory, arguments ?? []);
    }

    /// <summary>Opens the Terminal tab attached to a supervised instance's live pseudo terminal.</summary>
    public async void AttachTerminal(string instanceId)
    {
        Shell.SelectedTabIndex = 2;

        // async void: this is an event-handler path, and a failure here belongs in the log rather
        // than on the dispatcher.
        try
        {
            await TerminalTab.AttachAsync(instanceId);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }

    private async void OnShowAbout(object? sender, RoutedEventArgs e)
    {
        // A decorative dialog must never be able to take the supervisor's window down with it.
        try
        {
            await new AboutWindow().ShowDialog(this);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            await Dialogs.ShowWarningAsync($"The About window could not be opened.\n\n{ex.Message}");
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        try
        {
            TerminalTab.Shutdown();
            await Shell.DisposeAsync();
        }
        catch (Exception ex)
        {
            Program.Log(ex);
        }
    }
}
