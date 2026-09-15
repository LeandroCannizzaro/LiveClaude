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

/// <summary>What an environment is, who owns it, and why it may be protected from deletion.</summary>
public sealed record EnvironmentClassification(
    EnvironmentUsage Usage,
    string? OwnerName = null,
    string? ProtectionNote = null);

public static class EnvironmentClassifier
{
    /// <summary>
    /// An environment registered slightly before a server started still belongs to that server:
    /// the CLI registers around launch time and the two clocks are not the same.
    /// </summary>
    private static readonly TimeSpan RegistrationSlack = TimeSpan.FromMinutes(1);

    public static bool IsLive(InstanceState state) =>
        state is InstanceState.Running or InstanceState.Starting or InstanceState.NeedsAttention;

    /// <summary>
    /// Decides whether an environment is live, a leftover of a directory we supervise, or none of
    /// our business. Only <see cref="EnvironmentUsage.Stale"/> entries are pre-selected for cleanup.
    ///
    /// Protection deliberately does not rely on having resolved the environment id over the API:
    /// that lookup is best effort, and when it fails the live server's own environment must not
    /// become deletable. A directory with a running server protects every registration made during
    /// that run — including one the server creates again after a delete.
    /// </summary>
    public static EnvironmentClassification Describe(
        RemoteEnvironment environment,
        IEnumerable<InstanceSnapshot> instances,
        IEnumerable<SessionConfig> sessions)
    {
        var live = instances.Where(i => IsLive(i.State)).ToList();

        // 1. The environment we know this server registered.
        var tagged = live.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.EnvironmentId) &&
            string.Equals(i.EnvironmentId, environment.Id, StringComparison.Ordinal));

        if (tagged is not null)
            return new EnvironmentClassification(EnvironmentUsage.InUse, tagged.Name, "This is the environment the running server registered.");

        if (!environment.IsBridge || string.IsNullOrWhiteSpace(environment.Directory))
            return new EnvironmentClassification(EnvironmentUsage.Unrelated);

        // 2. A server is running for this directory. Anything it could have registered is protected.
        var owner = live.FirstOrDefault(i => SamePath(i.Directory, environment.Directory));
        if (owner is not null && !BelongsToAnEarlierRun(environment, owner))
        {
            return new EnvironmentClassification(
                EnvironmentUsage.InUse,
                owner.Name,
                $"A server is running for this directory. Stop '{owner.Name}' in the Dashboard first — " +
                "while it runs it registers again after every delete.");
        }

        // 3. A directory LiveClaude supervises, with nothing running behind this registration.
        foreach (var session in sessions)
        {
            if (SamePath(session.Directory, environment.Directory))
                return new EnvironmentClassification(EnvironmentUsage.Stale);
        }

        return owner is not null
            ? new EnvironmentClassification(EnvironmentUsage.Stale)
            : new EnvironmentClassification(EnvironmentUsage.Unrelated);
    }

    public static EnvironmentUsage Classify(
        RemoteEnvironment environment,
        IEnumerable<InstanceSnapshot> instances,
        IEnumerable<SessionConfig> sessions) =>
        Describe(environment, instances, sessions).Usage;

    /// <summary>
    /// True when the environment was registered before the current run of <paramref name="owner"/>
    /// started, which makes it a leftover of an earlier run rather than something in use.
    /// </summary>
    private static bool BelongsToAnEarlierRun(RemoteEnvironment environment, InstanceSnapshot owner)
    {
        // Without both timestamps there is no way to prove it is old, so treat it as in use.
        if (owner.StartedUtc is not { } startedUtc || environment.CreatedUtc is not { } createdUtc)
            return false;

        return createdUtc < startedUtc - RegistrationSlack;
    }

    /// <summary>
    /// The everyday cleanup: for each directory keep the environment a server is using — or the
    /// newest one when none is — and return every other registration for that directory.
    /// Anything a running server could be using is never returned, so the result is safe to delete.
    /// </summary>
    public static IReadOnlyList<RemoteEnvironment> FindDuplicates(
        IEnumerable<RemoteEnvironment> environments,
        IEnumerable<InstanceSnapshot> instances)
    {
        var snapshots = instances.ToList();
        var duplicates = new List<RemoteEnvironment>();

        var groups = environments
            .Where(e => e.IsBridge && !e.IsArchived && !string.IsNullOrWhiteSpace(e.Directory))
            .GroupBy(e => e.Directory!.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var candidates = group.ToList();
            var protectedIds = candidates
                .Where(e => Describe(e, snapshots, []).Usage == EnvironmentUsage.InUse)
                .Select(e => e.Id)
                .ToHashSet(StringComparer.Ordinal);

            // With a live server the protected registrations already are what we keep; otherwise
            // the newest one stays.
            var keep = protectedIds.Count > 0
                ? null
                : candidates.OrderByDescending(e => e.CreatedUtc ?? DateTimeOffset.MinValue).First();

            foreach (var environment in candidates)
            {
                if (protectedIds.Contains(environment.Id) || ReferenceEquals(environment, keep))
                    continue;

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
