using System.IO;
using StatusLight.App.Rendering;
using StatusLight.App.Tray;
using StatusLight.Core;
using Application = System.Windows.Application;
using ExitEventArgs = System.Windows.ExitEventArgs;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace StatusLight.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\ClaudeStatusLight";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeStatusLight", "settings.json");

    private Mutex? _instance;
    private AppHost? _host;
    private IconCache? _icons;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        var store = new SettingsStore(SettingsPath);
        _host = new AppHost(store, Dispatcher);
        _icons = new IconCache();
        _tray = new TrayController(_host, store, _icons, Shutdown);
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _host?.Dispose();
        _icons?.Dispose();
        if (_instance is not null)
        {
            _instance.ReleaseMutex();
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
