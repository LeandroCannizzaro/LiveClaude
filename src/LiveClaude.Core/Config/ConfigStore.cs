using System.Text.Json;
using System.Text.Json.Serialization;
using LiveClaude.Abstractions;
using LiveClaude.Core.Model;

namespace LiveClaude.Core.Config;

/// <summary>
/// Reads and writes the shared configuration file. The supervisor and the desktop app both point at
/// the same path so a change made in the UI is picked up by the supervisor; where that path is comes
/// from the platform (%ProgramData% on Windows, XDG on Linux, ~/Library on macOS).
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();

    public ConfigStore(string? path = null)
    {
        Path = path ?? DefaultPath;
    }

    public string Path { get; }

    private static IPathLayout Layout => PlatformLoader.Current.Paths;

    public static string RootDirectory => Layout.RootDirectory;

    public static string DefaultPath => Layout.ConfigFilePath;

    public static string LogDirectory => Layout.LogDirectory;

    public static string StateDirectory => Layout.StateDirectory;

    /// <summary>Raised after a successful <see cref="Save"/> or an external file change.</summary>
    public event Action<AppConfig>? Changed;

    public AppConfig Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(Path))
                    return new AppConfig();

                var json = File.ReadAllText(Path);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppConfig();

                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A corrupt file must not take the service down: keep a copy and start clean.
                TryBackupCorruptFile();
                return new AppConfig();
            }
        }
    }

    public void Save(AppConfig config)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(tmp, Path, overwrite: true);
        }

        Changed?.Invoke(config);
    }

    public static void EnsureDirectories() => Layout.EnsureDirectories();

    private void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(Path))
                File.Copy(Path, $"{Path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
