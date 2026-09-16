using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            if (AutoRefresh.IsChecked == true && IsEffectivelyVisible)
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

            // Put the caret at the end and let the TextBox scroll to it: Avalonia has no ScrollToEnd
            // on TextBox, and following a growing log from the top is useless.
            LogBox.CaretIndex = text.Length;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            LogBox.Text = $"Could not read the logs: {ex.Message}";
        }
    }

    private async void OnInstancePicked(object? sender, SelectionChangedEventArgs e) => await LoadAsync();

    private async void OnRefresh(object? sender, RoutedEventArgs e) => await LoadAsync();
}
