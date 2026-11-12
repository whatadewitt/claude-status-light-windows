using StatusLight.Core.Hook;

namespace StatusLight.Core.Tests;

public sealed class HostClassifierTests
{
    private const string CliPath = @"C:\Users\x\.local\bin\claude.exe";
    private const string DesktopPath = @"C:\Users\x\AppData\Local\AnthropicClaude\app-1.0\claude.exe";

    private static readonly ProcEntry Bash = new(10, 11, "bash", @"C:\Program Files\Git\usr\bin\bash.exe", 1000);
    private static readonly ProcEntry Cmd = new(12, 20, "cmd", @"C:\Windows\System32\cmd.exe", 1000);
    private static readonly ProcEntry Cli = new(20, 30, "claude", CliPath, 900);
    private static readonly ProcEntry Desktop = new(30, 1, "claude", DesktopPath, 500);

    private static Dictionary<string, string?> Env(string? wt = null, string? term = null) =>
        new() { ["WT_SESSION"] = wt, ["TERM_PROGRAM"] = term };

    [Fact]
    public void Shells_only_chain_is_unknown()
    {
        string[] shells = ["bash", "sh", "dash", "zsh", "fish", "cmd", "powershell", "pwsh", "conhost", "openconsole", "env"];
        Assert.True(HostClassifier.Shells.SetEquals(shells));
        var chain = shells.Select((n, i) => new ProcEntry(100 + i, 101 + i, n, "", 1000)).ToList();
        var id = HostClassifier.Classify(chain, Env(wt: "g"), 77);
        Assert.Equal(ProcessIdentity.Unknown, id);
    }

    [Fact]
    public void Empty_chain_is_unknown()
    {
        Assert.Equal(ProcessIdentity.Unknown, HostClassifier.Classify([], Env(), 0));
    }

    [Fact]
    public void Cli_under_windows_terminal_uses_wt_session()
    {
        var id = HostClassifier.Classify([Bash, Cli], Env(wt: "g"), 77);
        Assert.Equal(new ProcessIdentity(20, 900, "WindowsTerminal", "wt:g", 0), id);
    }

    [Fact]
    public void Desktop_ancestor_wins_even_with_wt_session()
    {
        var id = HostClassifier.Classify([Cmd, Cli, Desktop], Env(wt: "g"), 77);
        Assert.Equal(new ProcessIdentity(20, 900, "ClaudeApp", "claude-app:30", 0), id);
    }

    [Fact]
    public void Console_without_wt_uses_console_pid_and_hwnd()
    {
        var id = HostClassifier.Classify([Bash, Cli], Env(), 77);
        Assert.Equal(new ProcessIdentity(20, 900, "console", "console:20", 77), id);
    }

    [Fact]
    public void Console_prefers_term_program_env()
    {
        var id = HostClassifier.Classify([Cli], Env(term: "vscode"), 77);
        Assert.Equal(new ProcessIdentity(20, 900, "vscode", "console:20", 77), id);
    }

    [Fact]
    public void No_console_is_headless_with_empty_tty()
    {
        var id = HostClassifier.Classify([Bash, Cli], Env(), 0);
        Assert.Equal(new ProcessIdentity(20, 900, "unknown", "", 0), id);
        Assert.Equal("", id.Tty);
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\AnthropicClaude\app-1.0\claude.exe", true)]
    [InlineData(@"c:\users\x\appdata\local\anthropicclaude\app-1.0\Claude.exe", true)]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_1.0.0.0_x64__abc\app\claude.exe", true)]
    [InlineData(@"C:\Users\x\.local\bin\claude.exe", false)]
    [InlineData("", false)]
    public void IsClaudeDesktop_matches_desktop_install_paths(string path, bool expected)
    {
        Assert.Equal(expected, HostClassifier.IsClaudeDesktop(new ProcEntry(1, 0, "claude", path, 0)));
    }

    [Fact]
    public void IsClaudeDesktop_requires_claude_name()
    {
        Assert.False(HostClassifier.IsClaudeDesktop(new ProcEntry(1, 0, "updater", DesktopPath, 0)));
    }
}
