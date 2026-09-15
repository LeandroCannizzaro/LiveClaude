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

    private async void OnInstallTask(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.InstallTaskAsync();
    }

    private async void OnRemoveTask(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UninstallTaskAsync();
    }

    private async void OnStartTask(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.StartTaskAsync();
    }

    private async void OnStopTask(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.StopTaskAsync();
    }

    private async void OnInstallService(object sender, RoutedEventArgs e)
    {
        if (Shell is null)
            return;

        var password = ServicePassword.Password;
        var asLocalSystem = false;

        if (string.IsNullOrEmpty(password) && !string.IsNullOrWhiteSpace(Shell.Hosting.Account))
        {
            var proceed = MessageBox.Show(
                $"No password entered for {Shell.Hosting.Account}.\n\n" +
                "Windows never lets an account log on as a service without one, so the service would be created " +
                "as LocalSystem instead — which uses a different profile and usually cannot reach the Claude Code " +
                "sign-in.\n\nInstall as LocalSystem anyway?",
                "Install service",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (proceed != MessageBoxResult.Yes)
                return;

            asLocalSystem = true;
        }

        await Shell.Hosting.InstallServiceAsync(password, asLocalSystem);
        ServicePassword.Clear();
        await Shell.ConnectAsync(force: true);
    }

    private async void OnRemoveService(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.UninstallServiceAsync();
    }

    private async void OnStartService(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.StartServiceAsync();
    }

    private async void OnStopService(object sender, RoutedEventArgs e)
    {
        if (Shell is not null)
            await Shell.Hosting.StopServiceAsync();
    }

    private void OnUseInstall(object sender, RoutedEventArgs e)
    {
        if (Shell is not null && sender is Button { Tag: string path })
            Shell.ClaudePath = path;
    }
}
