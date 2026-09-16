using Avalonia.Controls;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App.Views;

public partial class EnvironmentsView : UserControl
{
    private bool _loaded;

    public EnvironmentsView()
    {
        InitializeComponent();

        // Avalonia's TabControl only realises the selected tab's content, so Loaded fires the first
        // time this tab is actually looked at — which is when it should reach the Anthropic API, and
        // not before. The guard keeps later tab switches from doing it again.
        Loaded += async (_, _) =>
        {
            if (_loaded)
                return;

            _loaded = true;

            if (DataContext is ShellViewModel shell)
                await shell.Environments.RefreshAsync();
        };
    }
}
