namespace StatusLight.Core;

/// <summary>
/// Recognizes Bash tool shells by the shell-snapshot signature their wrapper
/// command line carries, and extracts the user-visible command from it.
/// </summary>
public static class ShellCommands
{
    private const string SignatureSlash = ".claude/shell-snapshots/snapshot-";
    private const string SignatureBackslash = @".claude\shell-snapshots\snapshot-";

    private static readonly HashSet<string> ShellProcessNames = new(StringComparer.Ordinal)
    {
        "bash", "sh", "dash", "zsh", "cmd", "powershell", "pwsh",
    };

    /// <summary>
    /// Pids of the direct children of <paramref name="parentPid"/> whose executable is a shell
    /// (the only processes whose command lines are worth fetching). <c>ExeFile</c> is the image
    /// name as a process snapshot reports it, e.g. "bash.exe".
    /// </summary>
    public static IReadOnlyList<int> ShellChildren(
        IEnumerable<(int Pid, int ParentPid, string ExeFile)> processes, int parentPid)
    {
        var result = new List<int>();
        foreach (var (pid, ppid, exe) in processes)
        {
            if (ppid != parentPid || pid == parentPid) continue;
            var name = exe.ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name[..^4];
            if (ShellProcessNames.Contains(name)) result.Add(pid);
        }
        return result;
    }

    public static bool IsClaudeShell(string commandLine)
        => commandLine.Contains(SignatureSlash, StringComparison.Ordinal)
           || commandLine.Contains(SignatureBackslash, StringComparison.Ordinal);

    /// <summary>
    /// The `eval '...'` argument of the harness wrapper, with shell quote-escaping
    /// undone. Unrecognized shapes come back unchanged rather than hiding a running shell.
    /// </summary>
    public static string CommandFromArgv(string commandLine)
    {
        const string marker = "eval '";
        var start = commandLine.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return commandLine;
        var rest = commandLine[(start + marker.Length)..];

        // The wrapper appends "< /dev/null" (background shells) and/or
        // "&& pwd -P >| ..." after the closing quote.
        var end = rest.LastIndexOf("' < /dev/null", StringComparison.Ordinal);
        if (end < 0) end = rest.LastIndexOf("' && pwd -P", StringComparison.Ordinal);
        if (end >= 0) rest = rest[..end];
        else if (rest.EndsWith('\'')) rest = rest[..^1];

        return rest.Replace("'\\''", "'");
    }
}
