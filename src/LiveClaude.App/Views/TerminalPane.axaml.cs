using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveClaude.App.ViewModels;
using LiveClaude.Core.Claude;
using LiveClaude.Terminal.Controls;

namespace LiveClaude.App.Views;

public partial class TerminalPane : UserControl
{
    private LocalTerminalSession? _local;
    private string? _attachedInstanceId;
    private bool _wired;

    public TerminalPane()
    {
        InitializeComponent();
        Loaded += (_, _) => WireTerminal();
    }





    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void WireTerminal()
    {
        if (_wired)
            return;

        _wired = true;
        Terminal.Input += OnTerminalInput;
        Terminal.GridSizeChanged += OnGridSizeChanged;
    }

    /// <summary>Streams a supervised instance's pseudo terminal into this view, in both directions.</summary>
    public async Task AttachAsync(string instanceId)
    {
        var api = Shell?.Api;
        if (api is null)
            return;

        await DetachAsync();
        StopLocal();

        _attachedInstanceId = instanceId;
        api.TerminalOutput += OnRemoteOutput;

        var buffered = await api.AttachAsync(instanceId);
        Terminal.Clear();
        if (!string.IsNullOrEmpty(buffered))
            Terminal.Write(buffered);

        await api.ResizeAsync(instanceId, Terminal.Columns, Terminal.Rows);

        var name = Shell?.Instances.FirstOrDefault(i => i.Id == instanceId)?.Name ?? instanceId;
        StatusText.Text = $"Attached to '{name}' — typing goes straight to the server.";
        Terminal.Focus();
    }

    public async Task DetachAsync()
    {
        var api = Shell?.Api;
        if (api is null || _attachedInstanceId is null)
            return;

        api.TerminalOutput -= OnRemoteOutput;

        try
        {
            await api.DetachAsync(_attachedInstanceId);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            // the supervisor may already be gone
        }

        _attachedInstanceId = null;
        StatusText.Text = "Not attached.";
    }

    /// <summary>Runs the CLI locally in this window: the workspace-trust and confirmation flows.</summary>
    public async void StartLocal(string directory, string[] arguments)
    {
        await DetachAsync();
        StopLocal();
        WireTerminal();

        var config = Shell?.Config;
        var install = ClaudeLocator.Locate(config?.ClaudePath);
        if (install is null)
        {
            Terminal.Write("\r\n\x1b[31mClaude CLI not found. Set its path in the 'Service & startup' tab.\x1b[0m\r\n");
            return;
        }

        DirectoryBox.Text = directory;
        Terminal.Clear();

        _local = new LocalTerminalSession(Terminal);
        _local.Exited += _ => Dispatcher.UIThread.Post(() => StatusText.Text = "Local process exited.");
        _local.Start(install.Path, arguments, directory);

        StatusText.Text = arguments.Length == 0
            ? $"Running the CLI in {directory} — accept the trust prompt, then type /exit."
            : $"Running 'claude {string.Join(' ', arguments)}' in {directory}.";

        Terminal.Focus();
    }

    public void Shutdown()
    {
        StopLocal();
        _ = DetachAsync();
    }

    private void StopLocal()
    {
        _local?.Dispose();
        _local = null;
    }

    private void OnRemoteOutput(string instanceId, string data)
    {
        if (instanceId != _attachedInstanceId)
            return;

        Dispatcher.UIThread.Post(() => Terminal.Write(data));
    }

    private async void OnTerminalInput(string text)
    {
        // A local session receives its input directly from LocalTerminalSession.
        if (_local is { IsRunning: true })
            return;

        var api = Shell?.Api;
        if (api is null || _attachedInstanceId is null)
            return;

        try
        {
            await api.SendInputAsync(_attachedInstanceId, text);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            StatusText.Text = "Lost the connection to the supervisor.";
        }
    }

    private async void OnGridSizeChanged(int columns, int rows)
    {
        var api = Shell?.Api;
        if (api is null || _attachedInstanceId is null)
            return;

        try
        {
            await api.ResizeAsync(_attachedInstanceId, columns, rows);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            // resize is best effort
        }
    }

    private async void OnInstancePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (InstancePicker.SelectedItem is InstanceViewModel instance)
        {
            DirectoryBox.Text = instance.Directory;
            await AttachAsync(instance.Id);
        }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.PickFolderAsync("Choose a directory", DirectoryBox.Text) is { } folder)
            DirectoryBox.Text = folder;
    }

    private async void OnOpenClaude(object? sender, RoutedEventArgs e)
    {
        var directory = (DirectoryBox.Text ?? "").Trim();

        if (!Directory.Exists(directory))
        {
            await Dialogs.ShowWarningAsync("Choose an existing directory first.");
            return;
        }

        StartLocal(directory, []);
    }

    private void OnStop(object? sender, RoutedEventArgs e)
    {
        StopLocal();
        _ = DetachAsync();
        StatusText.Text = "Stopped.";
    }

    private void OnClear(object? sender, RoutedEventArgs e) => Terminal.Clear();

    private void OnCopy(object? sender, RoutedEventArgs e) => Terminal.CopyScreen();

    private void OnPaste(object? sender, RoutedEventArgs e) => Terminal.Paste();

    private void OnSendCtrlC(object? sender, RoutedEventArgs e)
    {
        // Both the local session and the remote attachment listen on TerminalView.Input.
        Terminal.Focus();
        Terminal.SendInput("\x03");
    }
}
