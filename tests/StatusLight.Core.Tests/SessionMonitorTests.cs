using StatusLight.Core;
using StatusLight.Core.Sources;

namespace StatusLight.Core.Tests;

public sealed class SessionMonitorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

    private sealed class FakeSource(Func<IReadOnlyList<SessionState>> poll) : ISessionSource
    {
        public IReadOnlyList<SessionState> Poll(DateTimeOffset now) => poll();
    }

    private static SessionState S(string id, LightState state) => new() { SessionId = id, State = state, UpdatedAt = Now };

    [Fact]
    public void Monitor_pops_out_only_on_new_attention()
    {
        IReadOnlyList<SessionState> current = [S("A", LightState.Attention)];
        var m = new SessionMonitor([new FakeSource(() => current)], () => false);

        Assert.True(m.Poll(Now).PopOut);
        Assert.False(m.Poll(Now).PopOut);
        current = [S("A", LightState.Attention), S("B", LightState.Attention)];
        var snap = m.Poll(Now);
        Assert.True(snap.PopOut);
        Assert.Equal(LightState.Attention, snap.Aggregate);
    }

    [Fact]
    public void Monitor_survives_throwing_source()
    {
        var m = new SessionMonitor(
            [new FakeSource(() => throw new IOException("boom")), new FakeSource(() => [S("A", LightState.Working)])],
            () => false);

        var snap = m.Poll(Now);
        Assert.Single(snap.Sessions);
        Assert.True(snap.AnyWorking);
    }
}
