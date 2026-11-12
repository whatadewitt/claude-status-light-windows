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
    private IReadOnlyList<SessionState> _last = [];
    private DateTimeOffset? _lastPoll;

    public WslSessionSource(ICommandRunner wsl)
        : this(wsl, d => $@"\\wsl.localhost\{d}", TimeSpan.FromSeconds(3)) { }

    public IReadOnlyList<SessionState> Poll(DateTimeOffset now)
    {
        if (_lastPoll is { } last && now - last < minInterval) return _last;
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
            return [];
        }

        var distros = WslOutput.ParseDistroList(list.Stdout);
        foreach (var stale in _homes.Keys.Where(k => !distros.Contains(k)).ToList()) _homes.Remove(stale);

        var result = new List<SessionState>();
        foreach (var distro in distros)
        {
            if (!_homes.TryGetValue(distro, out var home))
            {
                var r = wsl.Run(["-d", distro, "-e", "sh", "-c", "printf %s \"$HOME\""], CommandTimeout);
                home = r.ExitCode == 0 ? r.Stdout.Replace("\0", "").Trim() : "";
                if (home.Length == 0) continue; // don't cache failures
                _homes[distro] = home;
            }

            var dir = WslPaths.Map(uncRoot(distro), home + SessionsRelative);
            if (!HasSessionFiles(dir)) continue;

            var ps = wsl.Run(["-d", distro, "-e", "ps", "-eo", "pid=,ppid=,args="], CommandTimeout);
            if (ps.ExitCode != 0) continue;

            var reader = new SessionDirectoryReader(
                dir, SessionOrigin.Wsl, distro,
                new WslProcessTable(WslOutput.ParsePs(ps.Stdout)),
                p => WslPaths.Map(uncRoot(distro), p));
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
