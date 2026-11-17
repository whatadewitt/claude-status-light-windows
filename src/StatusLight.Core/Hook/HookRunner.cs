namespace StatusLight.Core.Hook;

public sealed class HookRunner(
    string sessionsDir,
    Func<ProcessIdentity> identify,
    Func<DateTimeOffset> clock,
    Func<string> markerName)
{
    public static string DefaultMarkerName() =>
        $"{Environment.ProcessId}.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    public void Run(string stateArg, string payloadJson, string fallbackCwd)
    {
        var p = HookPayload.Parse(payloadJson);
        var sessionId = string.IsNullOrEmpty(p.SessionId) ? "default" : p.SessionId;
        var state = HookClassifier.Resolve(stateArg, p);

        Directory.CreateDirectory(sessionsDir);
        var safe = SessionIds.Sanitize(sessionId);
        var file = Path.Combine(sessionsDir, safe + ".json");
        var agentsDir = Path.Combine(sessionsDir, safe + ".agents");

        switch (state)
        {
            case "agent-start":
                if (!File.Exists(file)) return;
                Directory.CreateDirectory(agentsDir);
                File.WriteAllBytes(Path.Combine(agentsDir, markerName()), []);
                return;
            case "agent-stop":
                if (!Directory.Exists(agentsDir)) return;
                var marker = Directory.EnumerateFiles(agentsDir).FirstOrDefault();
                if (marker is not null)
                {
                    try { File.Delete(marker); } catch (IOException) { }
                }
                return;
            case "end":
                try { File.Delete(file); } catch (IOException) { }
                DeleteDir(agentsDir);
                return;
        }

        if (p.HookEventName == "SessionStart") DeleteDir(agentsDir);

        var id = identify();
        var prev = SessionFiles.TryRead(file);

        // Terminal identity is sticky: a ttyless helper process must not strip it.
        if (id.Tty.Length == 0 && prev is { Tty.Length: > 0 })
            id = new ProcessIdentity(prev.Pid, prev.PidStart, prev.TermProgram, prev.Tty, prev.Hwnd);

        var mode = string.IsNullOrEmpty(p.PermissionMode) ? prev?.PermissionMode ?? "" : p.PermissionMode;

        SessionFiles.WriteAtomic(file, new SessionRecord
        {
            State = state,
            SessionId = sessionId,
            Cwd = string.IsNullOrEmpty(p.Cwd) ? fallbackCwd : p.Cwd,
            TermProgram = id.TermProgram,
            Tty = id.Tty,
            Pid = id.Pid,
            TranscriptPath = p.TranscriptPath ?? "",
            PermissionMode = mode,
            UpdatedAt = clock().ToUnixTimeSeconds(),
            PidStart = id.PidStart,
            Hwnd = id.Hwnd,
        });
    }

    private static void DeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}
