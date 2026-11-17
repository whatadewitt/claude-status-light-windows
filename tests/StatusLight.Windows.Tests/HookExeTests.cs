using System.Diagnostics;
using System.Text;
using StatusLight.Core.Hook;

namespace StatusLight.Windows.Tests;

/// <summary>Publishes status-hook.exe once (Native AOT) into a temp dir for <see cref="HookExeTests"/>.</summary>
public sealed class PublishedHook : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "slhookexe-" + Guid.NewGuid().ToString("N"));

    public string Exe => Path.Combine(_dir, "status-hook.exe");

    public PublishedHook()
    {
        var project = Path.Combine(RepoRoot(), "src", "StatusLight.Hook");
        var psi = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "publish", project, "-c", "Release", "-o", _dir, "--disable-build-servers" })
            psi.ArgumentList.Add(a);
        // Variables the outer `dotnet test` exports would point the nested build at the wrong MSBuild.
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(key);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet publish of status-hook timed out");
        }
        if (p.ExitCode != 0 || !File.Exists(Exe))
            throw new InvalidOperationException($"dotnet publish failed ({p.ExitCode}):\n{stdout.Result}\n{stderr.Result}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    private static string DotnetHost()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrEmpty(host) ? "dotnet" : host;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StatusLight.slnx"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException("StatusLight.slnx not found above " + AppContext.BaseDirectory);
    }
}

public sealed class HookExeTests(PublishedHook hook) : IClassFixture<PublishedHook>, IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "slhookhome-" + Guid.NewGuid().ToString("N"));

    private string SessionFile(string id) => Path.Combine(_home, ".claude", "status-light", "sessions", id + ".json");

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch (Exception) { }
    }

    private (int Exit, string Out, string Err) Run(string fileName, string arguments, string stdin)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["USERPROFILE"] = _home;

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} {arguments} did not exit");
        }
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void Direct_run_records_test_process_identity()
    {
        var (exit, stdout, stderr) = Run(hook.Exe, "working",
            """{"session_id":"direct","hook_event_name":"UserPromptSubmit","cwd":"C:\\proj"}""");

        Assert.Equal(0, exit);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
        var r = SessionFiles.TryRead(SessionFile("direct"));
        Assert.NotNull(r);
        Assert.Equal("working", r.State);
        Assert.Equal(@"C:\proj", r.Cwd);
        Assert.Equal(Environment.ProcessId, r.Pid);
        Assert.NotEqual(0, r.PidStart);
        var expectedStart = Process.GetCurrentProcess().StartTime.ToFileTimeUtc();
        Assert.InRange(r.PidStart, expectedStart - TimeSpan.TicksPerSecond, expectedStart + TimeSpan.TicksPerSecond);
    }

    [Fact]
    public void Run_through_cmd_skips_the_shell()
    {
        var (exit, _, _) = Run("cmd.exe", $"/c \"\"{hook.Exe}\" idle\"", """{"session_id":"viacmd"}""");

        Assert.Equal(0, exit);
        var r = SessionFiles.TryRead(SessionFile("viacmd"));
        Assert.NotNull(r);
        Assert.Equal("idle", r.State);
        Assert.Equal(Environment.ProcessId, r.Pid);
    }

    [Fact]
    public async Task Held_open_stdin_times_out_without_writing_a_session()
    {
        var psi = new ProcessStartInfo(hook.Exe, "working")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["USERPROFILE"] = _home;

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardInput.Write("""{"session_id":"held",""");
        p.StandardInput.Flush();
        // stdin stays open: the hook must give up on its own.
        var exited = p.WaitForExit(TimeSpan.FromSeconds(30));
        p.StandardInput.Close();
        if (!exited) p.Kill(entireProcessTree: true);

        Assert.True(exited);
        Assert.Equal(0, p.ExitCode);
        Assert.Equal("", await stdout);
        Assert.Equal("", await stderr);
        Assert.False(File.Exists(SessionFile("default")));
        Assert.False(File.Exists(SessionFile("held")));
    }

    [Fact]
    public void Garbage_stdin_exits_zero_and_writes_default()
    {
        var (exit, stdout, stderr) = Run(hook.Exe, "idle", "not json");

        Assert.Equal(0, exit);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
        Assert.True(File.Exists(SessionFile("default")));
    }
}
