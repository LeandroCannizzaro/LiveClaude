using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveClaude.App.Services;

public sealed record UpdatePackage(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size);

public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt = null,
    [property: JsonPropertyName("notesUrl")] Uri? NotesUrl = null,
    [property: JsonPropertyName("msi")] UpdatePackage? Msi = null,
    [property: JsonPropertyName("zip")] UpdatePackage? Zip = null,
    [property: JsonPropertyName("linuxX64")] UpdatePackage? LinuxX64 = null,
    [property: JsonPropertyName("linuxArm64")] UpdatePackage? LinuxArm64 = null,
    [property: JsonPropertyName("osxX64")] UpdatePackage? OsxX64 = null,
    [property: JsonPropertyName("osxArm64")] UpdatePackage? OsxArm64 = null,
    [property: JsonPropertyName("osxX64Macos11")] UpdatePackage? OsxX64Macos11 = null,
    [property: JsonPropertyName("osxArm64Macos11")] UpdatePackage? OsxArm64Macos11 = null);

public enum InstallKind
{
    None,
    Msi,
    ZipPerUser,
    ZipAllUsers,
    LinuxTarball,
    MacOsDmg,
}

public sealed record AvailableUpdate(Version Version, UpdateManifest Manifest, UpdatePackage? Package);

public static class UpdateVersions
{
    public static Version Current()
    {
        var asm = typeof(UpdateVersions).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParse(info) is { } v)
            return v;

        var raw = asm.GetName().Version;
        return raw is null
            ? new Version(0, 0, 0, 0)
            : new Version(Math.Max(raw.Major, 0), Math.Max(raw.Minor, 0), Math.Max(raw.Build, 0), 0);
    }

    public static Version? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim().TrimStart('v', 'V');
        var plus = trimmed.IndexOf('+');
        if (plus >= 0)
            trimmed = trimmed[..plus];

        var dash = trimmed.IndexOf('-');
        if (dash >= 0)
            trimmed = trimmed[..dash];

        if (!Version.TryParse(trimmed, out var v))
            return null;

        return new Version(
            Math.Max(v.Major, 0),
            Math.Max(v.Minor, 0),
            Math.Max(v.Build, 0),
            0);
    }
}

public sealed class UpdateChecker
{
    public const string DefaultManifestUrl =
        "https://github.com/LeandroCannizzaro/LiveClaude/releases/latest/download/update.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Uri _manifestUrl;
    private readonly Version _current;
    private readonly InstallKind _kind;

    public UpdateChecker(HttpClient http, Uri manifestUrl, Version current, InstallKind kind)
    {
        _http = http;
        _manifestUrl = manifestUrl;
        _current = current;
        _kind = kind;
    }

    public static Uri ManifestUrl(string? overrideUrl = null)
    {
        var env = string.IsNullOrWhiteSpace(overrideUrl)
            ? Environment.GetEnvironmentVariable("LIVECLAUDE_UPDATE_MANIFEST_URL")
            : overrideUrl;

        return Uri.TryCreate(env, UriKind.Absolute, out var custom)
            ? custom
            : new Uri(DefaultManifestUrl);
    }

    public static Uri ReleaseNotesUrl(Uri? manifestUri = null)
    {
        _ = manifestUri;
        return new Uri("https://leandrocannizzaro.github.io/LiveClaude/release-notes.html");
    }

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(_manifestUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, ct)
            .ConfigureAwait(false);

