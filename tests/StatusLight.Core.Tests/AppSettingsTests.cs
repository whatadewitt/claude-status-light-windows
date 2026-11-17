using StatusLight.Core;

namespace StatusLight.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("settings").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsPath => Path.Combine(_dir, "ClaudeStatusLight", "settings.json");

    [Fact]
    public void Defaults_match_global_constraints()
    {
        var s = new AppSettings();
        Assert.True(s.ShowFloatingWindow);
        Assert.True(s.LockToCorner);
        Assert.Equal(ScreenCorner.TopRight, s.Corner);
        Assert.False(s.AutoHide);
        Assert.True(s.GreenBeatsYellow);
        Assert.True(s.StartAtLogin);
        Assert.Null(s.FloatingX);
        Assert.Null(s.FloatingY);
        Assert.Null(s.FloatingMonitor);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(new AppSettings(), new SettingsStore(SettingsPath).Load());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("{\"corner\":\"Sideways\"}")]
    public void Corrupt_file_gives_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);

        Assert.Equal(new AppSettings(), new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Missing_keys_keep_their_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{\"autoHide\":true}");

        Assert.Equal(new AppSettings { AutoHide = true }, new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Round_trip_preserves_every_field_and_notifies()
    {
        var store = new SettingsStore(SettingsPath);
        AppSettings? notified = null;
        store.Changed += s => notified = s;
        var saved = new AppSettings
        {
            ShowFloatingWindow = false,
            LockToCorner = false,
            Corner = ScreenCorner.BottomLeft,
            AutoHide = true,
            GreenBeatsYellow = false,
            StartAtLogin = false,
            FloatingX = 120.5,
            FloatingY = -40,
            FloatingMonitor = @"\\.\DISPLAY2",
        };

        store.Save(saved);

        Assert.Equal(saved, new SettingsStore(SettingsPath).Load());
        Assert.Equal(saved, notified);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp"));
    }

    [Fact]
    public void Save_to_unwritable_path_does_not_throw_and_still_notifies()
    {
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "a file where the settings directory should be");
        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));
        AppSettings? notified = null;
        store.Changed += s => notified = s;

        store.Save(new AppSettings { AutoHide = true });

        Assert.Equal(new AppSettings { AutoHide = true }, notified);
    }
}
