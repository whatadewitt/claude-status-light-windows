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
            @"bash -c ""source C:\u\.claude\shell-snapshots\snapshot-x.sh && eval 'ls -la' < /dev/null""",
            "notepad.exe",
        ];
        Write("a", "idle", 42);

        var s = Assert.Single(Reader(t).Read(Now));
        Assert.Equal(LightState.Working, s.State);
        Assert.Equal(["ls -la"], s.Shells);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pid_zero_or_one_means_no_pid_and_fresh_session_kept(int pid)
    {
        Write("a", "idle", pid);

        var s = Assert.Single(Reader(new FakeTable()).Read(Now));
        Assert.Null(s.Pid);
    }

    [Fact]
    public void Invalid_state_with_live_pid_skipped_and_kept()
    {
        var t = new FakeTable();
        t.Starts[42] = 1;
        var path = Write("a", "bogus", 42);

        Assert.Empty(Reader(t).Read(Now));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Invalid_state_with_dead_pid_is_cleaned_up()
    {
        var path = Write("a", "bogus", 42);

        Assert.Empty(Reader(new FakeTable()).Read(Now));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Id_falls_back_to_file_name()
    {
        File.WriteAllText(Path.Combine(_dir, "from-name.json"),
            $$"""{"state":"idle","pid":0,"updated_at":{{Now.ToUnixTimeSeconds()}}}""");

        Assert.Equal("from-name", Assert.Single(Reader(new FakeTable()).Read(Now)).SessionId);
    }

    [Fact]
    public void Transcript_path_is_mapped_before_title_lookup()
    {
        var transcript = Path.Combine(_dir, "real-transcript.jsonl");
        File.WriteAllText(transcript, "{\"type\":\"ai-title\",\"aiTitle\":\"Mapped title\"}\n");
        File.WriteAllText(Path.Combine(_dir, "a.json"),
            $$"""{"state":"idle","session_id":"a","pid":0,"transcript_path":"/linux/path.jsonl","updated_at":{{Now.ToUnixTimeSeconds()}}}""");
        var reader = new SessionDirectoryReader(_dir, SessionOrigin.Wsl, "Ubuntu", new FakeTable(),
            p => p == "/linux/path.jsonl" ? transcript : "");

        var s = Assert.Single(reader.Read(Now));
        Assert.Equal("Mapped title", s.Title);
        Assert.Equal("Ubuntu", s.Distro);
        Assert.Equal(SessionOrigin.Wsl, s.Origin);
    }

    [Fact]
    public void Sorted_by_updated_at_descending()
    {
        var n = Now.ToUnixTimeSeconds();
        Write("old", "idle", 0, updated: n - 100);
        Write("new", "idle", 0, updated: n);
        Write("mid", "idle", 0, updated: n - 50);

        Assert.Equal(["new", "mid", "old"], Reader(new FakeTable()).Read(Now).Select(s => s.SessionId));
    }
}
