using System.Windows;
using System.Windows.Controls;

namespace LiveClaude.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void OnAttachTerminal(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
            MainWindow.Current?.AttachTerminal(id);
    }
}
