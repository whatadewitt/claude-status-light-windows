using System.Text.Json.Serialization;

namespace StatusLight.Core.Hook;

public sealed record SessionRecord
{
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; init; } = "";
    [JsonPropertyName("term_program")] public string TermProgram { get; init; } = "unknown";
    [JsonPropertyName("tty")] public string Tty { get; init; } = "";
    [JsonPropertyName("pid")] public int Pid { get; init; }
    [JsonPropertyName("transcript_path")] public string TranscriptPath { get; init; } = "";
    [JsonPropertyName("permission_mode")] public string PermissionMode { get; init; } = "";
    [JsonPropertyName("updated_at")] public long UpdatedAt { get; init; }
    [JsonPropertyName("pid_start")] public long PidStart { get; init; }
    [JsonPropertyName("hwnd")] public long Hwnd { get; init; }
}
