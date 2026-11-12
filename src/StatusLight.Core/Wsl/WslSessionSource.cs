using StatusLight.Core.Sources;

namespace StatusLight.Core.Wsl;

/// <summary>
/// Reads sessions of running WSL distros through \\wsl.localhost. Only distros reported by
/// `wsl -l --running` are touched, so a stopped distro is never woken.
/// </summary>
public sealed class WslSessionSource(ICommandRunner wsl, Func<string, string> uncRoot, TimeSpan minInterval) : ISessionSource
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);
    private const string SessionsRelative = "/.claude/status-light/sessions";

    private readonly Dictionary<string, string> _homes = new();
    private readonly Dictionary<string, TranscriptTitleCache> _titles = new();
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = new();
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
    private IReadOnlyList<SessionState> _last = [];
    private DateTimeOffset? _lastPoll;

    public WslSessionSource(ICommandRunner wsl)
        : this(wsl, d => $@"\\wsl.localhost\{d}", TimeSpan.FromSeconds(3)) { }

    /// <summary>
    /// May block on 9P filesystem IO over \\wsl.localhost; call off the UI thread, never overlapping.
    /// </summary>
    public IReadOnlyList<SessionState> Poll(DateTimeOffset now)
    {
        if (_lastPoll is { } last && now >= last && now - last < minInterval) return _last;
        _lastPoll = now;
        _last = PollCore(now);
        return _last;
    }

    private IReadOnlyList<SessionState> PollCore(DateTimeOffset now)
    {
        var list = wsl.Run(["-l", "--running", "-q"], CommandTimeout);
        if (list.ExitCode != 0)
        {
            _homes.Clear();
            _titles.Clear();
            _retryAfter.Clear();
            return [];
        }

        var distros = WslOutput.ParseDistroList(list.Stdout);
        foreach (var stale in _homes.Keys.Where(k => !distros.Contains(k)).ToList()) _homes.Remove(stale);
        foreach (var stale in _titles.Keys.Where(k => !distros.Contains(k)).ToList()) _titles.Remove(stale);
        foreach (var stale in _retryAfter.Keys.Where(k => !distros.Contains(k)).ToList()) _retryAfter.Remove(stale);

        var result = new List<SessionState>();
        foreach (var distro in distros)
        {
            if (_retryAfter.TryGetValue(distro, out var retryAt) && now < retryAt) continue;
            if (!_homes.TryGetValue(distro, out var home))
            {
                var r = wsl.Run(["-d", distro, "-e", "sh", "-c", "printf %s \"$HOME\""], CommandTimeout);
                home = r.ExitCode == 0 ? r.Stdout.Replace("\0", "").Trim() : "";
                if (home.Length == 0) { _retryAfter[distro] = now + FailureBackoff; continue; } // don't cache failures
                _homes[distro] = home;
            }

            var dir = WslPaths.Map(uncRoot(distro), home + SessionsRelative);
            if (!HasSessionFiles(dir)) continue;

            var ps = wsl.Run(["-d", distro, "-e", "ps", "-eo", "pid=,ppid=,args="], CommandTimeout);
            if (ps.ExitCode != 0) { _retryAfter[distro] = now + FailureBackoff; continue; }
            _retryAfter.Remove(distro);

            if (!_titles.TryGetValue(distro, out var titles)) _titles[distro] = titles = new TranscriptTitleCache();

            var reader = new SessionDirectoryReader(
                dir, SessionOrigin.Wsl, distro,
                new WslProcessTable(WslOutput.ParsePs(ps.Stdout)),
                p => WslPaths.Map(uncRoot(distro), p),
                titles);
            result.AddRange(reader.Read(now));
        }

        result.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return result;
    }

    private static bool HasSessionFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.json").Any();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
