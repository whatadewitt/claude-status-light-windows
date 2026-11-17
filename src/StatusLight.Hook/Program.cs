using System.Runtime.Versioning;
using System.Text;
using StatusLight.Core.Hook;
using StatusLight.Core.Install;

namespace StatusLight.Hook;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static readonly TimeSpan StdinCap = TimeSpan.FromSeconds(2);

    /// <summary>`status-hook.exe &lt;state&gt;`: records the hook event. Always exits 0 and never writes to stdout/stderr.</summary>
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] is "--install-hooks" or "--uninstall-hooks")) return Settings(args);
        try
        {
            var state = args.Length > 0 ? args[0] : "idle";
            var payload = ReadStdin();
            // A timed-out or failed read means we don't know which session this is: record nothing.
            if (payload is null) return 0;
            new HookRunner(SessionsDir(), Identify, () => DateTimeOffset.UtcNow, HookRunner.DefaultMarkerName)
                .Run(state, payload, Environment.CurrentDirectory);
        }
        catch (Exception)
        {
            // The hook must never fail Claude Code.
        }
        return 0;
    }

    /// <summary>`--install-hooks|--uninstall-hooks &lt;settingsPath&gt; &lt;hookPath&gt;`: run by the installer. Exit 0 ok, 2 bad usage or invalid JSON, 1 unexpected failure.</summary>
    private static int Settings(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: status-hook.exe --install-hooks|--uninstall-hooks <settingsPath> <hookPath>");
            return 2;
        }
        try
        {
            var install = args[0] == "--install-hooks";
            var code = install
                ? SettingsFile.AddHooks(args[1], args[2], DateTimeOffset.UtcNow)
                : SettingsFile.RemoveHooks(args[1], args[2], DateTimeOffset.UtcNow);
            Console.WriteLine(code == 0
                ? $"status-light hooks {(install ? "installed in" : "removed from")} {args[1]}"
                : $"ERROR: {args[1]} was not changed: invalid JSON/UTF-8 or empty hook path.");
            return code;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static string SessionsDir()
    {
        var home = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrEmpty(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "status-light", "sessions");
    }

    private static ProcessIdentity Identify()
    {
        var env = new Dictionary<string, string?>
        {
            ["WT_SESSION"] = Environment.GetEnvironmentVariable("WT_SESSION"),
            ["TERM_PROGRAM"] = Environment.GetEnvironmentVariable("TERM_PROGRAM"),
        };
        var ancestors = WindowsProcessChain.Ancestors(Environment.ProcessId).Skip(1).ToList();
        var hwnd = WindowsProcessChain.ConsoleWindow();
        // Claude Code may start the hook with CREATE_NO_WINDOW; then borrow Claude's own console window.
        if (hwnd == 0 && HostClassifier.ClaudeProcess(ancestors) is { } claude)
            hwnd = WindowsProcessChain.ConsoleWindowOf(claude.Pid);
        return HostClassifier.Classify(ancestors, env, hwnd);
    }

    /// <summary>Reads stdin on a background task, giving up after <see cref="StdinCap"/> so a held-open pipe can't hang Claude Code.
    /// Returns null on timeout or read failure; "" when stdin isn't redirected.</summary>
    private static string? ReadStdin()
    {
        if (!Console.IsInputRedirected) return "";
        var read = Task.Run(() =>
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            return reader.ReadToEnd();
        });
        try
        {
            return read.Wait(StdinCap) ? read.Result : null;
        }
        catch (AggregateException)
        {
            return null;
        }
    }
}