        return Evaluate(manifest, _current, _kind);
    }

    public static AvailableUpdate? Evaluate(
        UpdateManifest? manifest,
        Version current,
        InstallKind kind,
        Architecture? arch = null,
        bool? isMacOs11 = null)
    {
        if (manifest is null)
            return null;

        var remote = UpdateVersions.TryParse(manifest.Version);
        if (remote is null)
            return null;

        var normalizedCurrent = new Version(
            Math.Max(current.Major, 0),
            Math.Max(current.Minor, 0),
            Math.Max(current.Build, 0),
            0);

        if (remote <= normalizedCurrent)
            return null;

        var targetArch = arch ?? RuntimeInformation.OSArchitecture;
        var macOs11 = isMacOs11 ?? (OperatingSystem.IsMacOS() && Environment.OSVersion.Version.Major < 12);
        var pkg = SelectPackage(manifest, kind, targetArch, macOs11);
        return new AvailableUpdate(remote, manifest, pkg);
    }

    private static UpdatePackage? SelectPackage(UpdateManifest manifest, InstallKind kind, Architecture arch, bool macOs11) =>
        kind switch
        {
            InstallKind.Msi => manifest.Msi,
            InstallKind.ZipPerUser or InstallKind.ZipAllUsers => manifest.Zip,
            InstallKind.LinuxTarball => arch == Architecture.Arm64 ? manifest.LinuxArm64 : manifest.LinuxX64,
            InstallKind.MacOsDmg when macOs11 => arch == Architecture.Arm64
                ? (manifest.OsxArm64Macos11 ?? manifest.OsxArm64)
                : (manifest.OsxX64Macos11 ?? manifest.OsxX64),
            InstallKind.MacOsDmg => arch == Architecture.Arm64 ? manifest.OsxArm64 : manifest.OsxX64,
            _ => null,
        };
}

public static class InstallKindDetector
{
    public static InstallKind Detect(string? baseDirectory = null)
    {
        var dir = (baseDirectory ?? AppContext.BaseDirectory).TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(dir))
            return InstallKind.None;

        var msiMarker = File.Exists(Path.Combine(dir, ".liveclaude-msi"))
                        || (!OperatingSystem.IsWindows() ? false : IsMsiFallback(dir));

