using System.Runtime.Versioning;
using System.Text;
using StatusLight.Core.Hook;

namespace StatusLight.Hook;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static readonly TimeSpan StdinCap = TimeSpan.FromSeconds(2);

    /// <summary>`status-hook.exe &lt;state&gt;`: records the hook event. Always exits 0 and never writes to stdout/stderr.</summary>
    private static int Main(string[] args)
    {
        try
        {
            var state = args.Length > 0 ? args[0] : "idle";
            var payload = ReadStdin();
            new HookRunner(SessionsDir(), Identify, () => DateTimeOffset.UtcNow, HookRunner.DefaultMarkerName)
                .Run(state, payload, Environment.CurrentDirectory);
        }
        catch (Exception)
        {
            // The hook must never fail Claude Code.
        }
        return 0;
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
        return HostClassifier.Classify(ancestors, env, WindowsProcessChain.ConsoleWindow());
    }

    /// <summary>Reads stdin on a background task, giving up after <see cref="StdinCap"/> so a held-open pipe can't hang Claude Code.</summary>
    private static string ReadStdin()
    {
        if (!Console.IsInputRedirected) return "";
        var read = Task.Run(() =>
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            return reader.ReadToEnd();
        });
        try
        {
            return read.Wait(StdinCap) ? read.Result : "";
        }
        catch (AggregateException)
        {
            return "";
        }
    }
}
