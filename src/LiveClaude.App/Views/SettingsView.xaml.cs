using System.Windows;
using System.Windows.Controls;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (Shell is not null)
                await Shell.Hosting.RefreshAsync();
        };
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private async void OnInstallUserHost(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UserHost.InstallAsync();
    }

    private async void OnRemoveUserHost(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UserHost.UninstallAsync();
    }

    private async void OnStartUserHost(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UserHost.StartAsync();
    }

    private async void OnStopUserHost(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UserHost.StopAsync();
    }

    private async void OnInstallSystemHost(object sender, RoutedEventArgs e)
    {
        if (Shell?.Hosting.SystemHost is not { } host)
            return;

        var password = host.RequiresPassword ? ServicePassword.Password : null;
        var useSystemAccount = false;

        // Asking here rather than letting the install fail: on Windows an account with no password is
        // refused with error 1069 *after* the elevation prompt, which reads like a bug.
        if (host.RequiresPassword && string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(Shell.Hosting.Account))
        {
            var proceed = MessageBox.Show(
                $"No password entered for {Shell.Hosting.Account}.\n\n" +
                $"{host.DisplayName} cannot log on without one, so it would be installed under the system account " +
                "instead — which uses a different profile and usually cannot reach the Claude Code sign-in.\n\n" +
                "Install under the system account anyway?",
                $"Install {host.DisplayName.ToLowerInvariant()}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (proceed != MessageBoxResult.Yes)
                return;

            useSystemAccount = true;
        }

        await host.InstallAsync(password, useSystemAccount);
        ServicePassword.Clear();
        await Shell.ConnectAsync(force: true);
    }

    private async void OnRemoveSystemHost(object sender, RoutedEventArgs e)
    {
        if (Shell?.Hosting.SystemHost is { } host)
            await host.UninstallAsync();
    }

    private async void OnStartSystemHost(object sender, RoutedEventArgs e)
    {
        if (Shell?.Hosting.SystemHost is { } host)
            await host.StartAsync();
    }

    private async void OnStopSystemHost(object sender, RoutedEventArgs e)
    {
        if (Shell?.Hosting.SystemHost is { } host)
            await host.StopAsync();
    }

    private void OnUseInstall(object sender, RoutedEventArgs e)
    {
        if (Shell is not null && sender is Button { Tag: string path })
            Shell.ClaudePath = path;
    }
}
