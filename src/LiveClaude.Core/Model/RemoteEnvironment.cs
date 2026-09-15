using System.Text.Json.Serialization;

namespace LiveClaude.Core.Model;

/// <summary>
/// A bridge environment registered on Anthropic's side. Every <c>claude remote-control</c> process
/// registers one, and it outlives the process — which is why the session picker fills up with
/// entries for servers that are long gone.
/// </summary>
public sealed class RemoteEnvironment
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? State { get; set; }

    /// <summary>"bridge" for the environments a Remote Control server creates.</summary>
    public string? ConfigType { get; set; }

    public string? MachineName { get; set; }
    public string? Directory { get; set; }
    public string? Branch { get; set; }
    public string? GitRepoUrl { get; set; }

    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public DateTimeOffset? ArchivedUtc { get; set; }

    public bool IsBridge => string.Equals(ConfigType, "bridge", StringComparison.OrdinalIgnoreCase);

    public bool IsArchived => ArchivedUtc is not null;
}

/// <summary>How an environment relates to what this machine is currently supervising.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EnvironmentUsage
{
    /// <summary>Registered by a server LiveClaude is running right now.</summary>
    InUse,

    /// <summary>A bridge environment for a directory LiveClaude knows, with no server behind it.</summary>
    Stale,

    /// <summary>Another machine, another directory, or not a bridge environment at all.</summary>
    Unrelated
}

public static class EnvironmentClassifier
{
    /// <summary>
    /// Decides whether an environment is live, a leftover of a directory we supervise, or none of
    /// our business. Only <see cref="EnvironmentUsage.Stale"/> entries are pre-selected for cleanup.
    /// </summary>
    public static EnvironmentUsage Classify(
        RemoteEnvironment environment,
        IEnumerable<InstanceSnapshot> instances,
        IEnumerable<SessionConfig> sessions)
    {
        foreach (var instance in instances)
        {
            if (!string.IsNullOrEmpty(instance.EnvironmentId) &&
                string.Equals(instance.EnvironmentId, environment.Id, StringComparison.Ordinal) &&
                instance.State is InstanceState.Running or InstanceState.Starting or InstanceState.NeedsAttention)
            {
                return EnvironmentUsage.InUse;
            }
        }

        if (!environment.IsBridge || string.IsNullOrWhiteSpace(environment.Directory))
            return EnvironmentUsage.Unrelated;

        foreach (var session in sessions)
        {
            if (SamePath(session.Directory, environment.Directory))
                return EnvironmentUsage.Stale;
        }

        return EnvironmentUsage.Unrelated;
    }

    /// <summary>
    /// The everyday cleanup: for each directory keep the environment a server is using — or the
    /// newest one when none is — and return every other registration for that directory.
    /// Environments in use are never returned, so the result is always safe to delete.
    /// </summary>
    public static IReadOnlyList<RemoteEnvironment> FindDuplicates(
        IEnumerable<RemoteEnvironment> environments,
        IEnumerable<InstanceSnapshot> instances)
    {
        var live = instances
            .Where(i => i.State is InstanceState.Running or InstanceState.Starting or InstanceState.NeedsAttention)
            .Select(i => i.EnvironmentId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var duplicates = new List<RemoteEnvironment>();

        var groups = environments
            .Where(e => e.IsBridge && !e.IsArchived && !string.IsNullOrWhiteSpace(e.Directory))
            .GroupBy(e => e.Directory!.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var keep = group.FirstOrDefault(e => live.Contains(e.Id))
                       ?? group.OrderByDescending(e => e.CreatedUtc ?? DateTimeOffset.MinValue).First();

            foreach (var environment in group)
            {
                if (!ReferenceEquals(environment, keep) && !live.Contains(environment.Id))
                    duplicates.Add(environment);
            }
        }

        return duplicates;
    }

    public static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            left.TrimEnd('\\', '/'),
            right.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
