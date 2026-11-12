namespace StatusLight.Core.Sources;

/// <summary>
/// Decouples a slow source (WSL over 9P) from the poll loop: <see cref="Poll"/> returns the last
/// cached list immediately and, if no refresh is running, starts one on the thread pool. At most
/// one inner Poll runs at a time, so a non-thread-safe inner source is safe.
/// </summary>
/// <param name="refreshed">
/// Invoked on the thread pool after a refresh that changed the cached list (by reference).
/// Unchanged results — a throttled source returns the same list — and failures do not invoke it,
/// so a callback that polls again cannot loop.
/// </param>
public sealed class BackgroundRefreshSource(ISessionSource inner, Action? refreshed = null) : ISessionSource
{
    private volatile IReadOnlyList<SessionState> _cache = [];
    private int _inFlight;

    public IReadOnlyList<SessionState> Poll(DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) == 0)
            Task.Run(() => Refresh(now));
        return _cache;
    }

    private void Refresh(DateTimeOffset now)
    {
        var changed = false;
        try
        {
            var result = inner.Poll(now);
            changed = !ReferenceEquals(result, _cache);
            _cache = result;
        }
        catch (Exception)
        {
            // Keep the previous cache; the next Poll starts a fresh refresh.
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }

        if (!changed) return;
        try { refreshed?.Invoke(); } catch (Exception) { }
    }
}
