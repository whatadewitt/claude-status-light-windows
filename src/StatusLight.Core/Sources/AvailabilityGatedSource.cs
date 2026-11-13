namespace StatusLight.Core.Sources;

/// <summary>
/// Polls a source only while <paramref name="isAvailable"/> says its backend exists (e.g. WSL is
/// installed), re-checking at most every <paramref name="recheckInterval"/>. The source is created
/// on first availability, so an absent backend costs nothing. A throwing check counts as
/// unavailable. Not thread-safe: polls must not overlap (as with <see cref="SessionMonitor"/>).
/// </summary>
public sealed class AvailabilityGatedSource(
    Func<bool> isAvailable, Func<ISessionSource> create, TimeSpan recheckInterval) : ISessionSource
{
    private ISessionSource? _inner;
    private bool _available;
    private DateTimeOffset? _nextCheck;

    public IReadOnlyList<SessionState> Poll(DateTimeOffset now)
    {
        if (_nextCheck is not { } next || now >= next)
        {
            _nextCheck = now + recheckInterval;
            try { _available = isAvailable(); }
            catch (Exception) { _available = false; }
        }
        if (!_available) return [];
        _inner ??= create();
        return _inner.Poll(now);
    }
}
