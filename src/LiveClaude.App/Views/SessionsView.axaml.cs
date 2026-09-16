using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveClaude.App.ViewModels;
using LiveClaude.Core.Claude;

namespace LiveClaude.App.Views;

public partial class SessionsView : UserControl
{
    public SessionsView() => InitializeComponent();

    private SessionEditorViewModel? Editor => (DataContext as ShellViewModel)?.Editor;

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (Editor is null)
            return;

        var folder = await Dialogs.PickFolderAsync("Choose the project directory", Editor.Directory);
        if (folder is null)
            return;

        Editor.Directory = folder;

        if (string.IsNullOrWhiteSpace(Editor.Name))
            Editor.Name = new DirectoryInfo(folder).Name;

        Editor.RefreshPreview();
    }

    private void OnUpdatePreview(object? sender, RoutedEventArgs e) => Editor?.RefreshPreview();

    /// <summary>
    /// Claude Code asks for workspace trust the first time it runs in a directory, and that prompt
    /// only appears in a real terminal — so run it in the embedded one.
    /// </summary>
    private async void OnTrustDirectory(object? sender, RoutedEventArgs e)
    {
        if (Editor is null || !await ValidateDirectoryAsync(Editor.Directory))
            return;

        MainWindow.Current?.OpenInteractiveTerminal(Editor.Directory);
    }

    /// <summary>Runs the configured server once, interactively, to answer the one-time y/n confirmation.</summary>
    private async void OnRunOnce(object? sender, RoutedEventArgs e)
    {
        if (Editor is null || !await ValidateDirectoryAsync(Editor.Directory))
            return;

        var arguments = ClaudeArgs.BuildRemoteControl(Editor.ToConfig()).ToArray();
        MainWindow.Current?.OpenInteractiveTerminal(Editor.Directory, arguments);
    }

    private static async Task<bool> ValidateDirectoryAsync(string directory)
    {
        if (Directory.Exists(directory))
            return true;

        await Dialogs.ShowWarningAsync("Choose an existing project directory first.");
        return false;
    }
}
