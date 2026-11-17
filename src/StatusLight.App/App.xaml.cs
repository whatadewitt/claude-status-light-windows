using System.IO;
using System.Reflection;
using StatusLight.App.Floating;
using StatusLight.App.Platform;
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
    private SettingsStore? _store;
    private bool? _startAtLogin;
    private AppHost? _host;
    private IconCache? _icons;
    private TrayController? _tray;
    private FloatingController? _floating;

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

        Log.Info($"Claude Status Light {Version()} starting");

        // A tray app has no window to show an error in; never let one exception make the light vanish.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        // WinForms (tray menu) exceptions: log instead of showing the default error dialog. The mode
        // must be set before the first WinForms control exists, i.e. before the tray is created.
        try
        {
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("SetUnhandledExceptionMode failed", ex);
        }
        System.Windows.Forms.Application.ThreadException += (_, args) => Log.Error("Unhandled WinForms exception", args.Exception);

        var store = _store = new SettingsStore(SettingsPath);
        _host = new AppHost(store, Dispatcher);
        _icons = new IconCache();
        _tray = new TrayController(_host, store, _icons, Shutdown);
        _floating = new FloatingController(_host, store, _icons);
        ApplyStartAtLogin(_host.Settings);
        store.Changed += ApplyStartAtLogin;
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_store is not null) _store.Changed -= ApplyStartAtLogin;
        _floating?.Dispose();
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

    /// <summary>
    /// Syncs the HKCU Run value with the setting at every launch (so it follows the exe if it
    /// moves) and whenever StartAtLogin changes. A registry failure never crashes the app.
    /// </summary>
    private void ApplyStartAtLogin(AppSettings s)
    {
        if (_startAtLogin == s.StartAtLogin || Environment.ProcessPath is not { } exe) return;
        try
        {
            new StartupRegistration().Apply(s.StartAtLogin, exe);
            _startAtLogin = s.StartAtLogin; // a failed write is retried on the next settings change
        }
        catch (Exception e)
        {
            Log.Error("Start-at-login registration failed", e);
        }
    }

    private static string Version() =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
