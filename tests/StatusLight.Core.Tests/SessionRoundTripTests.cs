using StatusLight.Core;
using StatusLight.Core.Hook;
using StatusLight.Core.Sources;

namespace StatusLight.Core.Tests;

/// <summary>What the hook writes is exactly what the app reads back.</summary>
public sealed class SessionRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("slrt").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class AliveTable(int pid, long pidStart) : IProcessTable
    {
        public ProcessLiveness Check(int p, long start) =>
            p == pid && start == pidStart ? ProcessLiveness.Alive : ProcessLiveness.Dead;

        public IReadOnlyList<string> ChildCommandLines(int p) => [];
    }

    [Fact]
    public void Hook_written_session_reads_back_with_every_field()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var identity = new ProcessIdentity(4242, 133_000_000_000_000_000, "console", "console:4242", 0x1A2B3C);
        var runner = new HookRunner(_dir, () => identity, () => now, () => "m.1");

        runner.Run("attention",
            """{"session_id":"rt-1","hook_event_name":"Notification","cwd":"C:\\proj\\app","permission_mode":"plan"}""",
            @"C:\fallback");

        var reader = new SessionDirectoryReader(_dir, SessionOrigin.Windows, null,
            new AliveTable(identity.Pid, identity.PidStart), p => p);
        var s = Assert.Single(reader.Read(now));

        Assert.Equal("rt-1", s.SessionId);
        Assert.Equal(LightState.Attention, s.State);
        Assert.Equal(@"C:\proj\app", s.Cwd);
        Assert.Equal("console:4242", s.Tty);
        Assert.Equal("console", s.TermProgram);
        Assert.Equal(4242, s.Pid);
        Assert.Equal(identity.PidStart, s.PidStart);
        Assert.Equal(0x1A2B3C, s.Hwnd);
        Assert.Equal("plan", s.PermissionMode);
        Assert.Equal(HostTag.PowerShell, s.Tag);
        Assert.Equal(now, s.UpdatedAt);
    }
}