        return Detect(
            dir,
            msiMarker,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            File.Exists,
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux(),
            OperatingSystem.IsMacOS());
    }

    private static bool IsMsiFallback(string dir)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return !string.IsNullOrEmpty(programFiles)
               && dir.StartsWith(programFiles.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
               && !File.Exists(Path.Combine(dir, "uninstall.ps1"));
    }

    public static InstallKind Detect(
        string baseDir,
        bool msiMarker,
        string programFiles,
        Func<string, bool> fileExists) =>
        Detect(baseDir, msiMarker, programFiles, fileExists, isWindows: true, isLinux: false, isMacOs: false);

    public static InstallKind Detect(
        string baseDir,
        bool msiMarker,
        string programFiles,
        Func<string, bool> fileExists,
        bool isWindows,
        bool isLinux,
        bool isMacOs)
    {
        var dir = baseDir.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(dir))
            return InstallKind.None;

        var normalized = dir.Replace('\\', '/');
        if (normalized.Contains("/bin/Debug/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/Release/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/bin/Debug", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/bin/Release", StringComparison.OrdinalIgnoreCase))
        {
            return InstallKind.None;
        }

        if (isWindows)
        {
            if (msiMarker)
                return InstallKind.Msi;

            var hasUninstall = fileExists(Path.Combine(dir, "uninstall.ps1"))
                               || fileExists(Path.Combine(dir, ".liveclaude-zip-install"));
            if (!hasUninstall)
                return InstallKind.None;

            if (!string.IsNullOrEmpty(programFiles) &&
                dir.StartsWith(programFiles.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return InstallKind.ZipAllUsers;

            return InstallKind.ZipPerUser;
        }

        if (isLinux)
            return InstallKind.LinuxTarball;

        if (isMacOs)
        {
            return normalized.Contains(".app/Contents/MacOS", StringComparison.OrdinalIgnoreCase)
                ? InstallKind.MacOsDmg
                : InstallKind.None;
        }

        return InstallKind.None;
    }
}

public static class UpdateInstaller
{
    public static string DownloadDirectory(Version version)
    {
        var dir = Path.Combine(Path.GetTempPath(), "LiveClaude-Update", version.ToString(3));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static async Task<string> DownloadAsync(
        HttpClient http,
        UpdatePackage package,
        string targetDir,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(package.Url.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "LiveClaude-Update";

        var destination = Path.Combine(targetDir, fileName);
        var temp = destination + ".part";

        using (var response = await http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? package.Size;

            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                readTotal += read;
                if (total > 0)
                    progress?.Report(Math.Clamp((double)readTotal / total, 0, 1));
            }
        }

        if (!string.IsNullOrWhiteSpace(package.Sha256))
        {
            await using var verifyStream = File.OpenRead(temp);
            var hashBytes = await SHA256.HashDataAsync(verifyStream, ct).ConfigureAwait(false);
            var actual = Convert.ToHexString(hashBytes).ToLowerInvariant();
            if (!string.Equals(actual, package.Sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                File.Delete(temp);
                throw new InvalidOperationException(
                    $"Downloaded update failed SHA-256 verification (expected {package.Sha256}, got {actual}).");
            }
        }

        File.Move(temp, destination, overwrite: true);
        return destination;
    }

    public static void Launch(
        InstallKind kind,
        string packagePath,
        string installDir,
        string exePath,
        int processId,
        string logPath)
    {
        var workDir = Path.GetDirectoryName(packagePath) ?? Path.GetTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());

        switch (kind)
        {
            case InstallKind.Msi:
            case InstallKind.ZipPerUser:
            case InstallKind.ZipAllUsers:
                LaunchWindows(kind, packagePath, installDir, exePath, processId, logPath, workDir);
                break;

            case InstallKind.LinuxTarball:
                LaunchLinux(packagePath, installDir, exePath, processId, logPath, workDir);
                break;

            case InstallKind.MacOsDmg:
                LaunchMacOs(packagePath, installDir, processId, logPath, workDir);
                break;

            default:
                throw new InvalidOperationException($"Auto-update is not supported for install kind {kind}.");
        }
    }

    private static void LaunchWindows(
        InstallKind kind,
        string packagePath,
        string installDir,
        string exePath,
        int processId,
        string logPath,
        string workDir)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.ps1");

        var script = kind == InstallKind.Msi
            ? $$"""
                param([int]$PidToWait, [string]$Pkg, [string]$Exe, [string]$Log)
                Start-Transcript -Path $Log -Append | Out-Null
                try {
                  while (Get-Process -Id $PidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
                  $p = Start-Process msiexec.exe -ArgumentList @('/i', "`"$Pkg`"", '/passive', '/norestart') -Wait -PassThru -Verb RunAs
                  if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
                    Start-Process $Exe
                  }
                } finally {
                  Stop-Transcript | Out-Null
                }
                """
            : $$"""
                param([int]$PidToWait, [string]$Pkg, [string]$Target, [string]$Exe, [string]$Log, [switch]$Elevate)
                Start-Transcript -Path $Log -Append | Out-Null
                try {
                  while (Get-Process -Id $PidToWait -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }
                  $stage = Join-Path ([IO.Path]::GetDirectoryName($Pkg)) 'stage'
                  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
                  Expand-Archive -Path $Pkg -DestinationPath $stage -Force
                  $payload = if (Test-Path (Join-Path $stage 'app\LiveClaude.exe')) { Join-Path $stage 'app' } else { $stage }
                  Copy-Item (Join-Path $payload '*') -Destination $Target -Recurse -Force
                  Start-Process $Exe
                } finally {
                  Stop-Transcript | Out-Null
                }
                """;

        File.WriteAllText(scriptPath, script);

        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(ps))
            ps = "powershell.exe";

        var psi = new ProcessStartInfo
        {
            FileName = ps,
            UseShellExecute = kind == InstallKind.ZipAllUsers,
            Verb = kind == InstallKind.ZipAllUsers ? "runas" : "",
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-PidToWait");
        psi.ArgumentList.Add(processId.ToString());
        psi.ArgumentList.Add("-Pkg");
        psi.ArgumentList.Add(packagePath);

        if (kind != InstallKind.Msi)
        {
            psi.ArgumentList.Add("-Target");
            psi.ArgumentList.Add(installDir.TrimEnd('\\', '/'));
        }

        psi.ArgumentList.Add("-Exe");
        psi.ArgumentList.Add(exePath);
        psi.ArgumentList.Add("-Log");
        psi.ArgumentList.Add(logPath);

        Process.Start(psi);
    }

    private static void LaunchLinux(
        string packagePath,
        string installDir,
        string exePath,
        int processId,
        string logPath,
        string workDir)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.sh");
        var target = installDir.TrimEnd('/');
        var script = """
            #!/bin/sh
            set -eu
            PID_TO_WAIT="$1"
            PKG="$2"
            TARGET="$3"
            EXE="$4"
            LOG="$5"

            exec >>"$LOG" 2>&1
            echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Starting Linux update"

            while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
              sleep 0.3
            done

            STAGE=$(mktemp -d)
            cleanup() { rm -rf "$STAGE"; }
            trap cleanup EXIT

            tar -xzf "$PKG" -C "$STAGE"
            if [ -f "$STAGE/LiveClaude" ]; then
              SRC="$STAGE"
            else
              SRC=$(find "$STAGE" -mindepth 1 -maxdepth 1 -type d | head -n 1)
            fi

            if [ -z "${SRC:-}" ] || [ ! -d "$SRC" ]; then
              echo "Could not locate extracted payload in $STAGE"
              exit 1
            fi

            if [ -w "$TARGET" ]; then
              cp -af "$SRC"/. "$TARGET"/
              chmod +x "$TARGET/LiveClaude" 2>/dev/null || true
            elif command -v pkexec >/dev/null 2>&1; then
              pkexec sh -c 'cp -af "$1"/. "$2"/ && chmod +x "$2/LiveClaude"' _ "$SRC" "$TARGET"
            elif command -v sudo >/dev/null 2>&1; then
              sudo -n sh -c 'cp -af "$1"/. "$2"/ && chmod +x "$2/LiveClaude"' _ "$SRC" "$TARGET"
            else
              echo "Target directory $TARGET is not writable and neither pkexec nor sudo is available."
              exit 1
            fi

            rm -f "$PKG"
            nohup "$EXE" >/dev/null 2>&1 &
            """;

        File.WriteAllText(scriptPath, script.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add(processId.ToString());
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add(exePath);
        psi.ArgumentList.Add(logPath);

        Process.Start(psi);
    }

    private static void LaunchMacOs(
        string packagePath,
        string installDir,
        int processId,
        string logPath,
        string workDir)
    {
        var scriptPath = Path.Combine(workDir, "apply-update.sh");
        var appBundle = ResolveMacAppBundle(installDir);
        var script = """
            #!/bin/sh
            set -eu
            PID_TO_WAIT="$1"
            PKG="$2"
            TARGET_APP="$3"
            LOG="$4"

            exec >>"$LOG" 2>&1
            echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Starting macOS update"

            while kill -0 "$PID_TO_WAIT" 2>/dev/null; do
              sleep 0.3
            done

            MOUNT_POINT=$(mktemp -d)
            cleanup() {
              hdiutil detach "$MOUNT_POINT" -quiet >/dev/null 2>&1 || hdiutil detach "$MOUNT_POINT" -force -quiet >/dev/null 2>&1 || true
              rmdir "$MOUNT_POINT" >/dev/null 2>&1 || true
            }
            trap cleanup EXIT

            hdiutil attach "$PKG" -nobrowse -quiet -mountpoint "$MOUNT_POINT"
            SRC_APP=$(find "$MOUNT_POINT" -maxdepth 2 -name "*.app" -print -quit)
            if [ -z "${SRC_APP:-}" ] || [ ! -d "$SRC_APP" ]; then
              echo "Could not locate .app bundle inside $PKG"
              exit 1
            fi

            PARENT_DIR=$(dirname "$TARGET_APP")
            if [ -w "$PARENT_DIR" ]; then
              rm -rf "$TARGET_APP"
              ditto "$SRC_APP" "$TARGET_APP"
              xattr -dr com.apple.quarantine "$TARGET_APP" >/dev/null 2>&1 || true
            else
              osascript -e "do shell script \"rm -rf '$TARGET_APP' && ditto '$SRC_APP' '$TARGET_APP' && xattr -dr com.apple.quarantine '$TARGET_APP'\" with administrator privileges"
            fi

            rm -f "$PKG"
            open -a "$TARGET_APP"
            """;

        File.WriteAllText(scriptPath, script.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add(processId.ToString());
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add(appBundle);
        psi.ArgumentList.Add(logPath);

        Process.Start(psi);
    }

    private static string ResolveMacAppBundle(string installDir)
    {
        var normalized = installDir.Replace('\\', '/').TrimEnd('/');
        var idx = normalized.IndexOf(".app/Contents/MacOS", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return normalized[..(idx + 4)];

        return "/Applications/LiveClaude.app";
    }
}
