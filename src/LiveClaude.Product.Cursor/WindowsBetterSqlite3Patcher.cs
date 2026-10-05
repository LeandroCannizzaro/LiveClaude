using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LiveClaude.Core.Config;
using Microsoft.Extensions.Logging;

namespace LiveClaude.Product.Cursor;

/// <summary>
/// On Windows, Cursor's bundled <c>better_sqlite3.node</c> is sometimes built for the wrong
/// NODE_MODULE_VERSION (e.g. 127 vs 137). Replaces it with the matching WiseLibs prebuild.
/// Idempotent: skips when the ABI already matches; re-runs after a CLI update (new versions folder).
/// </summary>
public sealed class WindowsBetterSqlite3Patcher
{
    public const string DefaultPrebuildUrl =
        "https://github.com/WiseLibs/better-sqlite3/releases/download/v12.12.0/better-sqlite3-v12.12.0-node-v137-win32-x64.tar.gz";

    private readonly HttpClient _http;
    private readonly string _statePath;
    private readonly Func<string?> _versionsDirectoryFactory;
    private readonly string _prebuildUrl;

    public WindowsBetterSqlite3Patcher(
        HttpClient? http = null,
        string? stateDirectory = null,
        Func<string?>? versionsDirectoryFactory = null,
        string? prebuildUrl = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _statePath = Path.Combine(
            stateDirectory ?? ConfigStore.StateDirectory,
            "cursor-better-sqlite3-patch.json");
        _versionsDirectoryFactory = versionsDirectoryFactory ?? CursorAgentLocator.LatestWindowsVersionsDirectory;
        _prebuildUrl = prebuildUrl ?? DefaultPrebuildUrl;
    }

    public async Task EnsurePatchedAsync(ILogger logger, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var versionDir = _versionsDirectoryFactory();
        if (versionDir is null)
        {
            logger.LogDebug("No cursor-agent versions directory found; skipping better-sqlite3 patch.");
            return;
        }

        var nodePath = Path.Combine(versionDir, "node_modules", "better-sqlite3", "build", "Release", "better_sqlite3.node");
        if (!File.Exists(nodePath))
        {
            logger.LogWarning("better_sqlite3.node not found at {Path}; skipping patch.", nodePath);
            return;
        }

        var versionName = Path.GetFileName(versionDir);
        var hash = ComputeSha256(nodePath);
        var state = LoadState();

        if (state is { } s &&
            string.Equals(s.AgentVersion, versionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.NodeSha256, hash, StringComparison.OrdinalIgnoreCase) &&
            s.Patched)
        {
            logger.LogDebug("better-sqlite3 already patched for agent {Version}.", versionName);
            return;
        }

        logger.LogInformation("Patching better-sqlite3 for Cursor agent {Version}…", versionName);

        var tmp = Path.Combine(Path.GetTempPath(), "liveclaude-better-sqlite3-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);

        try
        {
            var tgz = Path.Combine(tmp, "prebuild.tar.gz");
            await using (var response = await _http.GetStreamAsync(_prebuildUrl, ct).ConfigureAwait(false))
            await using (var file = File.Create(tgz))
                await response.CopyToAsync(file, ct).ConfigureAwait(false);

            ExtractTarGz(tgz, tmp);

            var replacement = Directory.EnumerateFiles(tmp, "better_sqlite3.node", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Downloaded prebuild from {_prebuildUrl} did not contain better_sqlite3.node.");

            File.Copy(replacement, nodePath, overwrite: true);

            var newHash = ComputeSha256(nodePath);
            SaveState(new PatchState
            {
                AgentVersion = versionName,
                NodeSha256 = newHash,
                Patched = true,
                PrebuildUrl = _prebuildUrl,
                PatchedUtc = DateTimeOffset.UtcNow
            });

            logger.LogInformation("Replaced better_sqlite3.node for agent {Version}.", versionName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to patch Cursor's better-sqlite3 native module on Windows. " +
                "Disable 'Auto-patch Windows sqlite' in Settings → Products if you use WSL instead. " +
                $"Details: {ex.Message}", ex);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Test helper: records that a path was already patched without downloading.</summary>
    public void MarkPatchedForTests(string agentVersion, string nodeSha256)
    {
        SaveState(new PatchState
        {
            AgentVersion = agentVersion,
            NodeSha256 = nodeSha256,
            Patched = true,
            PrebuildUrl = _prebuildUrl,
            PatchedUtc = DateTimeOffset.UtcNow
        });
    }

    public PatchState? LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
                return null;
            return JsonSerializer.Deserialize<PatchState>(File.ReadAllText(_statePath));
        }
        catch
        {
            return null;
        }
    }

    private void SaveState(PatchState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ExtractTarGz(string tgzPath, string destination)
    {
        using var file = File.OpenRead(tgzPath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    public sealed class PatchState
    {
        public string AgentVersion { get; set; } = "";
        public string NodeSha256 { get; set; } = "";
        public bool Patched { get; set; }
        public string? PrebuildUrl { get; set; }
        public DateTimeOffset? PatchedUtc { get; set; }
    }
}
