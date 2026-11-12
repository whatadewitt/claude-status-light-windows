using StatusLight.Core;
using StatusLight.Core.Sources;

namespace StatusLight.Core.Tests;

public sealed class SessionDirectoryReaderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);
    private readonly string _dir = Directory.CreateTempSubdirectory("sdr").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeTable : IProcessTable
    {
        public Dictionary<int, long> Starts { get; } = new();
        public HashSet<int> Unknown { get; } = new();
        public Dictionary<int, string[]> Children { get; } = new();
        public List<int> ChildCalls { get; } = new();

        public ProcessLiveness Check(int pid, long pidStart)
        {
            if (Unknown.Contains(pid)) return ProcessLiveness.Unknown;
            if (!Starts.TryGetValue(pid, out var start)) return ProcessLiveness.Dead;
            return pidStart != 0 && pidStart != start ? ProcessLiveness.Dead : ProcessLiveness.Alive;
        }

        public IReadOnlyList<string> ChildCommandLines(int pid)
        {
            ChildCalls.Add(pid);
            return Children.TryGetValue(pid, out var c) ? c : [];
        }
    }

    private string Write(string id, string state, int pid, long pidStart = 0, long? updated = null)
    {
        var path = Path.Combine(_dir, id + ".json");
        File.WriteAllText(path,
            $$"""{"state":"{{state}}","session_id":"{{id}}","cwd":"/p/x","pid":{{pid}},"pid_start":{{pidStart}},"updated_at":{{updated ?? Now.ToUnixTimeSeconds()}}}""");
        return path;
    }

    private SessionDirectoryReader Reader(FakeTable t) =>
        new(_dir, SessionOrigin.Windows, null, t, p => p);

    [Fact]
    public void Dead_pid_removes_files()
    {
        var path = Write("a", "idle", 42);
        var agents = Path.Combine(_dir, "a.agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "g1"), "");

        Assert.Empty(Reader(new FakeTable()).Read(Now));
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(agents));
    }

    [Fact]
    public void Reused_pid_with_different_start_time_is_dead()
    {
        var t = new FakeTable();
        t.Starts[42] = 9;
        var path = Write("a", "idle", 42, pidStart: 5);

        Assert.Empty(Reader(t).Read(Now));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Unknown_liveness_keeps_session()
    {
        var t = new FakeTable();
        t.Unknown.Add(42);
        var path = Write("a", "attention", 42);

        var s = Assert.Single(Reader(t).Read(Now));
        Assert.Equal(LightState.Attention, s.State);
        Assert.Equal(42, s.Pid);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Stale_pidless_session_skipped_not_deleted()
    {
        var path = Write("a", "idle", 0, updated: Now.ToUnixTimeSeconds() - 13 * 3600);

        Assert.Empty(Reader(new FakeTable()).Read(Now));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Idle_with_claude_shell_child_becomes_working_with_command()
    {
        var t = new FakeTable();
        t.Starts[42] = 1;
        t.Children[42] =
        [
            @"cmd /c ""echo .claude\shell-snapshots\snapshot-x && eval 'ping -n 5 127.0.0.1'""",
            "notepad.exe",
        ];
        Write("a", "idle", 42);

        var s = Assert.Single(Reader(t).Read(Now));
        Assert.Equal(LightState.Working, s.State);
        Assert.Single(s.Shells);
    }

    [Fact]
    public void Working_session_is_not_shell_scanned()
    {
        var t = new FakeTable();
        t.Starts[42] = 1;
        Write("a", "working", 42);

        Reader(t).Read(Now);
        Assert.Empty(t.ChildCalls);
    }

    [Fact]
    public void Partial_json_skipped_and_kept()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{\"state\":\"idle\",\"sess");

        Assert.Empty(Reader(new FakeTable()).Read(Now));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Agents_counted_from_markers()
    {
        var t = new FakeTable();
        t.Starts[42] = 1;
        Write("a", "working", 42);
        var agents = Path.Combine(_dir, "a.agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "g1"), "");
        File.WriteAllText(Path.Combine(agents, "g2"), "");

        Assert.Equal(2, Assert.Single(Reader(t).Read(Now)).Agents);
    }
}
