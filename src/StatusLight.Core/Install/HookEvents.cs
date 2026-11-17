namespace StatusLight.Core.Install;

/// <summary>Claude Code hook events and the light state each one reports.</summary>
public static class HookEvents
{
    public static IReadOnlyList<(string Event, string State)> All { get; } = new (string, string)[]
    {
        ("SessionStart", "idle"),
        ("UserPromptSubmit", "working"),
        ("PreToolUse", "working"),
        ("PostToolUse", "working"),
        ("Notification", "attention"),
        ("Stop", "idle"),
        ("SubagentStart", "agent-start"),
        ("SubagentStop", "agent-stop"),
        ("SessionEnd", "end"),
    };

    /// <summary>Tool-scoped events take a matcher; the rest do not.</summary>
    public static IReadOnlySet<string> MatcherEvents { get; } = new HashSet<string> { "PreToolUse", "PostToolUse" };
}
