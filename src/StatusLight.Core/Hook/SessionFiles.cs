using System.Globalization;
using System.Text.Json;

namespace StatusLight.Core.Hook;

public static class SessionFiles
{
    /// <summary>Returns null on any IO or parse failure. Tolerant of missing optional fields.</summary>
    public static SessionRecord? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return new SessionRecord
            {
                State = Str(root, "state") ?? "",
                SessionId = Str(root, "session_id") ?? "",
                Cwd = Str(root, "cwd") ?? "",
                TermProgram = Str(root, "term_program") ?? "unknown",
                Tty = Str(root, "tty") ?? "",
                Pid = (int)Math.Clamp(Num(root, "pid"), int.MinValue, int.MaxValue),
                TranscriptPath = Str(root, "transcript_path") ?? "",
                PermissionMode = Str(root, "permission_mode") ?? "",
                UpdatedAt = Num(root, "updated_at"),
                PidStart = Num(root, "pid_start"),
                Hwnd = Num(root, "hwnd"),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void WriteAtomic(string path, SessionRecord r)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) dir = ".";
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(r, StatusLightJsonContext.Default.SessionRecord) + "\n");
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (OperatingSystem.IsWindows() && WindowsReplace.TryReplace(tmp, path)) return;
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(20);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long Num(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt64(out var l)) return l;
            return v.TryGetDouble(out var d) ? (long)d : 0;
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return (long)d;
        }
        return 0;
    }
}
