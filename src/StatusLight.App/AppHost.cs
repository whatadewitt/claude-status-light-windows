using System.IO;
using System.Windows.Threading;
using StatusLight.App.Platform;
using StatusLight.Core;
using StatusLight.Core.Sources;
using StatusLight.Core.Wsl;

namespace StatusLight.App;

/// <summary>
/// Composition root: owns the session sources and monitor, polls them off the UI thread, and
/// drives the dance. <see cref="Updated"/> fires on the UI thread after every poll and every
/// dance frame, so subscribers re-render from <see cref="Current"/> and <see cref="DanceFrame"/>.
/// </summary>
public sealed class AppHost : IDisposable
{
    public static string SessionsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "status-light", "sessions");

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WslMinInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WslPresenceRecheck = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DanceInterval = TimeSpan.FromSeconds(0.5);

    private readonly SettingsStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly SessionMonitor _monitor;
    private readonly DanceSequencer _dance = new(new Random());
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _danceTimer;
    private FileSystemWatcher? _watcher;
    private volatile AppSettings _settings;
    private bool _polling;
    private bool _pollAgain;
    private bool _disposed;

    public AppHost(SettingsStore store, Dispatcher dispatcher)
    {
        _store = store;
        _dispatcher = dispatcher;
        _settings = store.Load();
        _store.Changed += OnSettingsChanged;

        var local = new LocalWindowsSource(new SessionDirectoryReader(
            SessionsDir, SessionOrigin.Windows, null, new WindowsProcessTable(), p => p));
        // WSL reads may block on 9P IO: refresh them in the background so the native light never waits.
        // Without any registered distro, nothing WSL-related is created or polled (re-checked each minute).
        var wsl = new AvailabilityGatedSource(
            WslPresence.HasDistros,
            () => new BackgroundRefreshSource(
                new WslSessionSource(new WslCommandRunner(), d => $@"\\wsl.localhost\{d}", WslMinInterval),
                () => _dispatcher.BeginInvoke(RequestPoll),
                e => Log.Error("WSL refresh failed", e)),
            WslPresenceRecheck);
        _monitor = new SessionMonitor([local, wsl], () => _settings.GreenBeatsYellow);

        _pollTimer = NewTimer(PollInterval, RequestPoll);
        _debounceTimer = NewTimer(WatchDebounce, () =>
        {
            _debounceTimer!.Stop(); // assigned before the timer can tick
            RequestPoll();
        });
        _danceTimer = NewTimer(DanceInterval, () =>
        {
            try { _dance.Tick(); } catch (Exception e) { Log.Error("Dance tick failed", e); }
            RaiseUpdated();
        });
    }

    public event Action<MonitorSnapshot>? Updated;

    public MonitorSnapshot Current { get; private set; } = new([], LightState.Off, 0, false, false);

    public int DanceFrame => _dance.Frame;

    /// <summary>The latest saved settings (kept current through <see cref="SettingsStore.Changed"/>).</summary>
    public AppSettings Settings => _settings;

    public void Start()
    {
        StartWatching();
        _pollTimer.Start();
        RequestPoll();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Changed -= OnSettingsChanged;
        _pollTimer.Stop();
        _debounceTimer.Stop();
        _danceTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Runs one poll on the thread pool. Polls never overlap: the sources' caches are not
    /// thread-safe (WSL refreshes run separately inside <see cref="BackgroundRefreshSource"/>).
    /// A request during a poll queues exactly one follow-up so a file change seen mid-poll is not
    /// lost. Called on the UI thread only, so the flags need no locking and the await resumes on
    /// the dispatcher.
    /// </summary>
    private async void RequestPoll()
    {
        if (_disposed) return;
        if (_polling)
        {
            _pollAgain = true;
            return;
        }

        _polling = true;
        try
        {
            do
            {
                _pollAgain = false;
                MonitorSnapshot snapshot;
                try
                {
                    snapshot = await Task.Run(() => _monitor.Poll(DateTimeOffset.UtcNow));
                }
                catch (Exception e)
                {
                    Log.Error("Poll failed", e);
                    break; // SessionMonitor already isolates sources; next tick retries
                }
                if (_disposed) return;
                Apply(snapshot);
            }
            while (_pollAgain && !_disposed);
        }
        finally
        {
            _polling = false;
        }
    }

    private void Apply(MonitorSnapshot snapshot)
    {
        Current = snapshot;
        if (snapshot.AnyWorking)
        {
            if (!_danceTimer.IsEnabled) _danceTimer.Start();
        }
        else
        {
            _danceTimer.Stop();
            _dance.Reset();
        }
        RaiseUpdated();
    }

    /// <summary>A failing subscriber must not stop polling or the dance.</summary>
    private void RaiseUpdated()
    {
        try
        {
            Updated?.Invoke(Current);
        }
        catch (Exception e)
        {
            Log.Error("Updated subscriber failed", e);
        }
    }

    private DispatcherTimer NewTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = interval };
        timer.Tick += (_, _) => tick();
        return timer;
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(SessionsDir);
            var watcher = new FileSystemWatcher(SessionsDir)
            {
                IncludeSubdirectories = true, // <id>.agents\ subagent markers
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Changed += OnSessionsChanged;
            watcher.Created += OnSessionsChanged;
            watcher.Deleted += OnSessionsChanged;
            watcher.Renamed += OnSessionsChanged;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception)
        {
            // The 1 s poll still covers everything; the watcher only makes changes feel instant.
        }
    }

    // FileSystemWatcher raises on a thread-pool thread; restart the debounce on the dispatcher.
    private void OnSessionsChanged(object sender, FileSystemEventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });

    private void OnSettingsChanged(AppSettings s)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnSettingsChanged(s));
            return;
        }
        _settings = s;
        RequestPoll(); // GreenBeatsYellow changes the aggregate
    }
}
