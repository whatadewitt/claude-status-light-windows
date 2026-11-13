using StatusLight.App;

namespace StatusLight.Windows.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sllog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    [Fact]
    public void Creates_the_directory_and_appends_lines()
    {
        var log = new FileLog(Path.Combine(_dir, "log.txt"));
        log.Info("hello");
        log.Error("ctx", new InvalidOperationException("boom"));

        var lines = File.ReadAllLines(log.Path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("INFO hello", lines[0]);
        Assert.Contains("ERROR ctx: System.InvalidOperationException: boom", lines[1]);
    }

    [Fact]
    public void Rolls_to_log_1_when_full()
    {
        var log = new FileLog(Path.Combine(_dir, "log.txt"), maxBytes: 100);
        log.Info(new string('a', 120));
        log.Info("after roll");

        Assert.Equal(Path.Combine(_dir, "log.1.txt"), log.RolledPath);
        Assert.Contains(new string('a', 120), File.ReadAllText(log.RolledPath));
        Assert.Contains("after roll", File.ReadAllText(log.Path));
        Assert.DoesNotContain("aaaa", File.ReadAllText(log.Path));
    }

    [Fact]
    public void Unwritable_path_never_throws()
    {
        var log = new FileLog(Path.Combine(_dir, "a\0b", "log.txt"));
        log.Info("x");
    }

    [Fact]
    public void Concurrent_writes_are_all_kept()
    {
        var log = new FileLog(Path.Combine(_dir, "log.txt"));
        Parallel.For(0, 100, i => log.Info($"line {i}"));
        Assert.Equal(100, File.ReadAllLines(log.Path).Length);
    }
}
