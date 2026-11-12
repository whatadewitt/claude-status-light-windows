using StatusLight.Core;
using StatusLight.Core.Wsl;

namespace StatusLight.Core.Tests;

public sealed class WslSessionSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);
    private readonly string _root = Directory.CreateTempSubdirectory("wsl").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeRunner : ICommandRunner
    {
        public List<string> Calls { get; } = new();
        public string Running { get; set; } = "";
        public string Home { get; set; } = "/home/u";
        public string Ps { get; set; } = "";

        public CommandResult Run(IReadOnlyList<string> args, TimeSpan timeout)
        {
            var joined = string.Join(' ', args);
            Calls.Add(joined);
            if (joined == "-l --running -q") return new(0, Running);
            if (joined.Contains("printf")) return new(0, Home);
            if (joined.Contains(" ps ")) return new(0, Ps);
            return new(1, "");
        }
    }

    private string SessionsDir(string distro = "Ubuntu") =>
        Path.Combine(_root, distro, "home", "u", ".claude", "status-light", "sessions");

    private string Write(string id, string state, int pid)
    {
        Directory.CreateDirectory(SessionsDir());
        var path = Path.Combine(SessionsDir(), id + ".json");
        File.WriteAllText(path,
            $$"""{"state":"{{state}}","session_id":"{{id}}","cwd":"/p/x","pid":{{pid}},"updated_at":{{Now.ToUnixTimeSeconds()}}}""");
        return path;
    }

    private WslSessionSource Source(FakeRunner r, TimeSpan? min = null) =>
        new(r, d => Path.Combine(_root, d), min ?? TimeSpan.Zero);

    [Fact]
    public void Stopped_distros_are_left_alone()
    {
        var r = new FakeRunner();
        Assert.Empty(Source(r).Poll(Now));
        Assert.Equal(["-l --running -q"], r.Calls);
    }

    [Fact]
    public void No_session_files_means_no_ps()
    {
        var r = new FakeRunner { Running = "Ubuntu\r\n" };
        Assert.Empty(Source(r).Poll(Now));
        Assert.Equal(2, r.Calls.Count);
        Assert.DoesNotContain(r.Calls, c => c.Contains(" ps "));
    }

    [Fact]
    public void Liveness_comes_from_ps()
    {
        var live = Write("a", "working", 100);
        var dead = Write("b", "idle", 200);
        var r = new FakeRunner { Running = "Ubuntu\n", Ps = "  100     1 claude\n" };

        var s = Assert.Single(Source(r).Poll(Now));
        Assert.Equal(SessionOrigin.Wsl, s.Origin);
        Assert.Equal("Ubuntu", s.Distro);
        Assert.Equal(HostTag.Wsl, s.Tag);
        Assert.True(File.Exists(live));
        Assert.False(File.Exists(dead));
    }

    [Fact]
    public void Background_shell_makes_idle_session_working()
    {
        Write("a", "idle", 100);
        var r = new FakeRunner
        {
            Running = "Ubuntu\n",
            Ps = "100 1 claude\n101 100 bash -c source /h/.claude/shell-snapshots/snapshot-bash-1.sh && eval 'sleep 5' < /dev/null\n",
        };
        Assert.Equal(LightState.Working, Assert.Single(Source(r).Poll(Now)).State);
    }

    [Fact]
    public void Distro_list_tolerates_utf16()
    {
        Assert.Equal(["Ubuntu"], WslOutput.ParseDistroList("U\0b\0u\0n\0t\0u\0\r\0\n\0"));
    }

    [Fact]
    public void ParsePs_skips_malformed_and_allows_empty_args()
    {
        var rows = WslOutput.ParsePs("  10   1 /bin/x -y\nbogus\n 11 10\n");
        Assert.Equal([new PsRow(10, 1, "/bin/x -y"), new PsRow(11, 10, "")], rows);
    }

    [Fact]
    public void Process_table_children_ordered_by_pid()
    {
        var t = new WslProcessTable([new(9, 1, "c"), new(5, 1, "a"), new(7, 2, "x")]);
        Assert.Equal(["a", "c"], t.ChildCommandLines(1));
        Assert.Equal(StatusLight.Core.Sources.ProcessLiveness.Alive, t.Check(5, 123));
        Assert.Equal(StatusLight.Core.Sources.ProcessLiveness.Dead, t.Check(6, 0));
    }

    [Fact]
    public void Home_is_cached_across_polls()
    {
        var r = new FakeRunner { Running = "Ubuntu\n" };
        var src = Source(r);
        src.Poll(Now);
        src.Poll(Now.AddSeconds(10));
        Assert.Equal(1, r.Calls.Count(c => c.Contains("printf")));
    }

    [Fact]
    public void Polls_inside_min_interval_are_throttled()
    {
        Write("a", "working", 100);
        var r = new FakeRunner { Running = "Ubuntu\n", Ps = "100 1 claude\n" };
        var src = Source(r, TimeSpan.FromSeconds(3));
        var first = src.Poll(Now);
        var second = src.Poll(Now.AddSeconds(1));
        Assert.Equal(1, r.Calls.Count(c => c == "-l --running -q"));
        Assert.Same(first, second);
    }
}
