using StatusLight.Core;
using StatusLight.Core.Sources;

namespace StatusLight.Core.Tests;

public sealed class BackgroundRefreshSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FakeSource(Func<int, IReadOnlyList<SessionState>> poll) : ISessionSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<SessionState> Poll(DateTimeOffset now) => poll(Interlocked.Increment(ref _calls));
    }

    private static IReadOnlyList<SessionState> List(string id) =>
        [new SessionState { SessionId = id, State = LightState.Idle, UpdatedAt = Now }];

    [Fact]
    public void First_poll_returns_empty_then_later_polls_return_the_inner_result()
    {
        var result = List("A");
        using var done = new ManualResetEventSlim();
        var src = new BackgroundRefreshSource(new FakeSource(_ => result), done.Set);

        Assert.Empty(src.Poll(Now));
        Assert.True(done.Wait(Timeout));

        Assert.Same(result, src.Poll(Now));
    }

    [Fact]
    public void Blocked_inner_poll_never_blocks_callers_or_starts_a_second_poll()
    {
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var result = List("A");
        var inner = new FakeSource(_ =>
        {
            entered.Set();
            gate.Wait();
            return result;
        });
        var src = new BackgroundRefreshSource(inner, done.Set);

        Assert.Empty(src.Poll(Now));
        Assert.True(entered.Wait(Timeout));
        for (var i = 0; i < 20; i++) Assert.Empty(src.Poll(Now));
        Assert.Equal(1, inner.Calls);

        gate.Set();
        Assert.True(done.Wait(Timeout));
        Assert.Same(result, src.Poll(Now));
    }

    [Fact]
    public void Inner_exception_keeps_previous_cache_and_allows_the_next_refresh()
    {
        var first = List("A");
        var third = List("B");
        var inner = new FakeSource(call => call switch
        {
            1 => first,
            2 => throw new IOException("9P went away"),
            _ => third,
        });
        var src = new BackgroundRefreshSource(inner);

        src.Poll(Now);
        Assert.True(SpinWait.SpinUntil(() => ReferenceEquals(src.Poll(Now), first), Timeout));

        // Keep polling until the failing refresh has run and a third refresh has been allowed.
        var seen = new List<IReadOnlyList<SessionState>>();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            var r = src.Poll(Now);
            seen.Add(r);
            return ReferenceEquals(r, third);
        }, Timeout));

        Assert.True(inner.Calls >= 3);
        Assert.All(seen.Take(seen.Count - 1), r => Assert.Same(first, r));
    }

    [Fact]
    public void Refreshed_is_invoked_after_completion_only_when_the_result_changes()
    {
        var result = List("A");
        var refreshes = 0;
        var inner = new FakeSource(_ => result); // a throttled source returns the same list
        var src = new BackgroundRefreshSource(inner, () => Interlocked.Increment(ref refreshes));

        src.Poll(Now);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref refreshes) == 1, Timeout));
        Assert.Same(result, src.Poll(Now)); // starts a second refresh returning the same list
        Assert.True(SpinWait.SpinUntil(() => inner.Calls >= 2, Timeout));
        Thread.Sleep(50);

        Assert.Equal(1, Volatile.Read(ref refreshes));
    }
}
