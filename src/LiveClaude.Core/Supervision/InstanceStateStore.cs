using System.Text.Json;
using LiveClaude.Core.Config;

namespace LiveClaude.Core.Supervision;

/// <summary>State that must outlive the supervisor process, so a reboot can still reattach.</summary>
public sealed class PersistedInstanceState
{
    public DateTimeOffset? LastStopUtc { get; set; }
    public string? LastSessionUrl { get; set; }
    public int RestartCount { get; set; }

    /// <summary>Bridge environment registered by the last run, so the UI can tell live from stale.</summary>
    public string? LastEnvironmentId { get; set; }
}

/// <summary>Per-instance state files under %ProgramData%\LiveClaude\state.</summary>
public sealed class InstanceStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _directory;

    public InstanceStateStore(string? directory = null)
    {
        _directory = directory ?? ConfigStore.StateDirectory;
        Directory.CreateDirectory(_directory);
    }

    public PersistedInstanceState Load(string instanceId)
    {
        var path = PathFor(instanceId);
        try
        {
            if (!File.Exists(path))
                return new PersistedInstanceState();

            return JsonSerializer.Deserialize<PersistedInstanceState>(File.ReadAllText(path), Options)
                   ?? new PersistedInstanceState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new PersistedInstanceState();
        }
    }

    public void Save(string instanceId, PersistedInstanceState state)
    {
        try
        {
            File.WriteAllText(PathFor(instanceId), JsonSerializer.Serialize(state, Options));
        }
        catch (IOException)
        {
            // best effort
        }
    }

    public void Delete(string instanceId)
    {
        try
        {
            var path = PathFor(instanceId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private string PathFor(string instanceId) => Path.Combine(_directory, $"{instanceId}.json");
}
