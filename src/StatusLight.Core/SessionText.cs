namespace StatusLight.Core;

public static class SessionText
{
    public static string ProjectName(string cwd)
    {
        var trimmed = cwd.TrimEnd('/', '\\');
        var i = trimmed.LastIndexOfAny(['/', '\\']);
        var name = i >= 0 ? trimmed[(i + 1)..] : trimmed;
        return name.Length == 0 ? "session" : name;
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? string.Concat(s.AsSpan(0, max - 1), "…") : s;

    public static string DisplayName(SessionState s)
    {
        if (!s.IsBackground) return s.Project;
        if (!string.IsNullOrEmpty(s.Title)) return $"{s.Project} · {Truncate(s.Title, 48)}";
        return $"{s.Project} (bg)";
    }

    public static string AgentsSuffix(int n) =>
        n > 0 ? $" · {n} agent{(n == 1 ? "" : "s")}" : "";

    public static string ModeSuffix(string? mode)
    {
        if (string.IsNullOrEmpty(mode) || mode == "default") return "";
        return mode switch
        {
            "plan" => " · plan",
            "acceptEdits" => " · auto-edit",
            "auto" => " · auto",
            "dontAsk" => " · don't-ask",
            "bypassPermissions" => " · bypass",
            _ => $" · {mode}",
        };
    }

    public static string ShellsSuffix(SessionState s)
    {
        if (s.IsBackground && !string.IsNullOrEmpty(s.Title)) return "";
        if (s.Shells.Count == 0) return "";
        if (s.Shells.Count > 1) return $" · running {s.Shells.Count} shells…";
        return $" · sh: {Truncate(s.Shells[0], 40)}";
    }

    public static string PanelRow(SessionState s) =>
        DisplayName(s) + ModeSuffix(s.PermissionMode) + ShellsSuffix(s) + AgentsSuffix(s.Agents);

    public static string MenuRow(SessionState s) =>
        DisplayName(s) + ModeSuffix(s.PermissionMode) + ShellsSuffix(s) + " — " +
        LightStates.Label(s.State) + AgentsSuffix(s.Agents);

    public static string Tooltip(SessionState s, DateTimeOffset now)
    {
        var lines = new List<string>
        {
            s.Cwd,
            $"{s.TermProgram} · {(s.Tty.Length == 0 ? "tty unknown" : s.Tty)}",
        };
        if (!string.IsNullOrEmpty(s.PermissionMode)) lines.Add($"mode: {s.PermissionMode}");
        if (s.Origin == SessionOrigin.Wsl)
            lines.Add($"WSL session ({s.Distro})");
        else if (s.IsParked(now))
            lines.Add($"parked — idle {(int)((now - s.UpdatedAt).TotalMinutes)}m, process alive");
        else if (s.IsBackground)
            lines.Add("background session (no terminal)");
        foreach (var sh in s.Shells) lines.Add($"sh: {(sh.Length > 300 ? sh[..300] : sh)}");
        return string.Join("\n", lines);
    }

    public static string TagText(HostTag t) => t switch
    {
        HostTag.PowerShell => "PowerShell",
        HostTag.Wsl => "WSL",
        HostTag.ClaudeApp => "Claude app",
        _ => "",
    };
}
