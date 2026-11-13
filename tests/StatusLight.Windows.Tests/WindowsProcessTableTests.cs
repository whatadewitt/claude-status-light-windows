using System.Diagnostics;
using System.Runtime.Versioning;
using StatusLight.App.Platform;
using StatusLight.Core.Sources;

namespace StatusLight.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessTableTests
{
    private readonly WindowsProcessTable _table = new();

    private static long StartFileTime(Process p) => p.StartTime.ToFileTime();

    [Fact]
    public void Current_process_with_true_start_time_is_alive()
    {
        using var me = Process.GetCurrentProcess();
        Assert.Equal(ProcessLiveness.Alive, _table.Check(me.Id, StartFileTime(me)));
    }

    [Fact]
    public void Current_pid_with_wrong_start_time_is_dead()
    {
        using var me = Process.GetCurrentProcess();
        Assert.Equal(ProcessLiveness.Dead, _table.Check(me.Id, StartFileTime(me) + 10_000_000));
    }

    [Fact]
    public void Exited_child_is_dead()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd", "/c exit 0") { CreateNoWindow = true })!;
        child.WaitForExit();
        Assert.Equal(ProcessLiveness.Dead, _table.Check(child.Id, 0));
    }

    [Fact]
    public void Spawned_claude_shell_appears_in_child_command_lines()
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            Arguments = @"/c ""echo .claude\shell-snapshots\snapshot-x && ping -n 5 127.0.0.1""",
            CreateNoWindow = true,
        };
        using var child = Process.Start(psi)!;
        try
        {
            var found = false;
            for (var i = 0; i < 20 && !found; i++)
            {
                found = _table.ChildCommandLines(Environment.ProcessId)
                    .Any(l => l.Contains("shell-snapshots", StringComparison.Ordinal));
                if (!found) Thread.Sleep(100);
            }
            Assert.True(found);
        }
        finally
        {
            try { child.Kill(true); } catch (Exception) { }
        }
    }

    [Fact]
    public void Non_shell_children_are_not_reported()
    {
        var psi = new ProcessStartInfo("ping.exe", "-n 5 127.0.0.1") { CreateNoWindow = true };
        using var child = Process.Start(psi)!;
        try
        {
            Thread.Sleep(200);
            Assert.DoesNotContain(_table.ChildCommandLines(Environment.ProcessId),
                l => l.Contains("127.0.0.1", StringComparison.Ordinal));
        }
        finally
        {
            try { child.Kill(true); } catch (Exception) { }
        }
    }
}
