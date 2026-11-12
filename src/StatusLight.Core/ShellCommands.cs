namespace StatusLight.Core;

/// <summary>
/// Recognizes Bash tool shells by the shell-snapshot signature their wrapper
/// command line carries, and extracts the user-visible command from it.
/// </summary>
public static class ShellCommands
{
    private const string SignatureSlash = ".claude/shell-snapshots/snapshot-";
    private const string SignatureBackslash = @".claude\shell-snapshots\snapshot-";

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
