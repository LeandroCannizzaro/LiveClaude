using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveClaude.Core.Ipc;

/// <summary>Wire format: one JSON object per line over a named pipe.</summary>
public sealed class IpcEnvelope
{
    /// <summary>"req", "res" or "evt".</summary>
    public string Kind { get; set; } = "req";

    /// <summary>Correlation id for request/response pairs.</summary>
    public string? Id { get; set; }

    public string Method { get; set; } = "";

    public JsonElement? Payload { get; set; }

    public string? Error { get; set; }
}

public static class IpcProtocol
{
    public const string PipeName = "LiveClaude.v1";

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static class Methods
    {
        public const string Ping = "ping";
        public const string Status = "status";
        public const string GetConfig = "config.get";
        public const string SaveSettings = "config.saveSettings";
        public const string UpsertSession = "session.upsert";
        public const string DeleteSession = "session.delete";
        public const string StartInstance = "instance.start";
        public const string StopInstance = "instance.stop";
        public const string RestartInstance = "instance.restart";
        public const string Attach = "terminal.attach";
        public const string Detach = "terminal.detach";
        public const string Input = "terminal.input";
        public const string Resize = "terminal.resize";
        public const string LogTail = "logs.tail";
    }

    public static class Events
    {
        public const string Status = "event.status";
        public const string Terminal = "event.terminal";
    }
}

public sealed class InstanceRef
{
    public string Id { get; set; } = "";
}

public sealed class TerminalInput
{
    public string Id { get; set; } = "";
    public string Data { get; set; } = "";
}

public sealed class TerminalResize
{
    public string Id { get; set; } = "";
    public int Columns { get; set; } = 120;
    public int Rows { get; set; } = 30;
}

public sealed class TerminalChunk
{
    public string Id { get; set; } = "";
    public string Data { get; set; } = "";
}

public sealed class LogTailRequest
{
    public string Id { get; set; } = "";
    public int Lines { get; set; } = 300;
}

public sealed class LogTailResponse
{
    public List<string> Lines { get; set; } = new();
}
