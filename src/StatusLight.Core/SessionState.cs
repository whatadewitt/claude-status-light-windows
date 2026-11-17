namespace StatusLight.Core;

public enum SessionOrigin { Windows, Wsl }

public enum HostTag { None, PowerShell, Wsl, ClaudeApp }

public sealed record SessionState
{
    public required string SessionId { get; init; }
    public required LightState State { get; init; }
    public string Cwd { get; init; } = "";
    public string TermProgram { get; init; } = "unknown";
    public string Tty { get; init; } = "";
    public int? Pid { get; init; }
    public long PidStart { get; init; }
    public long Hwnd { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Agents { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Shells { get; init; } = [];
    public string? PermissionMode { get; init; }
    public SessionOrigin Origin { get; init; }
    public string? Distro { get; init; }

    /// <summary>Headless: a known owning process but no controlling terminal.</summary>
    public bool IsBackground => Pid is not null && Tty.Length == 0;

    public HostTag Tag
    {
        get
        {
            if (Origin == SessionOrigin.Wsl) return HostTag.Wsl;
            if (Tty.StartsWith("claude-app:", StringComparison.Ordinal)) return HostTag.ClaudeApp;
            if (Tty.StartsWith("wt:", StringComparison.Ordinal) ||
                Tty.StartsWith("console:", StringComparison.Ordinal)) return HostTag.PowerShell;
            return HostTag.None;
        }
    }

    public string Project => SessionText.ProjectName(Cwd);

    /// <summary>Idle headless sessions quiet for over two minutes.</summary>
    public bool IsParked(DateTimeOffset now) =>
        IsBackground && State == LightState.Idle && now - UpdatedAt > TimeSpan.FromMinutes(2);
}
