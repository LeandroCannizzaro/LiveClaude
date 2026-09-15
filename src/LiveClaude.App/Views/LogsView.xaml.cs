using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App.Views;

public partial class LogsView : UserControl
{
    private readonly DispatcherTimer _timer;

    public LogsView()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) =>
        {
            if (AutoRefresh.IsChecked == true && IsVisible)
                await LoadAsync();
        };

        Loaded += async (_, _) =>
        {
            _timer.Start();
            await LoadAsync();
        };

        Unloaded += (_, _) => _timer.Stop();
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private async Task LoadAsync()
    {
        var api = Shell?.Api;
        var instance = InstancePicker.SelectedItem as InstanceViewModel ?? Shell?.SelectedInstance;

        if (api is null || instance is null)
            return;

        try
        {
            var lines = await api.GetLogTailAsync(instance.Id, 500);
            var text = string.Join(Environment.NewLine, lines);

            if (LogBox.Text == text)
                return;

            LogBox.Text = text;
            LogBox.ScrollToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            LogBox.Text = $"Could not read the logs: {ex.Message}";
        }
    }

    private async void OnInstancePicked(object sender, SelectionChangedEventArgs e) => await LoadAsync();

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();
}
