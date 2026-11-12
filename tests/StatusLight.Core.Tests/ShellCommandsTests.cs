using StatusLight.Core;

namespace StatusLight.Core.Tests;

public class ShellCommandsTests
{
    [Fact]
    public void ExtractsEvalCommand()
    {
        var argv = "/bin/bash -c source /home/u/.claude/shell-snapshots/snapshot-bash-1.sh && eval 'uv run python x.py' < /dev/null";
        Assert.Equal("uv run python x.py", ShellCommands.CommandFromArgv(argv));
    }

    [Fact]
    public void ExtractsEvalCommandBeforePwdSuffix()
    {
        var argv = "/bin/zsh -c source /Users/x/.claude/shell-snapshots/snapshot-zsh-123.sh 2>/dev/null || true "
            + "&& setopt NO_EXTENDED_GLOB 2>/dev/null || true "
            + "&& eval 'uv run python feature_engineering/batch_features.py --start-date 2022-01-01' "
            + "&& pwd -P >| /tmp/claude-4b51-cwd";
        Assert.Equal("uv run python feature_engineering/batch_features.py --start-date 2022-01-01",
            ShellCommands.CommandFromArgv(argv));
    }

    [Fact]
    public void UnescapesSingleQuotes()
    {
        var argv = "/bin/zsh -c source /x/.claude/shell-snapshots/snapshot-zsh-1.sh "
            + "&& eval 'echo '\\''hi there'\\''' && pwd -P >| /tmp/claude-1-cwd";
        Assert.Equal("echo 'hi there'", ShellCommands.CommandFromArgv(argv));
    }

    [Fact]
    public void CutsAtStdinRedirectTerminator()
    {
        var argv = "/bin/zsh -c source /x/.claude/shell-snapshots/snapshot-zsh-2.sh 2>/dev/null || true "
            + "&& eval 'sleep 240 && echo demo-done' < /dev/null && pwd -P >| /tmp/claude-9-cwd";
        Assert.Equal("sleep 240 && echo demo-done", ShellCommands.CommandFromArgv(argv));
    }

    [Fact]
    public void UnrecognizedShapeIsReturnedUnchanged()
    {
        var argv = "/bin/zsh -c do_thing --flag";
        Assert.Equal(argv, ShellCommands.CommandFromArgv(argv));
    }

    [Theory]
    [InlineData("bash -c source /home/u/.claude/shell-snapshots/snapshot-bash-1.sh", true)]
    [InlineData(@"bash -c source C:\Users\u\.claude\shell-snapshots\snapshot-bash-1.sh", true)]
    [InlineData("bash -c echo hi", false)]
    public void IsClaudeShellMatchesBothSlashForms(string line, bool expected)
        => Assert.Equal(expected, ShellCommands.IsClaudeShell(line));
}
