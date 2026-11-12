using System.Text.Json;

namespace StatusLight.Core.Hook;

public sealed record HookPayload(
    string? SessionId,
    string? Cwd,
    string? TranscriptPath,
    string? PermissionMode,
    string? HookEventName,
    string? ToolName,
    string? Message)
{
    /// <summary>Never throws; invalid or empty input yields all nulls. Non-string fields are ignored.</summary>
    public static HookPayload Parse(string json)
    {
        var empty = new HookPayload(null, null, null, null, null, null, null);
        if (string.IsNullOrWhiteSpace(json)) return empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return empty;
            return new HookPayload(
                Str(root, "session_id"),
                Str(root, "cwd"),
                Str(root, "transcript_path"),
                Str(root, "permission_mode"),
                Str(root, "hook_event_name"),
                Str(root, "tool_name"),
                Str(root, "message"));
        }
        catch (Exception)
        {
            return empty;
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
