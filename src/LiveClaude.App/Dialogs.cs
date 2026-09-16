using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace LiveClaude.App;

/// <summary>What a confirmation came back with.</summary>
public enum DialogResult
{
    Ok,
    Yes,
    No,
    Cancel
}

/// <summary>
/// The dialogs the application needs.
///
/// Avalonia has no MessageBox and no synchronous folder picker, which is the honest position — both
/// block a UI thread — so these are async and the call sites await them. The message window is built
/// in code rather than in XAML because it is a handful of controls and having it as one method keeps
/// every caller's wording in one place.
/// </summary>
public static class Dialogs
{
    /// <summary>The window to parent a dialog to, or null before the main window exists.</summary>
    public static Window? Owner =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

    public static Task ShowInfoAsync(string message, string title = "LiveClaude") =>
        ShowAsync(message, title, DialogKind.Info);

    public static Task ShowWarningAsync(string message, string title = "LiveClaude") =>
        ShowAsync(message, title, DialogKind.Warning);

    public static Task ShowErrorAsync(string message, string title = "LiveClaude") =>
        ShowAsync(message, title, DialogKind.Error);

    /// <summary>Asks a yes/no question. Returns true only for an explicit yes.</summary>
    public static async Task<bool> ConfirmAsync(string message, string title = "LiveClaude") =>
        await ShowAsync(message, title, DialogKind.Question, yesNo: true) == DialogResult.Yes;

    private enum DialogKind
    {
        Info,
        Warning,
        Error,
        Question
    }

    private static async Task<DialogResult> ShowAsync(string message, string title, DialogKind kind, bool yesNo = false)
    {
        var result = yesNo ? DialogResult.No : DialogResult.Ok;

        var accent = kind switch
        {
            DialogKind.Error => "DangerBrush",
            DialogKind.Warning => "WarningBrush",
            _ => "AccentBrush"
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

        if (yesNo)
        {
            buttons.Children.Add(MakeButton("No", () =>
            {
                result = DialogResult.No;
                window.Close();
            }));

            buttons.Children.Add(MakeButton("Yes", () =>
            {
                result = DialogResult.Yes;
                window.Close();
            }, primary: true));
        }
        else
        {
            buttons.Children.Add(MakeButton("OK", () =>
            {
                result = DialogResult.Ok;
                window.Close();
            }, primary: true));
        }

        var stripe = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 2, 14, 2),
            Background = FindBrush(accent)
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        };

        window.Content = new Border
        {
            Classes = { "card" },
            Margin = new Thickness(20),
            Padding = new Thickness(22, 20, 22, 18),
            Child = new StackPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { stripe, text }
                    },
                    buttons
                }
            }
        };

        // Escape closes with the safe answer, which for a yes/no is "no".
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                window.Close();
        };

        if (Owner is { } owner)
            await window.ShowDialog(owner);
        else
            await ShowStandaloneAsync(window);

        return result;
    }

    /// <summary>
    /// Shows a dialog with no main window to parent to — which happens when something fails during
    /// startup, exactly when losing the message would be worst.
    /// </summary>
    private static Task ShowStandaloneAsync(Window window)
    {
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        return closed.Task;
    }

    private static Button MakeButton(string content, Action onClick, bool primary = false)
    {
        var button = new Button { Content = content, MinWidth = 88 };

        if (primary)
            button.Classes.Add("primary");

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// Looks a palette brush up by key. The theme is fixed to dark, so there is no variant to track
    /// and a one-off lookup is enough — a dialog does not outlive a theme change.
    /// </summary>
    private static IBrush? FindBrush(string key) =>
        Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var value) == true
            ? value as IBrush
            : null;

    /// <summary>
    /// Asks for a directory. Returns null when the user cancelled, or when there is no window to host
    /// the picker.
    /// </summary>
    public static async Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        if (Owner is not { } owner)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(startIn) && Directory.Exists(startIn))
        {
            try
            {
                options.SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(startIn);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // A start location is a nicety; never fail the picker over it.
            }
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(options);

        // TryGetLocalPath returns null for a location with no file-system path — a cloud provider on
        // Windows, or a portal document on Linux — and the supervisor can only run in a real directory.
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
