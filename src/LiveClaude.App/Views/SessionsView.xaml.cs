using System.IO;
using System.Windows;
using System.Windows.Controls;
using LiveClaude.App.ViewModels;
using LiveClaude.Core.Claude;
using Microsoft.Win32;

namespace LiveClaude.App.Views;

public partial class SessionsView : UserControl
{
    public SessionsView() => InitializeComponent();

    private SessionEditorViewModel? Editor => (DataContext as ShellViewModel)?.Editor;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (Editor is null)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose the project directory",
            InitialDirectory = Directory.Exists(Editor.Directory) ? Editor.Directory : ""
        };

        if (dialog.ShowDialog() != true)
            return;

        Editor.Directory = dialog.FolderName;

        if (string.IsNullOrWhiteSpace(Editor.Name))
            Editor.Name = new DirectoryInfo(dialog.FolderName).Name;

        Editor.RefreshPreview();
    }

    private void OnUpdatePreview(object sender, RoutedEventArgs e) => Editor?.RefreshPreview();

    /// <summary>
    /// Claude Code asks for workspace trust the first time it runs in a directory, and that dialog
    /// only appears in a real terminal — so run it in the embedded one.
    /// </summary>
    private void OnTrustDirectory(object sender, RoutedEventArgs e)
    {
        if (Editor is null || !ValidateDirectory(Editor.Directory))
            return;

        MainWindow.Current?.OpenInteractiveTerminal(Editor.Directory);
    }

    /// <summary>Runs the configured server once, interactively, to answer the one-time y/n confirmation.</summary>
    private void OnRunOnce(object sender, RoutedEventArgs e)
    {
        if (Editor is null || !ValidateDirectory(Editor.Directory))
            return;

        var arguments = ClaudeArgs.BuildRemoteControl(Editor.ToConfig()).ToArray();
        MainWindow.Current?.OpenInteractiveTerminal(Editor.Directory, arguments);
    }

    private static bool ValidateDirectory(string directory)
    {
        if (Directory.Exists(directory))
            return true;

        MessageBox.Show("Choose an existing project directory first.", "LiveClaude",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
