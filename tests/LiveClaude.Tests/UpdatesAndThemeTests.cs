using System.Runtime.InteropServices;
using LiveClaude.Abstractions;
using LiveClaude.App.Services;
using LiveClaude.Core.Config;
using LiveClaude.Core.Model;
using LiveClaude.Core.Supervision;
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
    public void UpdateCheckerSelectsPackageForAllOperatingSystems()
    {
        var msi = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-x64.msi"), "abc", 100);
        var zip = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-win-x64-installer.zip"), "def", 200);
        var linuxX64 = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-linux-x64.tar.gz"), "ghi", 300);
        var linuxArm64 = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-linux-arm64.tar.gz"), "jkl", 310);
        var osxX64 = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-osx-x64.dmg"), "mno", 400);
        var osxArm64 = new UpdatePackage(new Uri("https://example.com/LiveClaude-2.2.0-osx-arm64.dmg"), "pqr", 410);
        var manifest = new UpdateManifest(
            "2.2.0",
            Msi: msi,
            Zip: zip,
            LinuxX64: linuxX64,
            LinuxArm64: linuxArm64,
            OsxX64: osxX64,
            OsxArm64: osxArm64);

        Assert.Null(UpdateChecker.Evaluate(manifest, new Version(2, 2, 0, 0), InstallKind.Msi));
        Assert.Equal(msi, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.Msi)?.Package);
        Assert.Equal(zip, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.ZipPerUser)?.Package);
        Assert.Equal(linuxX64, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.LinuxTarball, Architecture.X64)?.Package);
        Assert.Equal(linuxArm64, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.LinuxTarball, Architecture.Arm64)?.Package);
        Assert.Equal(osxX64, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.MacOsDmg, Architecture.X64)?.Package);
        Assert.Equal(osxArm64, UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.MacOsDmg, Architecture.Arm64)?.Package);
        Assert.Null(UpdateChecker.Evaluate(manifest, new Version(2, 1, 3, 0), InstallKind.None)?.Package);
    }

    [Fact]
    public void InstallKindDetectorDistinguishesWindowsLinuxMacOsAndDevFolders()
    {
        var programFiles = Path.Combine(Path.GetTempPath(), "PF");
        var msiDir = Path.Combine(programFiles, "LiveClaude");
        var userZipDir = Path.Combine(Path.GetTempPath(), "LocalAppData", "LiveClaude");

        Assert.Equal(InstallKind.Msi, InstallKindDetector.Detect(msiDir, msiMarker: true, programFiles, _ => false));
        Assert.Equal(InstallKind.ZipAllUsers, InstallKindDetector.Detect(msiDir, msiMarker: false, programFiles, p => p.EndsWith("uninstall.ps1", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(InstallKind.ZipPerUser, InstallKindDetector.Detect(userZipDir, msiMarker: false, programFiles, p => p.EndsWith("uninstall.ps1", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(InstallKind.None, InstallKindDetector.Detect(userZipDir, msiMarker: false, programFiles, _ => false));

        Assert.Equal(InstallKind.LinuxTarball, InstallKindDetector.Detect("/opt/liveclaude", false, "", _ => false, isWindows: false, isLinux: true, isMacOs: false));
        Assert.Equal(InstallKind.None, InstallKindDetector.Detect("/home/user/src/bin/Debug/net10.0", false, "", _ => false, isWindows: false, isLinux: true, isMacOs: false));

        Assert.Equal(InstallKind.MacOsDmg, InstallKindDetector.Detect("/Applications/LiveClaude.app/Contents/MacOS", false, "", _ => false, isWindows: false, isLinux: false, isMacOs: true));
        Assert.Equal(InstallKind.None, InstallKindDetector.Detect("/Users/user/src/bin/Release/net10.0", false, "", _ => false, isWindows: false, isLinux: false, isMacOs: true));
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
        Assert.Equal(10, ThemeService.Presets.Count);
        Assert.True(ThemeService.GetPreset("Solarized Light").IsLight);
        Assert.False(ThemeService.GetPreset("Dracula").IsLight);
    }

    [Fact]
    public async Task SupervisorSaveSettingsPersistsTheme()
    {
        var tempDir = Directory.CreateTempSubdirectory("liveclaude-supervisor-theme-");
        try
        {
            var store = new ConfigStore(Path.Combine(tempDir.FullName, "config.json"));
            store.Save(new AppConfig());

            await using var supervisor = new Supervisor(store);
            var updated = store.Load();
            updated.Backoff.MaxRestarts = 5;
            updated.Theme = new ThemeSettings
            {
                PresetName = "Dracula",
                CustomColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AccentColor"] = "#FF79C6"
                }
            };

            await supervisor.SaveSettingsAsync(updated);

            var reloaded = store.Load();
            Assert.Equal(5, reloaded.Backoff.MaxRestarts);
            Assert.Equal("Dracula", reloaded.Theme.PresetName);
            Assert.Equal("#FF79C6", reloaded.Theme.CustomColors["AccentColor"]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
