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

    /// <param name="ancestors">The hook's parent first, then upward.</param>
    public static ProcessIdentity Classify(
        IReadOnlyList<ProcEntry> ancestors, IReadOnlyDictionary<string, string?> env, long consoleHwnd)
    {
        var index = -1;
        for (var i = 0; i < ancestors.Count; i++)
        {
            if (!Shells.Contains(ancestors[i].Name)) { index = i; break; }
        }
        if (index < 0) return ProcessIdentity.Unknown;
        var claude = ancestors[index];

        for (var i = index + 1; i < ancestors.Count; i++)
        {
            if (IsClaudeDesktop(ancestors[i]))
                return new(claude.Pid, claude.StartFileTime, "ClaudeApp", $"claude-app:{ancestors[i].Pid}", 0);
        }

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
