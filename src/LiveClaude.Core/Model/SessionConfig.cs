using System.Text.Json.Serialization;
using LiveClaude.Core.Products;

namespace LiveClaude.Core.Model;

/// <summary>How the <c>claude remote-control</c> server creates sessions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpawnMode
{
    /// <summary>All sessions share the server's working directory.</summary>
    SameDir,

    /// <summary>Each on-demand session gets its own git worktree (requires a git repository).</summary>
    Worktree,

    /// <summary>Serve exactly one session and reject additional connections.</summary>
    Session
}

/// <summary>Permission mode applied to the sessions the server spawns.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PermissionMode
{
    Default,
    AcceptEdits,
    Auto,
    BypassPermissions,
    DontAsk,
    Plan
}

/// <summary>A supervised agent instance — one long-lived CLI process per entry.</summary>
public sealed class SessionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>
    /// Which agent product runs this session (<see cref="ProductIds.Claude"/>,
    /// <see cref="ProductIds.Cursor"/>, …).
    /// </summary>
    public string ProductId { get; set; } = ProductIds.Claude;

    /// <summary>Display name (<c>--name</c> for both Claude and Cursor workers).</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Primary working directory. For Claude this is the process cwd; for Cursor it is the first
    /// <c>--worker-dir</c> (assignment identity).
    /// </summary>
    public string Directory { get; set; } = "";

    /// <summary>When false the supervisor ignores this entry entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Start together with the supervisor (service or scheduled task).</summary>
    public bool AutoStart { get; set; } = true;

    // --- Claude remote-control options (used when ProductId == claude) ---

    public SpawnMode Spawn { get; set; } = SpawnMode.SameDir;

    public PermissionMode PermissionMode { get; set; } = PermissionMode.Default;

    /// <summary>Maximum concurrent sessions (<c>--capacity</c>); ignored for <see cref="SpawnMode.Session"/>.</summary>
    public int Capacity { get; set; } = 32;

    /// <summary>Pre-create one session in the working directory (<c>--[no-]create-session-in-dir</c>).</summary>
    public bool CreateSessionInDir { get; set; } = true;

    public bool Sandbox { get; set; }

    public bool Verbose { get; set; }

    /// <summary>Optional model override passed through to the server.</summary>
    public string? Model { get; set; }

    /// <summary>Prefix for auto-generated session names (<c>--remote-control-session-name-prefix</c>).</summary>
    public string? SessionNamePrefix { get; set; }

    /// <summary>
    /// Reattach to the session previously served in this directory (<c>--continue</c>) when the
    /// server is restarted inside the reattach window. Falls back to a fresh session otherwise.
    /// </summary>
    public bool ContinuePrevious { get; set; } = true;

    /// <summary>Reattach to one specific session id (<c>--session-id</c>). Wins over <see cref="ContinuePrevious"/>.</summary>
    public string? SessionId { get; set; }

    // --- Cursor My Machines options (used when ProductId == cursor) ---

    public CursorSessionOptions Cursor { get; set; } = new();

    /// <summary>Raw extra arguments appended verbatim.</summary>
    public string ExtraArgs { get; set; } = "";

    /// <summary>Extra environment variables for the spawned process.</summary>
    public Dictionary<string, string> Environment { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public SessionConfig Clone()
    {
        var clone = (SessionConfig)MemberwiseClone();
        clone.Environment = new Dictionary<string, string>(Environment, StringComparer.OrdinalIgnoreCase);
        clone.Cursor = Cursor.Clone();
        return clone;
    }

    /// <summary>
    /// Common validation shared by every product. Product-specific rules run through
    /// <see cref="IAgentProduct.Validate"/>.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Name is required.";
        if (string.IsNullOrWhiteSpace(Directory))
            return "Directory is required.";
        if (!System.IO.Directory.Exists(Directory))
            return $"Directory not found: {Directory}";
        if (string.IsNullOrWhiteSpace(ProductId))
            return "Product is required.";
        return null;
    }

    /// <summary>Claude-specific validation kept for tests and the Claude product.</summary>
    public string? ValidateClaudeOptions()
    {
        if (Capacity is < 1 or > 32)
            return "Capacity must be between 1 and 32.";
        if (Spawn == SpawnMode.Session && !CreateSessionInDir)
            return "Spawn=Session requires 'Create session in dir'.";
        if (!string.IsNullOrWhiteSpace(SessionId) && Spawn != SpawnMode.SameDir)
            return "A fixed session id cannot be combined with spawn flags.";
        return null;
    }
}
