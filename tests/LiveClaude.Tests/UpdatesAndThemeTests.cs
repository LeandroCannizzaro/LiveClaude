using LiveClaude.App.Services;
using LiveClaude.Core.Model;
using Xunit;

namespace LiveClaude.Tests;

public class UpdatesAndThemeTests
{
    [Fact]
    public void UpdateVersionsNormalizesAndParses()
    {
        Assert.Equal(new Version(2, 1, 3, 0), UpdateVersions.TryParse("v2.1.3+abc123"));
        Assert.Equal(new Version(2, 1, 3, 0), UpdateVersions.TryParse("2.1.3-beta.1"));
        Assert.Null(UpdateVersions.TryParse(""));
        Assert.Null(UpdateVersions.TryParse("not-a-version"));
    }

    [Fact]
    public void UpdateCheckerSelectsPackageForInstallKind()
    {
        var msi = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-x64.msi"), "abc", 100);
        var zip = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-win-x64-installer.zip"), "def", 200);
        var manifest = new UpdateManifest("2.2.0", Msi: msi, Zip: zip);

        Assert.Null(UpdateChecker.Evaluate(manifest, new Version(2, 2, 0, 0), InstallKind.Msi));
        Assert.Equal(msi, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.Msi)?.Package);
        Assert.Equal(zip, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.ZipPerUser)?.Package);
        Assert.Null(UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.None)?.Package);
    }

    [Fact]
    public void InstallKindDetectorDistinguishesMsiZipAndDevFolder()
    {
        var programFiles = Path.Combine(Path.GetTempPath(), "PF");
        var msiDir = Path.Combine(programFiles, "LiveClaude");
        var userZipDir = Path.Combine(Path.GetTempPath(), "LocalAppData", "LiveClaude");

        Assert.Equal(InstallKind.Msi, InstallKindDetector.Detect(msiDir, msiMarker: true, programFiles, _ => false));
        Assert.Equal(InstallKind.ZipAllUsers, InstallKindDetector.Detect(msiDir, msiMarker: false, programFiles, p => p.EndsWith("uninstall.ps1", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(InstallKind.ZipPerUser, InstallKindDetector.Detect(userZipDir, msiMarker: false, programFiles, p => p.EndsWith("uninstall.ps1", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(InstallKind.None, InstallKindDetector.Detect(userZipDir, msiMarker: false, programFiles, _ => false));
    }

    [Fact]
    public void ThemeServiceResolvesPresetsAndCustomOverrides()
    {
        var light = ThemeService.GetPreset("Light");
        Assert.True(light.IsLight);

        var settings = new ThemeSettings
        {
            PresetName = "Midnight Blue",
            CustomColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AccentColor"] = "#FF00AA",
                ["BorderColor"] = "not-a-color"
            }
        };

        var resolved = ThemeService.ResolveColors(settings);
        Assert.Equal("#FF00AA", resolved["AccentColor"]);
        Assert.Equal(ThemeService.GetPreset("Midnight Blue").Colors["BorderColor"], resolved["BorderColor"]);
    }
}
