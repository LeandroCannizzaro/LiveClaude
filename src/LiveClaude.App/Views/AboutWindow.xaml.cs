using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using LiveClaude.App.ViewModels;

namespace LiveClaude.App.Views;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/LeandroCannizzaro/LiveClaude";
    private const string ProductPageUrl = "https://leandrocannizzaro.github.io/LiveClaude/";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"version {AppInfo.Version}";
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => ShellViewModel.OpenUrl(RepositoryUrl);

    private void OnOpenProductPage(object sender, RoutedEventArgs e) => ShellViewModel.OpenUrl(ProductPageUrl);

    private void OnOpenLicense(object sender, RoutedEventArgs e) => ShellViewModel.OpenUrl($"{RepositoryUrl}/blob/main/LICENSE");

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Version of the running build, as shown in the footer and in the About window.</summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the build metadata the SDK appends (1.0.4+abc1234).
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown";
        }
        catch (IOException)
        {
            // fall through
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
