namespace StatusLight.Core.Sources;

public sealed record MonitorSnapshot(
    IReadOnlyList<SessionState> Sessions,
    LightState Aggregate,
    int TotalAgents,
    bool AnyWorking,
    bool PopOut);

public sealed class SessionMonitor(IReadOnlyList<ISessionSource> sources, Func<bool> greenBeatsYellow)
{
    private HashSet<(SessionOrigin, string?, string)> _previousAttention = [];

    public MonitorSnapshot Poll(DateTimeOffset now)
    {
        var all = new List<SessionState>();
        foreach (var source in sources)
        {
            try { all.AddRange(source.Poll(now)); }
            catch (Exception) { /* a failing source contributes nothing this tick */ }
        }
        all.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));

        var attention = all
            .Where(s => s.State == LightState.Attention)
            .Select(s => (s.Origin, s.Distro, s.SessionId))
            .ToHashSet();
        var popOut = attention.Any(k => !_previousAttention.Contains(k));
        _previousAttention = attention;

        return new MonitorSnapshot(
            all,
            LightStates.Aggregate(all.Select(s => s.State), greenBeatsYellow()),
            all.Sum(s => s.Agents),
            all.Any(s => s.State == LightState.Working),
            popOut);
    }
}
