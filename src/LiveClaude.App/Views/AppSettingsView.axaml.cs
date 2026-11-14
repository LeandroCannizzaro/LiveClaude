using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveClaude.App.ViewModels;
using LiveClaude.Core.Products;

namespace LiveClaude.App.Views;

public partial class AppSettingsView : UserControl
{
    public AppSettingsView()
    {
        InitializeComponent();
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void OnUseClaudeInstall(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null && sender is Button { Tag: string path })
            Shell.ClaudePath = path;
    }

    private void OnUseCursorInstall(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null && sender is Button { Tag: string path })
            Shell.CursorPath = path;
    }

    private void OnUseAntigravityInstall(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null && sender is Button { Tag: string path })
            Shell.AntigravityPath = path;
    }

    private async void OnSignInClaude(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Login.StartLoginAsync(ProductIds.Claude);
    }

    private async void OnSignInCursor(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Login.StartLoginAsync(ProductIds.Cursor);
    }

    private async void OnSignInAntigravity(object? sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Login.StartLoginAsync(ProductIds.Antigravity);
    }
}
