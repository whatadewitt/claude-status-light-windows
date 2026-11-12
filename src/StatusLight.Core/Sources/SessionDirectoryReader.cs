using StatusLight.Core.Hook;

namespace StatusLight.Core.Sources;

/// <summary>Reads one sessions directory (port of the Mac StateStore.activeSessions).</summary>
public sealed class SessionDirectoryReader(
    string sessionsDir,
    SessionOrigin origin,
    string? distro,
    IProcessTable processes,
    Func<string, string> mapTranscriptPath)
{
    private static readonly TimeSpan PidlessMaxAge = TimeSpan.FromHours(12);

    // TranscriptTitleCache is not thread-safe; polls never overlap, so one instance is fine.
    private readonly TranscriptTitleCache _titles = new();

    public IReadOnlyList<SessionState> Read(DateTimeOffset now)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(sessionsDir)) return [];
            files = Directory.GetFiles(sessionsDir, "*.json");
        }
        catch (Exception)
        {
            return [];
        }

        var result = new List<SessionState>();
        foreach (var file in files)
        {
            var record = SessionFiles.TryRead(file);
            if (record is null) continue;
            var stem = file[..^".json".Length];
            var agentsDir = stem + ".agents";
            var id = record.SessionId.Length > 0 ? record.SessionId : Path.GetFileNameWithoutExtension(file);
            var updatedAt = DateTimeOffset.FromUnixTimeSeconds(record.UpdatedAt);
            int? pid = record.Pid > 1 ? record.Pid : null;

            if (pid is { } p)
            {
                if (processes.Check(p, record.PidStart) == ProcessLiveness.Dead)
                {
                    Delete(file, agentsDir);
                    continue;
                }
            }
            else if (now - updatedAt > PidlessMaxAge)
            {
                continue;
            }

            // Liveness first so a dead session is cleaned up even with an unrecognized state.
            if (!LightStates.TryParse(record.State, out var state)) continue;

            IReadOnlyList<string> shells = [];
            if (state == LightState.Idle && pid is { } sp)
            {
                shells = processes.ChildCommandLines(sp)
                    .Where(ShellCommands.IsClaudeShell)
                    .Select(ShellCommands.CommandFromArgv)
                    .ToList();
                if (shells.Count > 0) state = LightState.Working;
            }

            string? title = null;
            if (record.TranscriptPath.Length > 0)
                title = _titles.TitleFor(mapTranscriptPath(record.TranscriptPath));

            result.Add(new SessionState
            {
                SessionId = id,
                State = state,
                Cwd = record.Cwd,
                TermProgram = record.TermProgram,
                Tty = record.Tty,
                Pid = pid,
                PidStart = record.PidStart,
                Hwnd = record.Hwnd,
                UpdatedAt = updatedAt,
                Agents = CountAgents(agentsDir),
                Title = title,
                Shells = shells,
                PermissionMode = record.PermissionMode.Length > 0 ? record.PermissionMode : null,
                Origin = origin,
                Distro = distro,
            });
        }

        result.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return result;
    }

    private static int CountAgents(string agentsDir)
    {
        try
        {
            return Directory.Exists(agentsDir) ? Directory.GetFiles(agentsDir).Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static void Delete(string file, string agentsDir)
    {
        try { File.Delete(file); } catch (Exception) { }
        try { if (Directory.Exists(agentsDir)) Directory.Delete(agentsDir, recursive: true); } catch (Exception) { }
    }
}
