namespace StatusLight.Core.Hook;

/// <summary>Turns the hook's ancestor chain plus environment into the session's process identity.</summary>
public static class HostClassifier
{
    public static readonly IReadOnlySet<string> Shells = new HashSet<string>(StringComparer.Ordinal)
    {
        "bash", "sh", "dash", "zsh", "fish", "cmd", "powershell", "pwsh", "conhost", "openconsole", "env",
    };

    public static bool IsClaudeDesktop(ProcEntry p) =>
        p.Name == "claude"
        && (p.ImagePath.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase)
            || p.ImagePath.Contains(@"\WindowsApps\Claude", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Claude Code process: the first ancestor that isn't a shell; null when there is none.</summary>
    /// <param name="ancestors">The hook's parent first, then upward.</param>
    public static ProcEntry? ClaudeProcess(IReadOnlyList<ProcEntry> ancestors)
    {
        var i = ClaudeIndex(ancestors);
        return i < 0 ? null : ancestors[i];
    }

    private static int ClaudeIndex(IReadOnlyList<ProcEntry> ancestors)
    {
        for (var i = 0; i < ancestors.Count; i++)
        {
            if (!Shells.Contains(ancestors[i].Name)) return i;
        }
        return -1;
    }

    /// <param name="ancestors">The hook's parent first, then upward.</param>
    public static ProcessIdentity Classify(
        IReadOnlyList<ProcEntry> ancestors, IReadOnlyDictionary<string, string?> env, long consoleHwnd)
    {
        var index = ClaudeIndex(ancestors);
        if (index < 0) return ProcessIdentity.Unknown;
        var claude = ancestors[index];

        // The outermost desktop process is the main Electron process (stable across helper restarts).
        ProcEntry? desktop = null;
        for (var i = index + 1; i < ancestors.Count; i++)
        {
            if (IsClaudeDesktop(ancestors[i])) desktop = ancestors[i];
        }
        if (desktop is not null)
            return new(claude.Pid, claude.StartFileTime, "ClaudeApp", $"claude-app:{desktop.Pid}", 0);

        var wt = Get(env, "WT_SESSION");
        if (wt is not null)
            return new(claude.Pid, claude.StartFileTime, "WindowsTerminal", $"wt:{wt}", 0);

        var term = Get(env, "TERM_PROGRAM");
        if (consoleHwnd != 0)
            return new(claude.Pid, claude.StartFileTime, term ?? "console", $"console:{claude.Pid}", consoleHwnd);

        return new(claude.Pid, claude.StartFileTime, term ?? "unknown", "", 0);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> env, string key) =>
        env.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
}
