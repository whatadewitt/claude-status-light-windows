using StatusLight.Core;
using StatusLight.Core.Sources;

namespace StatusLight.Core.Tests;

public sealed class AvailabilityGatedSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(60);

    private sealed class FakeSource : ISessionSource
    {
        public int Polls { get; private set; }
        public IReadOnlyList<SessionState> Result { get; } =
            [new SessionState { SessionId = "a", State = LightState.Idle, UpdatedAt = Now }];

        public IReadOnlyList<SessionState> Poll(DateTimeOffset now)
        {
            Polls++;
            return Result;
        }
    }

    [Fact]
    public void Unavailable_never_creates_the_source_and_rechecks_only_after_the_interval()
    {
        var checks = 0;
        var creates = 0;
        var src = new AvailabilityGatedSource(() => { checks++; return false; }, () => { creates++; return new FakeSource(); }, Recheck);

        Assert.Empty(src.Poll(Now));
        Assert.Empty(src.Poll(Now + TimeSpan.FromSeconds(59)));
        Assert.Equal(1, checks);
        Assert.Empty(src.Poll(Now + Recheck));
        Assert.Equal(2, checks);
        Assert.Equal(0, creates);
    }

    [Fact]
    public void Becoming_available_creates_the_source_once_and_polls_it()
    {
        var available = false;
        var creates = 0;
        var inner = new FakeSource();
        var src = new AvailabilityGatedSource(() => available, () => { creates++; return inner; }, Recheck);

        Assert.Empty(src.Poll(Now));
        available = true;
        Assert.Empty(src.Poll(Now + TimeSpan.FromSeconds(30))); // not rechecked yet
        Assert.Same(inner.Result, src.Poll(Now + Recheck));
        Assert.Same(inner.Result, src.Poll(Now + Recheck + TimeSpan.FromSeconds(1)));
        Assert.Equal(1, creates);
        Assert.Equal(2, inner.Polls);
    }

    [Fact]
    public void Throwing_check_counts_as_unavailable()
    {
        var src = new AvailabilityGatedSource(() => throw new InvalidOperationException(), () => new FakeSource(), Recheck);
        Assert.Empty(src.Poll(Now));
    }

    [Fact]
    public void Becoming_unavailable_stops_polling_the_source()
    {
        var available = true;
        var inner = new FakeSource();
        var src = new AvailabilityGatedSource(() => available, () => inner, Recheck);

        src.Poll(Now);
        available = false;
        Assert.Empty(src.Poll(Now + Recheck));
        Assert.Equal(1, inner.Polls);
    }
}
