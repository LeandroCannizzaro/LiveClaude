using System.Windows.Controls;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App.Views;

public partial class EnvironmentsView : UserControl
{
    private bool _loaded;

    public EnvironmentsView()
    {
        InitializeComponent();

        // Loaded fires on every tab switch; only the first one should hit the API.
        IsVisibleChanged += async (_, _) =>
        {
            if (!IsVisible || _loaded)
                return;

            _loaded = true;
            if (DataContext is ShellViewModel shell)
                await shell.Environments.RefreshAsync();
        };
    }
}
